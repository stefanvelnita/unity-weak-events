// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using System;

namespace Wander
{
	/// <summary>
	/// Extends <see cref="WeakEventBase{TDelegate}"/> with a mutual-exclusion lock, making
	/// <see cref="Unsubscribe"/>, <see cref="Prune"/>, and <see cref="Clear"/> thread-safe.<br/>
	/// <br/>
	/// Derived classes (<see cref="ConcurrentWeakEvent"/> and its typed variants) acquire
	/// <see cref="lockObj"/> when calling <see cref="WeakEventBase{TDelegate}.TryAddHandlerNoLock"/>
	/// and <see cref="WeakEventBase{TDelegate}.FillSnapshotNoLock"/>, then release it before
	/// dispatching to handlers (Snapshot Pattern).
	/// </summary>
	public abstract class ConcurrentWeakEventBase<TDelegate> : WeakEventBase<TDelegate> where TDelegate : Delegate
	{
		/// <summary>
		/// Guards all reads/writes to <see cref="WeakEventBase{TDelegate}.handlers"/>.<br/>
		/// The snapshot pattern releases this lock before invoking handlers, preventing cross-thread deadlock.
		/// </summary>
		protected readonly object lockObj = new();

		/// <param name="initialCapacity">Initial capacity of the handler list. Pass a higher value if the event is expected to have many subscribers.</param>
		protected ConcurrentWeakEventBase(int initialCapacity = DEFAULT_CAPACITY) : base(initialCapacity) { }

		/// <inheritdoc/>
		public override void Unsubscribe(int hash)
		{
			lock(lockObj)
			{
				base.Unsubscribe(hash);
			}
		}

		/// <inheritdoc/>
		public override void Prune()
		{
			lock(lockObj)
			{
				PruneNoLock();
			}
		}

		/// <inheritdoc/>
		public override void Clear()
		{
			lock(lockObj)
			{
				base.Clear();
			}
		}
	}
}
