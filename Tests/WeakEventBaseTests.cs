// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using Wander;

namespace Wander.Tests
{
	public abstract class WeakEventBaseTests
	{
		// -------------------------------------------------------------------------
		// Factories (overridden per variant)
		// -------------------------------------------------------------------------

		protected abstract IWeakEvent CreateEvent();
		protected abstract IWeakEventT0 CreateEventT0();
		protected abstract IWeakEventT0T1 CreateEventT0T1();

		// -------------------------------------------------------------------------
		// GC / lifetime
		// -------------------------------------------------------------------------

		[Test]
		public void Invoke_DoesNotFire_WhenTargetIsGarbageCollected()
		{
			var ev = CreateEvent();
			var signal = new Signal();

			// Non-inlined so the reference is off the stack before GC.Collect().
			var weakListener = SubscribeWeakly(ev, signal);

			GC.Collect();
			GC.WaitForPendingFinalizers();

			if(weakListener.TryGetTarget(out _))
			{
				Assert.Ignore("GC did not collect the listener; test skipped.");
				return;
			}

			ev.Invoke();

			Assert.IsFalse(signal.Fired, "Handler should not fire after target is GC'd.");
		}

		[Test]
		public void Invoke_DoesNotFire_WhenUnityObjectIsDestroyed()
		{
			var ev = CreateEvent();
			bool fired = false;
			var go = new GameObject("TestListener");

			ev.Subscribe(go, _ => fired = true);
			UnityEngine.Object.DestroyImmediate(go);

			ev.Invoke();

			Assert.IsFalse(fired, "Handler should not fire after Unity object is destroyed.");
		}

		// -------------------------------------------------------------------------
		// Subscribe / Unsubscribe
		// -------------------------------------------------------------------------

		[Test]
		public void Invoke_DoesNotFire_AfterUnsubscribe()
		{
			var ev = CreateEvent();
			var target = new object();
			bool fired = false;
			int token = ev.Subscribe(target, _ => fired = true);

			ev.Unsubscribe(token);
			ev.Invoke();

			Assert.IsFalse(fired, "Handler should not fire after unsubscription.");
		}

		[Test]
		public void Subscribe_IgnoresDuplicate_WhenSameDelegateInstanceSubscribesTwice()
		{
			var ev = CreateEvent();
			var target = new object();
			int callCount = 0;
			Action<object> handler = _ => callCount++;

			ev.Subscribe(target, handler);
			ev.Subscribe(target, handler);
			ev.Invoke();

			Assert.AreEqual(1, callCount, "Duplicate subscription should be ignored.");
		}

		// -------------------------------------------------------------------------
		// Invocation order
		// -------------------------------------------------------------------------

		[Test]
		public void Invoke_FiresHandlers_InSubscriptionOrder()
		{
			var ev = CreateEvent();
			var target = new object();
			var order = new List<int>();

			ev.Subscribe(target, _ => order.Add(1));
			ev.Subscribe(target, _ => order.Add(2));
			ev.Subscribe(target, _ => order.Add(3));
			ev.Invoke();

			Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray(), "Handlers should fire in subscription (FIFO) order.");
		}

		// -------------------------------------------------------------------------
		// Re-entrancy
		// -------------------------------------------------------------------------

		[Test]
		public void Invoke_DoesNotThrow_WhenSubscribeCalledInsideCallback()
		{
			var ev = CreateEvent();
			var target = new object();

			ev.Subscribe(target, _ =>
			{
				var inner = new object();
				ev.Subscribe(inner, __ => { });
			});

			Assert.DoesNotThrow(() => ev.Invoke(), "Subscribe called inside a callback should not throw.");
		}

		[Test]
		public void Invoke_DoesNotThrow_WhenUnsubscribeCalledInsideCallback()
		{
			var ev = CreateEvent();
			var target = new object();
			int token = 0;

			// token is captured by reference; it holds the correct hash when the lambda runs.
			token = ev.Subscribe(target, _ => ev.Unsubscribe(token));

			Assert.DoesNotThrow(() => ev.Invoke(), "Unsubscribe called inside a callback should not throw.");
		}

		[Test]
		public void Unsubscribe_DoesNotThrow_WhenTokenIsInvalid()
		{
			var ev = CreateEvent();
			var target = new object();
			bool fired = false;
			int token = ev.Subscribe(target, _ => fired = true);

			Assert.DoesNotThrow(() => ev.Unsubscribe(token + 1), "Unsubscribe with an invalid token should not throw.");

			ev.Invoke();

			Assert.IsTrue(fired, "Valid subscription should still fire after unsubscribing an invalid token.");
		}

