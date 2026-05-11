# Performance and Concurrency

DotNetPy ships two concurrency models. Pick the one that matches your
workload — they share the same execution surface and can coexist in the same
process.

| Mode | Acquired via | When to pick it |
|------|-------------|-----------------|
| **Shared singleton** | [`Python.GetInstance`](../src/DotNetPy/Python.cs) | Sequential scripting, REPL-style sessions, single-threaded automation; you want variables to persist across calls and across callers. |
| **Isolated executor** | [`Python.CreateIsolated`](../src/DotNetPy/Python.cs) (or [`DotNetPyExecutor.CreateIsolated`](../src/DotNetPy/DotNetPyExecutor.cs)) | Concurrent callers, plugin sandboxes, multi-tenant workloads, free-threaded Python (3.13t / 3.14t) where you want genuine parallelism. |

## Thread Safety — what each mode actually guarantees

### Shared singleton

`Python.GetInstance()` returns a process-wide singleton whose execution
namespace is CPython's `__main__` module. Multiple threads can call its
methods concurrently and DotNetPy will route them through the underlying
Python runtime safely, but there are two distinct constraints:

- **Under classic GIL builds (CPython 3.13, 3.14, …):** Python execution
  itself serializes through the GIL. Multiple threads compete for the lock,
  so throughput is bounded by Python's single-threaded behaviour. This is a
  CPython property, not a DotNetPy property.
- **Under free-threaded builds (CPython 3.13t / 3.14t):** the GIL no longer
  serializes interpreter operations. Two threads can run Python code in
  parallel, and **they share `__main__` globals** — if both write to the
  same user variable name (e.g. each does `seed = …`), they will race on
  that slot. DotNetPy's *internal* scratch names are isolated per-call (see
  [Free-threaded Audit](FREETHREADED-AUDIT.md)), but user-defined globals are
  shared by design.

For free-threaded builds, prefer the isolated mode for any non-trivial
concurrency. For classic GIL builds the shared singleton is the natural
default.

### Isolated executor

`Python.CreateIsolated()` produces an executor with its own private Python
namespace (a fresh dict, pre-populated with `__builtins__`). Concurrent
callers running on different isolated executors cannot collide on user
variable names because each executor's `Execute` / `ExecuteAndCapture` /
`Evaluate` operates against a different dict.

Within a single isolated executor, variables still persist across calls —
the executor is just like the singleton in miniature, but private. So:

```csharp
using var iso = Python.CreateIsolated();
iso.Execute("import numpy as np");
iso.Execute("arr = np.array([1, 2, 3])");   // np and arr persist within iso
using var v = iso.ExecuteAndCapture("result = arr.sum()");  // sees them
```

Two isolated executors share the same Python runtime (the loaded interpreter
is process-wide) but nothing else:

```csharp
using var a = Python.CreateIsolated();
using var b = Python.CreateIsolated();
a.Execute("secret = 'alpha'");
b.Execute("secret = 'beta'");   // does not disturb a
```

Under free-threaded CPython, two isolated executors driven from different
threads run *truly in parallel* — this is the recommended pattern for
parallel ML inference, multi-tenant scripting hosts, or plugin systems.

## When to pick which — quick decision tree

```text
Are concurrent callers in scope?
├── No  -> Shared singleton (Python.GetInstance). Simpler, persists state.
└── Yes -> Are you on free-threaded Python (3.13t / 3.14t)?
          ├── Yes -> Isolated executor (Python.CreateIsolated) per caller/thread.
          │         Genuinely parallel.
          └── No  -> Either mode works (GIL serializes anyway). Isolated
                    mode still prevents accidental cross-call state leakage.
```

## Performance characteristics

### Per-call overhead

Both modes pay the same Python C API entry costs (GIL acquisition or
attach, dict lookups, JSON round-trip for results). The isolated mode adds
a one-time dict allocation at executor construction (~microseconds); the
namespace pointer is then cached for every subsequent call. There is no
measurable steady-state overhead versus the shared mode.

### Concurrent throughput

- *Classic GIL build, either mode:* throughput is bounded by Python's
  single-threaded execution. Concurrent threads queue on the GIL. CPU-bound
  parallelism is not achievable; I/O-bound parallelism works (Python
  releases the GIL during blocking I/O).
- *Free-threaded build, shared singleton:* concurrent execution is possible
  but callers race on `__main__` globals if they use the same names. Safe
  patterns: caller-unique variable names, or external serialization.
- *Free-threaded build, isolated executors:* concurrent calls on different
  executors run in parallel with no shared user-state contention. This is
  the recommended pattern for maximum parallel throughput.

### JSON marshalling

Every result variable is serialized in Python and deserialized in .NET via
`System.Text.Json`. This is a deliberate trade-off for Native AOT
compatibility. For workloads where this cost dominates (very large result
objects), batch results into a single capture call rather than capturing
each value separately.

## Patterns

### Sequential automation (default)

```csharp
Python.Initialize();
var executor = Python.GetInstance();
executor.Execute("import requests");
executor.Execute("data = requests.get('https://example.com').text");
using var v = executor.CaptureVariable("data");
```

### Concurrent workers on free-threaded Python

```csharp
Python.Initialize();

Parallel.For(0, Environment.ProcessorCount, threadId =>
{
    using var iso = Python.CreateIsolated();
    // Each thread loads its own copy of state, runs in parallel under 3.13t/3.14t.
    iso.Execute("import json");
    using var result = iso.ExecuteAndCapture(
        "result = json.dumps({'thread': tid})",
        new Dictionary<string, object?> { { "tid", threadId } });
    Console.WriteLine(result?.GetString());
});
```

### Plugin sandbox

```csharp
DotNetPyExecutor CreatePluginExecutor(string pluginName)
{
    var iso = Python.CreateIsolated();
    iso.Execute($"PLUGIN_NAME = '{pluginName}'");
    return iso;
}

// Each plugin sees only its own variables. Disposing the executor
// releases the namespace dict; the Python runtime stays loaded for the
// next plugin.
using var pluginA = CreatePluginExecutor("alpha");
using var pluginB = CreatePluginExecutor("beta");
```

## Design Philosophy

DotNetPy exposes Python's underlying threading model honestly rather than
hiding it behind a compatibility shim:

- **Two modes, both first-class.** The shared singleton is not a "legacy"
  path; it is the right answer for sequential and stateful workloads. The
  isolated executor is the right answer for concurrency and sandboxing. Both
  are supported, documented, and verified.
- **No magic synchronisation layers.** DotNetPy does not serialize calls
  globally to hide free-threading semantics, and it does not pretend to
  bypass the GIL on classic builds. What CPython does, DotNetPy reflects.
- **The free-threaded audit is public.** See
  [`docs/FREETHREADED-AUDIT.md`](FREETHREADED-AUDIT.md) for the full
  engineering audit against pythonnet PR #2721's five risk categories, the
  four fixes applied, and the three-by-two verification matrix
  (GIL 3.13 / FT 3.13t / FT 3.14t × in-process unit tests / AOT consumer).

## When CPython is the wrong tool

For workloads where Python itself is the bottleneck, no interop library can
help. Consider:

- **Pure .NET implementations** when the algorithm has a good .NET library.
- **Python multiprocessing** (separate processes) for CPU-bound parallelism
  on classic GIL Python without needing the free-threaded build.
- **Free-threaded Python 3.13t / 3.14t** with isolated DotNetPy executors
  for in-process parallelism today — this is what DotNetPy's audit work was
  for.
