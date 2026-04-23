// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using System;
using UnityEngine;

namespace Wander
{
	/// <summary>
	/// Weak event with no parameters for single-threaded (main-thread) use. Subscribers are held weakly
	/// and collected automatically when their target is garbage-collected or destroyed, eliminating the
	/// lapsed listener memory leak common with standard C# <c>event</c> fields in Unity.<br/>
	/// <br/>
	/// For use across multiple threads, use <see cref="ConcurrentWeakEvent"/> instead.<br/>
	/// <br/>
	/// <b>Basic usage:</b>
	/// <code>
	/// // Declaration (on the event owner)
	/// public readonly WeakEvent onDeath = new();
	///
	/// // Subscribe — store the returned hash if manual unsubscription is needed
	/// int token = onDeath.Subscribe(this, self => self.HandleDeath());
	///
	/// // Unsubscribe manually (optional — not needed if the object will be destroyed)
	/// onDeath.Unsubscribe(token);
	///
	/// // Fire
	/// onDeath.Invoke();
	/// </code>
	/// <b>The <c>self</c> pattern:</b> always use the <c>self</c> parameter inside the lambda rather than
	/// capturing <c>this</c> directly. Capturing <c>this</c> creates a strong reference that defeats weak
	/// retention and prevents GC collection.<br/>
	/// <br/>
	/// <b>Parameterized variants:</b> use <see cref="WeakEvent{T0}"/> or <see cref="WeakEvent{T0, T1}"/>
	/// when the event carries data.<br/>
	/// <br/>
	/// <b>Re-entrancy:</b> handlers may call <see cref="Subscribe{TTarget}"/>,
	/// <see cref="WeakEventBase{TDelegate}.Unsubscribe"/>, or <see cref="Invoke"/> on the same event
	/// from within a callback without corrupting the iterator (Snapshot Pattern).<br/>
	/// <br/>
	/// <b>Invocation order:</b> handlers fire in FIFO (subscription) order.<br/>
	/// <b>Allocation:</b> after warm-up, <see cref="Invoke"/> is allocation-free; <see cref="Subscribe{TTarget}"/>
	/// allocates one delegate wrapper and one <see cref="WeakReference{T}"/> per call.
	/// </summary>
	public class WeakEvent : WeakEventBase<Action<object>>
	{
		/// <param name="initialCapacity">Initial capacity of the handler list. Increase if the event is expected to have many subscribers.</param>
		public WeakEvent(int initialCapacity = DEFAULT_CAPACITY) : base(initialCapacity) { }

		/// <summary>
		/// Subscribes <paramref name="target"/> as an observer.<br/>
		/// Use <c>self</c> inside the lambda — do not capture <c>this</c> directly,
		/// as that would create a strong reference and prevent GC collection.
		/// <code>
		/// onDeath.Subscribe(this, self => self.HandleDeath());
		/// </code>
		/// </summary>
		/// <typeparam name="TTarget">The type of the subscribing object.</typeparam>
		/// <param name="target">The subscribing object, held weakly.</param>
		/// <param name="method">The callback to invoke; receives the target as its first argument.</param>
		/// <returns>A hash identifying this subscription; pass to <see cref="WeakEventBase{TDelegate}.Unsubscribe"/> to remove it.</returns>
		public int Subscribe<TTarget>(TTarget target, Action<TTarget> method) where TTarget : class
		{
			return TryAddHandlerNoLock(new WeakReference<object>(target), t => method((TTarget)t), method.GetHashCode());
		}

		/// <summary>Invokes all live handlers, pruning any dead or destroyed targets.</summary>
		public void Invoke()
		{
			var snapshot = RentSnapshotBuffer();

			try
			{
				FillSnapshotNoLock(snapshot);

				// Iterating backwards restores FIFO order: FillSnapshotNoLock fills the snapshot in reverse,
				// so iterating the snapshot in reverse yields the original subscription order.
				// TryGetTarget is re-checked in case the target was GC'd between snapshot and iteration.
				for(int i = snapshot.Count - 1; i >= 0; i--)
				{
					if(snapshot[i].target.TryGetTarget(out var target))
					{
						snapshot[i].method(target);
					}
				}
			}
			finally
			{
				// Guaranteed return so the pool is never depleted by an exception in a handler.
				ReturnSnapshotBuffer(snapshot);
			}
		}
	}

