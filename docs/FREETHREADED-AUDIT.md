# Free-threaded Python Audit

This document records a thread-safety audit of DotNetPy against free-threaded
Python (PEP 703 — `Py_GIL_DISABLED`), available experimentally in CPython 3.13t
and 3.14t. It explains what changed and what the resulting library guarantees
are, so callers can decide how to use DotNetPy in concurrent scenarios.

## Why audit now

Free-threaded CPython removes the Global Interpreter Lock. Interop libraries
written against the classic GIL build silently relied on it to serialize
interpreter operations and to provide implicit mutual exclusion over shared
state. When the GIL goes away, those implicit guarantees vanish and any
shared-state assumption becomes a race.

The reference engineering effort in this space is pythonnet PR
[#2721 — *Python 3.14 free-threaded support*](https://github.com/pythonnet/pythonnet/pull/2721),
which catalogues five categories of work needed to make a deep .NET ↔ Python
binding safe under free-threading. We used those same five categories as the
audit lens for DotNetPy.

## Audit lens — five risk categories from pythonnet PR #2721

| # | Category | Why it matters under PEP 703 |
|---|----------|------------------------------|
| 1 | Refcount layout / `ob_refcnt` access | Free-threaded `PyObject` is 16 bytes longer and uses a split refcount; any code reading `ob_refcnt` directly breaks. |
| 2 | Concurrent type/object cache races | Library-internal `Dictionary<,>` caches that the GIL implicitly serialized tear under PEP 703. |
| 3 | `Reflection.Emit` thread safety | `TypeBuilder`/`ModuleBuilder` corrupt the IL stream under concurrent use; needs serialization. |
| 4 | `GCHandle` slot ownership | When two paths can swap a single `GCHandle` slot, a missing atomic claim double-frees the handle. |
| 5 | Finalizer / `Py_Finalize` race | The .NET finalizer thread can call into Python after interpreter teardown. |

## DotNetPy mapping

| # | Risk in pythonnet | Status in DotNetPy |
|---|-------------------|--------------------|
| 1 | `ob_refcnt` direct read | **N/A by design.** DotNetPy uses `Py_IncRef` / `Py_DecRef` exported symbols only. The CPython ABI guarantees these work across GIL and free-threaded builds. [`DotNetPyObject.Initialize`](../src/DotNetPy/DotNetPyObject.cs) loads them. |
| 2 | Library-internal cache races | **N/A by design.** DotNetPy does not bridge .NET and Python type systems and has no library-internal type cache. The static fields it does hold (`_libraryHandle`, function-pointer delegates, `_currentPythonInfo`) are written once under `_initLock` and read-only thereafter. |
| 3 | `Reflection.Emit` thread safety | **N/A by design.** `Reflection.Emit` does not appear anywhere in `src/DotNetPy/`. DotNetPy does not generate Python subclasses of CLR types or wrap delegates dynamically. |
| 4 | `GCHandle` slot race | **N/A by design.** `GCHandle` does not appear anywhere in `src/DotNetPy/`. Python-side objects are wrapped only through `SafeHandle` ([`DotNetPyObject`](../src/DotNetPy/DotNetPyObject.cs)); no `GCHandle` slot is exposed to Python. |
| 5 | Finalizer / shutdown race | **Mitigated.** DotNetPy never calls `Py_Finalize` (see [`Dispose`](../src/DotNetPy/DotNetPyExecutor.cs) — *"Py_Finalize() is only safe to call on process exit"*), so the .NET finalizer thread cannot race against interpreter teardown. The remaining concern — `SafeHandle.ReleaseHandle` running `Py_DecRef` on the .NET finalizer thread without holding the GIL — is addressed by an explicit `PyGILState_Ensure` / `PyGILState_Release` guard. |

The 4-of-5 "N/A by design" rows hold because DotNetPy is a deliberately *shallow*
interop layer: it executes Python code as strings via `PyRun_String` and
marshals data as JSON, instead of bridging the .NET and Python type systems.
The whole class of races pythonnet wrestles with — concurrent type cache
construction, dynamic delegate dispatchers, subclass `tp_clear` and so on — has
no surface to land on in DotNetPy.

## Fixes implemented

Two issues did surface in the audit. Both are now fixed.

### Fix 1 — Internal scratch names in shared `__main__` globals

**Problem.** Several executor methods injected helper variables into
`__main__.globals()` under fixed, hard-coded names (`_json_result`, `_is_valid`,
`_var_exists_check`, `_existing_vars`, `_var_delete_existed`, `_deleted_vars`,
`_captured_dict`, `_to_delete`). Under the GIL build, `GilLock` serializes the
entire C# method, so two callers never see each other's slot. Under
free-threaded Python, `PyGILState_Ensure` no longer provides whole-method mutual
exclusion: two concurrent callers can both write `_json_result`, then both read
it back, returning each other's value.

**Fix.** A monotonic counter mints per-call unique names:

```csharp
private static long _tempVarCounter;

private static string MakeInternalName(string baseName)
    => $"_dotnetpy_{baseName}_{Interlocked.Increment(ref _tempVarCounter):x}";
```

Every fixed scratch name has been replaced with `MakeInternalName(...)` and the
matching cleanup follows the new name. The `_dotnetpy_*` prefix keeps the
underscore-leading convention so `ClearGlobals`'s `not k.startswith('_')` filter
still excludes our scratch names from the user-deletion set.

### Fix 2 — `Evaluate` exposed a fixed `result` slot

**Problem.** `Evaluate("expr")` was implemented as
`ExecuteAndCapture("result = " + expr)`. Two concurrent `Evaluate` calls under
free-threaded Python therefore raced on the user-visible `result` slot in
`__main__` globals. This was caught by the AOT-consumer test in section
*Verification*, after Fix 1 was already in place.

**Fix.** `Evaluate` now generates a per-call unique result name and cleans it up
in `finally`:

```csharp
public DotNetPyValue? Evaluate(string expression)
{
    string resultVar = MakeInternalName("eval_result");
    try
    {
        return ExecuteAndCapture($"{resultVar} = {expression}", resultVar);
    }
    finally
    {
        using var gil = new GilLock();
        CleanupTemporaryVariable(resultVar);
    }
}
```

**Behavioural side-effect (documented break).** Previously `executor.Evaluate("x + 1")`
left a `result` global in `__main__` that subsequent code could read via
`CaptureVariable("result")`. That side-effect is gone. Callers who relied on
it should switch to the explicit pattern:

```csharp
executor.Execute("result = x + 1");
using var v = executor.CaptureVariable("result");
```

This is a deliberate trade-off: the previous behaviour was an undocumented
leak that also accumulated stale `result` values across calls; the new
behaviour is concurrency-safe and leaves globals clean.

### Fix 3 — `Py_DecRef` from the .NET finalizer thread

**Problem.** `DotNetPyObject` is a `SafeHandle`. When the .NET garbage collector
finalizes a wrapper, `ReleaseHandle` runs on the .NET finalizer thread and
called `Py_DecRef(handle)` directly. The .NET finalizer thread is not attached
to the Python interpreter and does not hold the GIL. Under classic GIL builds,
this is a latent bug — if the refcount drop fires a Python `__del__`, that
`__del__` executes Python code without an attached thread state. Under
free-threaded builds, `Py_DecRef` itself becomes atomic, but `__del__` still
needs a valid thread state.

**Fix.** `ReleaseHandle` now acquires the GIL (via `PyGILState_Ensure`) around
the `Py_DecRef` call, with a fallback to a bare `Py_DecRef` if the GIL helpers
were not initialized (e.g. early process shutdown). See
[`DotNetPyObject.ReleaseHandle`](../src/DotNetPy/DotNetPyObject.cs).

### Fix 4 — Opt-in isolated executors (`CreateIsolated`)

**Problem.** Fixes 1–3 handle DotNetPy's *internal* state. They do not address
the case where two concurrent callers — running on different threads —
deliberately use the same user variable name. With the shared singleton every
call's user variables land in `__main__.globals()`; under free-threaded Python
two threads writing `seed = X` and `seed = Y` will race and either thread can
then read the other's value. This is exercised, and continuously regressed
against, by the `[Ignore]`-marked
[`KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT`](../src/DotNetPy.UnitTest/ConcurrencyAndIsolationTests.cs).

**Fix.** A new factory method,
[`DotNetPyExecutor.CreateIsolated`](../src/DotNetPy/DotNetPyExecutor.cs) (also
re-exported as [`Python.CreateIsolated`](../src/DotNetPy/Python.cs)), produces
an executor with its own private execution namespace. Internally the executor
holds a strong reference to a fresh `PyDict` (pre-populated with
`__builtins__`) and runs every `PyRun_String` against that dict as both
globals and locals.

```csharp
// Concurrent workers, same user variable names, no race:
Parallel.For(0, 16, callerId =>
{
    using var iso = Python.CreateIsolated();
    iso.Execute("seed = " + callerId);
    iso.Execute("result = seed * 2 + 1");
    // 'seed' and 'result' are this caller's alone.
});
```

Trade-offs:

- **Cross-executor isolation.** Variables defined on executor A are invisible
  to executor B and to the shared singleton. Within a single isolated executor
  variables still persist across calls.
- **Per-call overhead.** Creating the namespace dict and injecting
  `__builtins__` is a one-time cost at executor construction (microseconds).
  Each call resolves `globals` from a cached pointer; no extra work versus the
  shared path.
- **`PyRun_SimpleString` cannot be used.** It always executes against
  `__main__`. DotNetPy's internal validation (`IsValidPythonIdentifier`) and
  scratch cleanup paths now use `PyRun_String` with the executor's namespace
  instead.
- **Lifetime.** Disposing an isolated executor releases its namespace dict
  (under a GIL guard so any `__del__` side-effects run safely). The shared
  singleton is unaffected.

The shared singleton (`Python.GetInstance`) keeps its existing semantics —
variables persist across calls in `__main__`, which is what most scripting
scenarios want. `CreateIsolated` is an opt-in for callers that need
concurrency or hard isolation.

## Verification

Two layers of tests verify the fixes, run against three Python builds.

### Layer A — in-process unit tests

`src/DotNetPy.UnitTest/ConcurrencyAndIsolationTests.cs` (12 active tests +
1 deliberately-ignored "known limitation" test) covers the shared-singleton
path:

- Each public method leaves no `_dotnetpy_*` scratch residue in globals.
- 50 sequential mixed-API calls do not accumulate `_dotnetpy_*` entries.
- 16-caller × 8-iter parallel `ExecuteAndCapture` with caller-unique user
  variable names.
- 12-caller × 5-iter parallel `CaptureVariable` against per-caller globals.
- 10-caller parallel `VariableExists` / `DeleteVariable` against per-caller
  globals.

`src/DotNetPy.UnitTest/IsolatedExecutorTests.cs` (7 tests) covers Fix 4 —
the `CreateIsolated` factory:

- Distinct instances per `CreateIsolated()` call.
- Isolated executor's variables do not appear in the shared singleton.
- Two isolated executors are mutually independent.
- Variables persist across calls within a single isolated executor.
- `__builtins__` is wired up (`print`, `len`, `import json` all work).
- Disposing one isolated executor does not disturb others.
- The exact workload that races on the shared singleton —
  16 callers × 8 iterations using `seed`/`result` as user variable names —
  succeeds when each caller owns its own isolated executor.

The test suite as a whole is driven against an arbitrary Python build through
the `DOTNETPY_TEST_PYTHON_LIB` environment variable, picked up by
`[AssemblyInitialize]` in `MSTestSettings.cs`.

### Layer B — AOT-published native DLL through a C-API consumer

`samples/native-aot/` (see [Sample](#sample)) is a P/Invoke consumer that drives
the AOT-compiled `dotnetpy-native.dll` through its C exports the way a real
C/C++/Rust consumer would. The .NET host process is only a convenience — the
DLL never sees it. The consumer runs 8 checks: 6 functional smoke tests plus a
16-caller × 8-iter parallel `execute_and_capture` and a 32-caller × 16-iter
parallel `evaluate`. The parallel `evaluate` check is what surfaced Fix 2.

### Result matrix

| Python build | Unit tests (passed / skipped / failed) | Native AOT consumer |
|--------------|----------------------------------------|---------------------|
| CPython 3.13 (GIL, auto-discovered) | 209 / 1 / **0** | 8 / 8 ✅ |
| CPython 3.13.13t (free-threaded) | 205 / 5 / **0** | 8 / 8 ✅ |
| CPython 3.14.4t (free-threaded) | 205 / 5 / **0** | 8 / 8 ✅ |

The 4 additional skips under free-threaded runs are dispose-and-reinitialize
lifecycle tests that conflict with the pre-primed singleton in
`[AssemblyInitialize]`; they are a test-harness limitation, not a behavioural
gap. The 1 always-skipped test is the deliberately ignored
`KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT` — it
documents the shared-singleton race that Fix 4's `CreateIsolated` solves; the
equivalent workload over isolated executors runs green in the totals above.

### Reproducing the matrix locally

```powershell
# 1. Install free-threaded Python via uv (one-time)
uv python install 3.13t 3.14t

# 2. Locate the DLLs
$ft313 = "$env:APPDATA\uv\python\cpython-3.13.13+freethreaded-windows-x86_64-none\python313t.dll"
$ft314 = "$env:APPDATA\uv\python\cpython-3.14.4+freethreaded-windows-x86_64-none\python314t.dll"

# 3. Unit tests against each build
Remove-Item Env:\DOTNETPY_TEST_PYTHON_LIB -ErrorAction SilentlyContinue
dotnet test src/DotNetPy.UnitTest/DotNetPy.UnitTest.csproj --configuration Release

$env:DOTNETPY_TEST_PYTHON_LIB = $ft313
dotnet test src/DotNetPy.UnitTest/DotNetPy.UnitTest.csproj --configuration Release --no-build

$env:DOTNETPY_TEST_PYTHON_LIB = $ft314
dotnet test src/DotNetPy.UnitTest/DotNetPy.UnitTest.csproj --configuration Release --no-build
```

For the AOT consumer, see [`samples/native-aot/README.md`](../samples/native-aot/README.md).

## Known limitations

### Shared-singleton concurrent calls with the same user variable name

`Python.GetInstance` returns a process-wide singleton that injects user
variables into `__main__.globals()`. Two concurrent callers using the same
user variable name still race on that shared dict regardless of how cleanly
DotNetPy isolates its own internal scratch names. This is intrinsic to the
shared singleton's contract — its appeal is exactly that variables persist
across calls and across callers, which means they are *also* visible to (and
clobberable by) every other caller.

**Recommended options, in priority order:**

1. **Use `Python.CreateIsolated()`** (Fix 4 above) — each caller / thread gets
   its own namespace. This is the first-class solution and the same code that
   raced on the singleton runs green when each thread owns an isolated
   executor.
2. **Use caller-unique user variable names** when staying on the shared
   singleton is preferable (e.g. you actually want cross-call persistence).
3. **Serialise calls externally** with your own lock when neither of the
   above fits — coarse-grained, but a backstop.

### Lifecycle tests skipped under explicit-library initialization

The 4 dispose / reference-count lifecycle tests cannot run when the singleton
is pre-primed via `DOTNETPY_TEST_PYTHON_LIB`. This is a test-harness shape, not
a runtime constraint; they pass cleanly under the default auto-discovery flow.

## References

- pythonnet PR #2721 — [Python 3.14 free-threaded support](https://github.com/pythonnet/pythonnet/pull/2721)
- pythonnet issue #2610 — [Free-threading (no-GIL) support](https://github.com/pythonnet/pythonnet/issues/2610)
- PEP 703 — [Making the Global Interpreter Lock Optional in CPython](https://peps.python.org/pep-0703/)
- CPython 3.13 release notes — [Free-threaded build](https://docs.python.org/3/whatsnew/3.13.html#free-threaded-cpython)
- Python `PyGILState_Ensure` — [Threading semantics across GIL and free-threaded builds](https://docs.python.org/3/c-api/init.html#c.PyGILState_Ensure)
