# WeakEvent for Unity

A weak event system for Unity that eliminates the **lapsed listener problem**: standard C# `event` fields hold strong references to subscribers, preventing garbage collection and causing `MissingReferenceException` on destroyed `MonoBehaviour` instances.

Two variants are provided — pick the one that matches your threading model:

| Class | Use when |
| --- | --- |
| `WeakEvent` | All subscribers and invocations are on the main thread |
| `ConcurrentWeakEvent` | Subscribers or invocations may come from background threads |

Both variants share the same API; `ConcurrentWeakEvent` adds locking around mutations and snapshot fills.

## Features

- **Allocation-free `Invoke`** — uses a `[ThreadStatic]` snapshot pool; no GC pressure at runtime after warm-up.
- **Unity-aware** — automatically detects and prunes destroyed `UnityEngine.Object` instances ("zombie" objects that pass `TryGetTarget` but throw on access).
- **Re-entrant** — handlers may call `Subscribe`, `Unsubscribe`, or `Invoke` on the same event without corrupting the iterator (Snapshot Pattern).
- **FIFO invocation order** — handlers fire in the order they were subscribed.
- **Three arities** — zero, one, and two event parameters.
- **Thread-safe** (`ConcurrentWeakEvent` only) — `Subscribe`, `Unsubscribe`, and `Invoke` are safe to call from any thread concurrently.

## Installation

### Unity Package Manager (recommended)

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL**
3. Enter: `https://github.com/stefanvelnita/unity-weak-events.git`

### Manual

Drop the following files anywhere under your `Assets` folder:

- `WeakEventBase.cs`
- `WeakEvent.cs`
- `ConcurrentWeakEventBase.cs` *(only needed if using `ConcurrentWeakEvent`)*
- `ConcurrentWeakEvent.cs` *(only needed if using `ConcurrentWeakEvent`)*

## Usage

### Declaration

```csharp
// Single-threaded
public readonly WeakEvent onDeath = new();
public readonly WeakEvent<int> onScoreChanged = new();
public readonly WeakEvent<Vector3, GameObject> onHit = new();

// Thread-safe
public readonly ConcurrentWeakEvent onTick = new();
public readonly ConcurrentWeakEvent<float> onDeltaTime = new();
```

### The `self` pattern — important

The lambda **must not** capture `this` from the outer scope. Use the `self` parameter provided by the callback instead. Capturing `this` creates a hidden strong reference that defeats weak retention and causes a memory leak.

```csharp
// Correct: 'self' is used, the reference stays weak.
GlobalEvents.onDeath.Subscribe(this, self => self.HandleDeath());

// Wrong: capturing 'this' creates a strong reference — the subscriber will never be GC'd.
GlobalEvents.onDeath.Subscribe(this, _ => this.HandleDeath());
```

### Subscribe and unsubscribe

```csharp
private void OnEnable()
{
    GlobalEvents.onDeath.Subscribe(this, self => self.HandleDeath());
}
```

Manual unsubscription is optional — the subscriber is pruned automatically when it is garbage-collected or destroyed. To disconnect early, store the token returned by `Subscribe`:

```csharp
private int token;

private void OnEnable()
{
    token = GlobalEvents.onScoreChanged.Subscribe(this, (self, score) => self.UpdateUI(score));
}

private void OnDisable()
{
    GlobalEvents.onScoreChanged.Unsubscribe(token);
}
```

### Fire

```csharp
onDeath.Invoke();
onScoreChanged.Invoke(newScore);
onHit.Invoke(hitPoint, instigator);
```

## Performance

| Operation | Cost |
| --- | --- |
| `Invoke` | O(N); zero allocation after pool warm-up |
| `Subscribe` | O(N) for prune + duplicate check; allocates one delegate wrapper + one `WeakReference<T>` |
| `Unsubscribe` | O(N) |

The handler list and snapshot buffers are pre-allocated to `DEFAULT_CAPACITY` (16 slots) to avoid early resizes. Pass a higher `initialCapacity` to the constructor for events with many subscribers:

```csharp
public readonly WeakEvent onTick = new(initialCapacity: 64);
```

## How it works

**Weak references** — subscribers are held via `WeakReference<T>`, so they are collected normally when no other strong reference keeps them alive.

**Snapshot Pattern** — on `Invoke`, the handler list is copied into a pooled snapshot buffer (while holding the lock for `ConcurrentWeakEvent`), then iteration happens outside that critical section. This allows handlers to freely call `Subscribe` or `Invoke` without deadlocking or corrupting the iterator.

**Double-reverse iteration** — the snapshot is filled by iterating the handler list backwards (enabling safe in-place removal of dead entries). The snapshot is then iterated backwards again, which restores the original subscription order (FIFO).

**Unity-null detection** — `WeakReference<T>.TryGetTarget` returns `true` even for destroyed Unity objects. A separate check (`target is UnityEngine.Object obj && !obj`) catches these "zombie" instances before invoking their callbacks.

**`[ThreadStatic]` pool** — each thread owns its own stack of reusable snapshot lists. Re-entrant invocations pop additional lists from the same stack, so nested `Invoke` calls never share a buffer.

## Class hierarchy

```
WeakEventBase<TDelegate>
├── WeakEvent / WeakEvent<T0> / WeakEvent<T0,T1>
└── ConcurrentWeakEventBase<TDelegate>
    └── ConcurrentWeakEvent / ConcurrentWeakEvent<T0> / ConcurrentWeakEvent<T0,T1>
```

## License

MIT © [Stefan Velnita](https://github.com/stefanvelnita)