		[Test]
		public void Invoke_PrunesDestroyedSubscribers_AndFiresRemainingOnes()
		{
			var ev = CreateEvent();
			int fireCount = 0;

			var gameObjects = new GameObject[10];

			for(int i = 0; i < 10; i++)
			{
				gameObjects[i] = new GameObject($"Listener_{i}");
				// Capture i as a local to force a distinct closure instance per iteration,
				// which produces a distinct delegate hash and avoids the duplicate-subscription guard.
				int capturedIndex = i;
				ev.Subscribe(gameObjects[i], _ => fireCount += capturedIndex >= 0 ? 1 : 0);
			}

			for(int i = 0; i < 5; i++)
			{
				UnityEngine.Object.DestroyImmediate(gameObjects[i]);
			}

			ev.Invoke();

			Assert.AreEqual(5, fireCount, "Only the 5 surviving subscribers should fire.");
		}

		// -------------------------------------------------------------------------
		// Parameterized variants
		// -------------------------------------------------------------------------

		[Test]
		public void InvokeT0_PassesParameter_ToHandler()
		{
			var ev = CreateEventT0();
			var target = new object();
			int received = -1;

			ev.Subscribe(target, (_, v) => received = v);
			ev.Invoke(42);

			Assert.AreEqual(42, received, "Handler should receive the correct parameter value.");
		}

		[Test]
		public void InvokeT0_DoesNotFire_AfterUnsubscribe()
		{
			var ev = CreateEventT0();
			var target = new object();
			bool fired = false;
			int token = ev.Subscribe(target, (_, v) => fired = true);

			ev.Unsubscribe(token);
			ev.Invoke(1);

			Assert.IsFalse(fired, "Handler should not fire after unsubscription.");
		}

		[Test]
		public void InvokeT0T1_PassesBothParameters_ToHandler()
		{
			var ev = CreateEventT0T1();
			var target = new object();
			int receivedInt = -1;
			string receivedStr = null;

			ev.Subscribe(target, (_, a, b) => { receivedInt = a; receivedStr = b; });
			ev.Invoke(7, "hello");

			Assert.AreEqual(7, receivedInt, "Handler should receive the correct first parameter.");
			Assert.AreEqual("hello", receivedStr, "Handler should receive the correct second parameter.");
		}

		[Test]
		public void InvokeT0T1_DoesNotFire_AfterUnsubscribe()
		{
			var ev = CreateEventT0T1();
			var target = new object();
			bool fired = false;
			int token = ev.Subscribe(target, (_, a, b) => fired = true);

			ev.Unsubscribe(token);
			ev.Invoke(1, "x");

			Assert.IsFalse(fired, "Handler should not fire after unsubscription.");
		}

		// -------------------------------------------------------------------------
		// Helpers
		// -------------------------------------------------------------------------

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference<object> SubscribeWeakly(IWeakEvent ev, Signal signal)
		{
			var listener = new object();
			ev.Subscribe(listener, _ => signal.Fired = true);
			return new WeakReference<object>(listener);
		}

		// -------------------------------------------------------------------------
		// Inner classes
		// -------------------------------------------------------------------------

		protected class Signal
		{
			public bool Fired;
		}

		protected interface IWeakEvent
		{
			int Subscribe(object target, Action<object> method);
			void Unsubscribe(int hash);
			void Invoke();
		}

		protected interface IWeakEventT0
		{
			int Subscribe(object target, Action<object, int> method);
			void Unsubscribe(int hash);
			void Invoke(int a0);
		}

		protected interface IWeakEventT0T1
		{
			int Subscribe(object target, Action<object, int, string> method);
			void Unsubscribe(int hash);
			void Invoke(int a0, string a1);
		}

		protected sealed class WeakEventWrapper : IWeakEvent
		{
			private readonly WeakEvent ev = new();
			public int Subscribe(object target, Action<object> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke() => ev.Invoke();
		}

		protected sealed class WeakEventT0Wrapper : IWeakEventT0
		{
			private readonly WeakEvent<int> ev = new();
			public int Subscribe(object target, Action<object, int> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke(int a0) => ev.Invoke(a0);
		}

		protected sealed class WeakEventT0T1Wrapper : IWeakEventT0T1
		{
			private readonly WeakEvent<int, string> ev = new();
			public int Subscribe(object target, Action<object, int, string> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke(int a0, string a1) => ev.Invoke(a0, a1);
		}

		protected sealed class ConcurrentWeakEventWrapper : IWeakEvent
		{
			private readonly ConcurrentWeakEvent ev = new();
			public int Subscribe(object target, Action<object> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke() => ev.Invoke();
		}

		protected sealed class ConcurrentWeakEventT0Wrapper : IWeakEventT0
		{
			private readonly ConcurrentWeakEvent<int> ev = new();
			public int Subscribe(object target, Action<object, int> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke(int a0) => ev.Invoke(a0);
		}

		protected sealed class ConcurrentWeakEventT0T1Wrapper : IWeakEventT0T1
		{
			private readonly ConcurrentWeakEvent<int, string> ev = new();
			public int Subscribe(object target, Action<object, int, string> method) => ev.Subscribe(target, method);
			public void Unsubscribe(int hash) => ev.Unsubscribe(hash);
			public void Invoke(int a0, string a1) => ev.Invoke(a0, a1);
		}
	}
}