	/// <summary>
	/// Weak event with one parameter for single-threaded use. See <see cref="WeakEvent"/> for full usage documentation.
	/// </summary>
	/// <typeparam name="T0">The type of the first event parameter.</typeparam>
	public class WeakEvent<T0> : WeakEventBase<Action<object, T0>>
	{
		/// <param name="initialCapacity">Initial capacity of the handler list. Increase if the event is expected to have many subscribers.</param>
		public WeakEvent(int initialCapacity = DEFAULT_CAPACITY) : base(initialCapacity) { }

		/// <inheritdoc cref="WeakEvent.Subscribe{TTarget}"/>
		public int Subscribe<TTarget>(TTarget target, Action<TTarget, T0> method) where TTarget : class
		{
			return TryAddHandlerNoLock(new WeakReference<object>(target), (t, a0) => method((TTarget)t, a0), method.GetHashCode());
		}

		/// <summary>Invokes all live handlers, pruning any dead or destroyed targets.</summary>
		/// <param name="a0">The first event parameter.</param>
		public void Invoke(T0 a0)
		{
			var snapshot = RentSnapshotBuffer();

			try
			{
				FillSnapshotNoLock(snapshot);

				// Iterating backwards restores FIFO order (see WeakEvent.Invoke for details).
				for(int i = snapshot.Count - 1; i >= 0; i--)
				{
					if(snapshot[i].target.TryGetTarget(out var target))
					{
						snapshot[i].method(target, a0);
					}
				}
			}
			finally
			{
				// Guaranteed return so the pool is never depleted by an exception in a handler.
				ReturnSnapshotBuffer(snapshot);
			}
		}
	}

	/// <summary>
	/// Weak event with two parameters for single-threaded use. See <see cref="WeakEvent"/> for full usage documentation.
	/// </summary>
	/// <typeparam name="T0">The type of the first event parameter.</typeparam>
	/// <typeparam name="T1">The type of the second event parameter.</typeparam>
	public class WeakEvent<T0, T1> : WeakEventBase<Action<object, T0, T1>>
	{
		/// <param name="initialCapacity">Initial capacity of the handler list. Increase if the event is expected to have many subscribers.</param>
		public WeakEvent(int initialCapacity = DEFAULT_CAPACITY) : base(initialCapacity) { }

		/// <inheritdoc cref="WeakEvent.Subscribe{TTarget}"/>
		public int Subscribe<TTarget>(TTarget target, Action<TTarget, T0, T1> method) where TTarget : class
		{
			return TryAddHandlerNoLock(new WeakReference<object>(target), (t, a0, a1) => method((TTarget)t, a0, a1), method.GetHashCode());
		}

		/// <summary>Invokes all live handlers, pruning any dead or destroyed targets.</summary>
		/// <param name="a0">The first event parameter.</param>
		/// <param name="a1">The second event parameter.</param>
		public void Invoke(T0 a0, T1 a1)
		{
			var snapshot = RentSnapshotBuffer();

			try
			{
				FillSnapshotNoLock(snapshot);

				// Iterating backwards restores FIFO order (see WeakEvent.Invoke for details).
				for(int i = snapshot.Count - 1; i >= 0; i--)
				{
					if(snapshot[i].target.TryGetTarget(out var target))
					{
						snapshot[i].method(target, a0, a1);
					}
				}
			}
			finally
			{
				// Guaranteed return so the pool is never depleted by an exception in a handler.
				ReturnSnapshotBuffer(snapshot);
			}
		}
	}
}
