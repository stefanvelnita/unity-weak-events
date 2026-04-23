// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Wander.Tests
{
	public class ConcurrentWeakEventTests : WeakEventBaseTests
	{
		protected override IWeakEvent CreateEvent() => new ConcurrentWeakEventWrapper();
		protected override IWeakEventT0 CreateEventT0() => new ConcurrentWeakEventT0Wrapper();
		protected override IWeakEventT0T1 CreateEventT0T1() => new ConcurrentWeakEventT0T1Wrapper();

		// -------------------------------------------------------------------------
		// Thread safety
		// -------------------------------------------------------------------------

		[Test]
		public void Invoke_IsThreadSafe_UnderConcurrentSubscribeAndInvoke()
		{
			var ev = new ConcurrentWeakEvent();
			var target = new object();
			var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

			ev.Subscribe(target, _ => { });

			var tasks = new Task[10];

			for(int i = 0; i < 5; i++)
			{
				tasks[i] = Task.Run(() =>
				{
					try { ev.Invoke(); }
					catch(Exception e) { exceptions.Add(e); }
				});
			}

			for(int i = 5; i < 10; i++)
			{
				var t = new object();
				tasks[i] = Task.Run(() =>
				{
					try { ev.Subscribe(t, _ => { }); }
					catch(Exception e) { exceptions.Add(e); }
				});
			}

			Task.WaitAll(tasks);

			Assert.IsEmpty(exceptions, "No exceptions should be thrown under concurrent access.");
		}
	}
}
