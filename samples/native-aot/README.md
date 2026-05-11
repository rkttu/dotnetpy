# native-aot

P/Invoke consumer that drives the AOT-compiled `DotNetPy.Native.Shared` DLL
through its C exports the way a real C/C++/Rust consumer would. Use it as:

- A smoke test for the native AOT path of DotNetPy.
- A reference for embedding DotNetPy in non-.NET hosts via its C API.
- A regression check for free-threaded Python (PEP 703) against the AOT build.

The sample is a regular .NET 8 console project (not a `dotnet run` file-based
app) because it depends on a pre-published native DLL, which is more naturally
expressed as a two-step workflow.

## Requirements

- .NET 8 SDK
- The Visual Studio C++ build tools (Windows) or clang/lld (Linux/macOS), so
  the AOT compiler can produce the shared library
- A CPython shared library to point at — any of:
  - Classic GIL build (e.g. `python313.dll`)
  - Free-threaded build (e.g. `python313t.dll`, `python314t.dll`)

## Workflow

### 1 — publish the native shared library (one-time per build)

```powershell
# Windows: from the repo root, with VS Developer PowerShell loaded.
dotnet publish src/DotNetPy.Native.Shared/DotNetPy.Native.Shared.csproj `
    --configuration Release --runtime win-x64
```

```bash
# Linux / macOS: same command with the appropriate RID.
dotnet publish src/DotNetPy.Native.Shared/DotNetPy.Native.Shared.csproj \
    --configuration Release --runtime linux-x64
```

The output lands at
`src/DotNetPy.Native.Shared/bin/Release/net8.0/<rid>/publish/dotnetpy-native.{dll,so,dylib}`.

### 2 — run the consumer

```powershell
cd samples/native-aot

# Against a classic GIL build:
dotnet run -- "$env:LOCALAPPDATA\Programs\Python\Python313\python313.dll"

# Against a free-threaded build (after `uv python install 3.13t`):
dotnet run -- "$env:APPDATA\uv\python\cpython-3.13.13+freethreaded-windows-x86_64-none\python313t.dll"
```

By default the consumer walks up from its build output looking for the
publish path above. Override the DLL location with either a second positional
argument or the `DOTNETPY_NATIVE_DLL` environment variable.

## Expected output

```
AOT DLL : .../dotnetpy-native.dll
Python  : .../python313t.dll

[ok] dotnetpy_initialize
[ok] dotnetpy_evaluate '1+1' => 2
[ok] execute 'x = 7' + capture_variable 'x' => 7
[ok] execute_and_capture dict => json
[ok] delete_variable existing => 1, missing => 0
[ok] clear_globals removes user vars
[ok] parallel execute_and_capture (caller-unique vars)
[ok] parallel evaluate (no shared user state)

PASS — all native C-API smoke checks succeeded.
```

The exit code is 0 on PASS and 1 on FAIL, so the sample is usable in CI.

## What the checks cover

| # | Check | What it exercises |
|---|-------|-------------------|
| 1 | `dotnetpy_initialize` | Loading the Python shared library through the AOT DLL |
| 2 | `dotnetpy_evaluate` | Round-tripping a simple expression result as a JSON string |
| 3 | `execute` + `capture_variable` | Persisted user variables in the executor's `__main__` namespace |
| 4 | `execute_and_capture` (dict) | JSON serialization of a structured Python result |
| 5 | `delete_variable` | Variable lifecycle round-trip |
| 6 | `clear_globals` | Bulk user-variable cleanup |
| 7 | parallel `execute_and_capture` | 16 callers × 8 iterations, caller-unique user names — verifies DotNetPy's internal scratch-name isolation through the AOT path |
| 8 | parallel `evaluate` | 32 callers × 16 iterations, no shared user state — verifies `Evaluate`'s per-call result isolation |

Checks 7 and 8 are the ones that matter under free-threaded Python: under the
classic GIL build they would pass trivially because the GIL serializes
everything; under PEP 703 builds they only pass once the per-call unique-name
isolation in DotNetPy is in place.

## Related

- [docs/FREETHREADED-AUDIT.md](../../docs/FREETHREADED-AUDIT.md) — full audit of
  DotNetPy against pythonnet PR #2721's risk categories, with the verification
  matrix this sample contributes to.
- [src/DotNetPy.Native.Shared/NativeExports.cs](../../src/DotNetPy.Native.Shared/NativeExports.cs)
  — the C-API surface this consumer exercises.
