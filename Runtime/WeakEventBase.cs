// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wander
{
	/// <summary>
	/// Shared base for all <see cref="WeakEvent"/> and <see cref="ConcurrentWeakEvent"/> arities.<br/>
	/// <br/>
	/// Solves the <b>lapsed listener problem</b>: standard C# <c>event</c> fields hold <i>strong</i> references
	/// to subscribers, which prevents garbage collection and — in Unity — can invoke callbacks on destroyed
	/// <c>MonoBehaviour</c> instances, throwing <see cref="MissingReferenceException"/>.<br/>
	/// <br/>
	/// Subscribers are held via <see cref="WeakReference{T}"/>, so they are collected normally when no other
	/// strong reference keeps them alive. Dead or Unity-destroyed entries are pruned automatically on every
	/// <see cref="WeakEvent.Subscribe{TTarget}"/> and <see cref="WeakEvent.Invoke"/> call.<br/>
	/// <br/>
	/// Provides: a typed handler list, Unity-null detection, a per-thread snapshot buffer pool to avoid
	/// per-<c>Invoke</c> allocations, and a <b>Snapshot Pattern</b> invocation model that copies the handler
	/// list before calling handlers — allowing re-entrant <see cref="WeakEvent.Subscribe{TTarget}"/>,
	/// <see cref="WeakEventBase{TDelegate}.Unsubscribe"/>, and <see cref="WeakEvent.Invoke"/> calls
	/// from within a callback without corrupting the iterator.
	/// </summary>
	public abstract class WeakEventBase<TDelegate> where TDelegate : Delegate
	{
		/// <summary>Default initial capacity for the handler list and snapshot buffers. Pre-allocates enough slots to cover most events without an early resize.</summary>
		protected const int DEFAULT_CAPACITY = 16;

		// TODO: REPLACE WITH System.Collections.Generic.OrderedDictionary<int, (WeakReference<object>, TDelegate)> WHEN UNITY SWITCHES TO CORECLR.
		/// <summary>The list of active weak subscriptions. Each entry pairs a weak reference to the target object with its delegate and subscription hash.</summary>
		protected readonly List<(WeakReference<object> target, TDelegate method, int hash)> handlers;

		/// <summary>
		/// Per-thread pool of reusable snapshot lists. Grows to the max observed re-entrancy depth,
		/// then stabilizes with zero allocations. Always access via <see cref="RentSnapshotBuffer"/>
		/// and <see cref="ReturnSnapshotBuffer"/> — <c>[ThreadStatic]</c> fields are null on non-main threads.
		/// </summary>
		[ThreadStatic]
		private static Stack<List<(WeakReference<object> target, TDelegate method, int hash)>> threadSnapshotPool;

		/// <param name="initialCapacity">Initial capacity of the handler list. Pass a higher value if the event is expected to have many subscribers.</param>
		protected WeakEventBase(int initialCapacity = DEFAULT_CAPACITY)
		{
			handlers = new List<(WeakReference<object>, TDelegate, int)>(initialCapacity);
		}

		/// <summary>
		/// Removes the subscription identified by <paramref name="hash"/>.<br/>
		/// Pass the value returned by <see cref="WeakEvent.Subscribe{TTarget}"/>.
		/// </summary>
		/// <param name="hash">The subscription hash returned by <see cref="WeakEvent.Subscribe{TTarget}"/>.</param>
		public virtual void Unsubscribe(int hash)
		{
			for(int i = handlers.Count - 1; i >= 0; i--)
			{
				if(handlers[i].hash == hash)
				{
					handlers.RemoveAt(i);
					return;
				}
			}
		}

		/// <summary>Removes all dead or Unity-destroyed subscriptions. Called automatically on <see cref="WeakEvent.Subscribe{TTarget}"/>; call manually on rarely-invoked events to prevent unbounded list growth.</summary>
		public virtual void Prune()
		{
			PruneNoLock();
		}

		/// <summary>Removes all subscriptions. Use when the event owner is torn down and all observers should be dropped immediately.</summary>
		public virtual void Clear()
		{
			handlers.Clear();
		}

		/// <summary>
		/// Prunes dead entries, checks for duplicates, and adds the handler.<br/>
		/// Must be called while holding any required lock, or without a lock for <see cref="WeakEvent"/>.
		/// </summary>
		/// <returns>The subscription hash; pass to <see cref="Unsubscribe"/> to remove it.</returns>
		protected int TryAddHandlerNoLock(WeakReference<object> weakRef, TDelegate wrappedMethod, int hash)
		{
			PruneNoLock();

			// Subscribing the same method twice is almost always a bug — warn and skip.
			// Note: detection relies on delegate instance identity. Lambdas that capture outer variables
			// produce a new instance on each call and will not be detected as duplicates.
			for(int i = 0; i < handlers.Count; i++)
			{
				if(handlers[i].hash == hash)
				{
					Debug.LogWarning($"[WeakEvent] Duplicate subscription detected (hash {hash}). Ignoring.");
					return hash;
				}
			}

			handlers.Add((weakRef, wrappedMethod, hash));
			return hash;
		}

		/// <summary>
		/// Fills <paramref name="snapshot"/> with all live handlers, pruning dead entries in-place.<br/>
		/// Iterates backwards to allow safe in-place removal; the caller iterates the snapshot backwards
		/// to restore FIFO order (double-reverse).<br/>
		/// Must be called while holding any required lock, or without a lock for <see cref="WeakEvent"/>.
		/// </summary>
		protected void FillSnapshotNoLock(List<(WeakReference<object> target, TDelegate method, int hash)> snapshot)
		{
			for(int i = handlers.Count - 1; i >= 0; i--)
			{
				if(!handlers[i].target.TryGetTarget(out var target) || IsDestroyedUnityObject(target))
				{
					handlers.RemoveAt(i);
					continue;
				}

				snapshot.Add(handlers[i]);
			}
		}

		/// <summary>
		/// Pops a list from the per-thread pool, or allocates a fresh one if the pool is empty.<br/>
		/// Re-entrancy is safe: each nested <see cref="WeakEvent.Invoke"/> call gets its own list.<br/>
		/// Always pair with <see cref="ReturnSnapshotBuffer"/> inside a <c>try/finally</c> to guarantee the list is returned.
		/// </summary>
		/// <returns>A cleared snapshot list safe to iterate outside any lock.</returns>
		protected static List<(WeakReference<object> target, TDelegate method, int hash)> RentSnapshotBuffer()
		{
			// [ThreadStatic] fields are null on non-main threads, so the pool must be initialized on first access per thread.
			threadSnapshotPool ??= new Stack<List<(WeakReference<object>, TDelegate, int)>>();

			if(threadSnapshotPool.Count == 0)
			{
				// First call on this thread, or re-entrant depth exceeded pool size — allocate once, pool grows to max depth.
				return new List<(WeakReference<object>, TDelegate, int)>(DEFAULT_CAPACITY);
			}

			var pooled = threadSnapshotPool.Pop();
			pooled.Clear();
			return pooled;
		}

		/// <summary>Clears <paramref name="buffer"/> and returns it to the per-thread pool for reuse.</summary>
		/// <param name="buffer">The list previously obtained from <see cref="RentSnapshotBuffer"/>.</param>
		protected static void ReturnSnapshotBuffer(List<(WeakReference<object> target, TDelegate method, int hash)> buffer)
		{
			buffer.Clear();
			threadSnapshotPool ??= new Stack<List<(WeakReference<object>, TDelegate, int)>>();
			threadSnapshotPool.Push(buffer);
		}

		/// <summary>Prune body — must be called with any required lock already held.</summary>
		protected void PruneNoLock()
		{
			for(int i = handlers.Count - 1; i >= 0; i--)
			{
				if(!handlers[i].target.TryGetTarget(out var target) || IsDestroyedUnityObject(target))
				{
					handlers.RemoveAt(i);
				}
			}
		}

		/// <summary>
		/// Returns true if <paramref name="target"/> is a destroyed <see cref="UnityEngine.Object"/>.<br/>
		/// Such objects pass <c>TryGetTarget</c> but throw <see cref="MissingReferenceException"/> on use.
		/// </summary>
		protected static bool IsDestroyedUnityObject(object target)
		{
			return target is UnityEngine.Object obj && !obj;
		}
	}
}
