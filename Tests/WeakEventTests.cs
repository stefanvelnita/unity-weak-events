// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Stefan Velnita | https://github.com/stefanvelnita
// Part of the WeakEvent system for Unity.

using NUnit.Framework;

namespace Wander.Tests
{
	public class WeakEventTests : WeakEventBaseTests
	{
		protected override IWeakEvent CreateEvent() => new WeakEventWrapper();
		protected override IWeakEventT0 CreateEventT0() => new WeakEventT0Wrapper();
		protected override IWeakEventT0T1 CreateEventT0T1() => new WeakEventT0T1Wrapper();
	}
}
