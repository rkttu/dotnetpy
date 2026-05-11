# Changelog

All notable changes to DotNetPy are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/).

## [0.6.0] — Free-threaded Python ready

### Added

- **Free-threaded Python (PEP 703) support.** DotNetPy has been audited
  against pythonnet PR
  [#2721](https://github.com/pythonnet/pythonnet/pull/2721)'s risk
  categories and verified on CPython **3.13t** and **3.14t** alongside the
  classic GIL build. See
  [`docs/FREETHREADED-AUDIT.md`](docs/FREETHREADED-AUDIT.md) for the audit,
  fixes, and the three-by-two verification matrix.
- **`Python.CreateIsolated()` / `DotNetPyExecutor.CreateIsolated()`.** A
  new factory that produces executors with their own private Python
  namespace (a fresh dict pre-populated with `__builtins__`). Multiple
  isolated executors coexist with the shared singleton and with each
  other; user variables defined on one are invisible to the rest. This
  is the recommended pattern for concurrent callers, plugin sandboxes,
  multi-tenant scripting hosts, and parallel ML inference under
  free-threaded Python.
- **`samples/native-aot/`.** P/Invoke consumer that drives the
  AOT-published `DotNetPy.Native.Shared` DLL through its C exports the
  way a real C/C++/Rust consumer would. Doubles as a portable smoke
  test for the AOT path and the free-threaded build.
- **`samples/ml-embeddings/`.** End-to-end semantic search with
  HuggingFace `sentence-transformers`: encodes a corpus, scores a
  query, returns top-K hits. Demonstrates real ML inference, .NET ↔
  Python array marshalling, and the per-worker isolated-executor
  pattern.

### Fixed

- **Internal scratch-name races under free-threaded Python.** All
  injected helper variables (`_json_result`, `_is_valid`,
  `_var_exists_check`, `_existing_vars`, `_var_delete_existed`,
  `_deleted_vars`, `_captured_dict`, `_to_delete`) are now minted
  per-call via `Interlocked.Increment` so two concurrent callers
  cannot collide on the same slot in `__main__` globals.
- **`Evaluate` no longer leaks a shared `result` global.** Each
  `Evaluate` call uses a per-call unique sink that is cleaned up in
  `finally`. Side-effect: callers who relied on `executor.Evaluate("x + 1")`
  leaving a `result` variable in `__main__` for later
  `CaptureVariable("result")` must switch to the explicit pattern
  (`Execute("result = x + 1")` + `CaptureVariable("result")`).
- **`SafeHandle.ReleaseHandle` now acquires the GIL before
  `Py_DecRef`.** Previously the .NET finalizer thread could trigger a
  Python `__del__` without an attached thread state — a latent bug on
  classic GIL builds that becomes unsafe more visibly under
  free-threaded builds.

### Changed

- **Public messaging repositioned.** `README.md`, `docs/PERFORMANCE.md`,
  and `docs/COMPARISON.md` lead with the verified free-threaded support
  and isolated-executor pattern. The comparison matrix now includes
  "Free-threaded Python (PEP 703)" and "Isolated Namespaces" rows plus
  a Quick Decision Guide flowchart. The performance doc is structured
  around the two concurrency models (shared singleton vs isolated
  executor) instead of the previous GIL-only framing.

### Documentation

- New: [`docs/FREETHREADED-AUDIT.md`](docs/FREETHREADED-AUDIT.md) —
  full engineering audit against pythonnet PR #2721's five risk
  categories, the four fixes shipped in this release, and the
  verification matrix (GIL 3.13 / FT 3.13t / FT 3.14t × in-process
  unit tests + AOT consumer).

### Internal

- Refactored `DotNetPyExecutor` to route every `PyRun_String` call
  through a shared `GetExecutionNamespacePtr()` helper so the shared
  and isolated modes share the same execution path. `IsValidPythonIdentifier`
  and the cleanup helpers moved from `PyRun_SimpleString` (which is
  hard-wired to `__main__`) to namespace-aware `PyRun_String` invocations.
- New delegate `PyDict_SetItemString` added to executor function-pointer
  table for `__builtins__` injection on isolated namespaces.

### Verification

3-way matrix (commit time):

| Python build | Unit tests | Native AOT consumer |
|--------------|-----------|---------------------|
| CPython 3.13 (GIL, auto-discovered) | 209 / 1 / **0** | 8 / 8 ✅ |
| CPython 3.13.13t (free-threaded) | 205 / 5 / **0** | 8 / 8 ✅ |
| CPython 3.14.4t (free-threaded) | 205 / 5 / **0** | 8 / 8 ✅ |

The 4 extra skips under free-threaded runs are dispose/reinitialise
lifecycle tests that conflict with the pre-primed singleton; the 1
always-skipped test is the deliberately ignored
`KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT`,
which exists to document the shared-singleton race that `CreateIsolated`
solves.

---

## [0.5.2] — Build pipeline polish

- Make unit tests non-blocking on release workflow.
- Bump shared package version to 0.5.2.

## [0.5.1] — Initialization deadlock fix

- Fix sync-over-async deadlock in `PythonProject.Initialize`.
- Bump shared package version to 0.5.1.

## Earlier releases

For pre-0.5.1 history see the [GitHub release
notes](https://github.com/rkttu/dotnetpy/releases) and the git log.
