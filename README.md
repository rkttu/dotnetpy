<!-- markdownlint-disable MD033 -->
<!-- markdownlint-disable MD041 -->

# DotNetPy

<img src="https://raw.githubusercontent.com/rkttu/dotnetpy/main/dotnetpy.png" alt="DotNetPy Logo" width="120" />

**Modern, AOT-ready Python interop for .NET — verified against free-threaded CPython** ✨

[![NuGet](https://img.shields.io/nuget/v/DotNetPy.svg)](https://www.nuget.org/packages/DotNetPy)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DotNetPy.svg)](https://www.nuget.org/packages/DotNetPy)
[![CI](https://github.com/rkttu/dotnetpy/actions/workflows/ci.yml/badge.svg)](https://github.com/rkttu/dotnetpy/actions/workflows/ci.yml)
[![Release](https://github.com/rkttu/dotnetpy/actions/workflows/release.yml/badge.svg)](https://github.com/rkttu/dotnetpy/actions/workflows/release.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

```csharp
// .NET ↔ Python in 4 lines
var executor = Python.GetInstance();
var temperatures = new[] { 23.5, 19.2, 31.8, 27.4, 22.1 };
using var result = executor.ExecuteAndCapture(@"
    import statistics
    result = {'mean': statistics.mean(data), 'stdev': statistics.stdev(data)}
", new Dictionary<string, object?> { { "data", temperatures } });
Console.WriteLine($"Mean: {result?.GetDouble("mean"):F1}°C ± {result?.GetDouble("stdev"):F1}");
```

✅ Native AOT | ✅ .NET 10 file-based apps | ✅ Free-threaded Python 3.13t/3.14t | ✅ Isolated executors | ✅ Declarative uv | ✅ Built-in security analyzer

DotNetPy (pronounced `dot-net-pie`) is a .NET library that executes Python code from C# with minimal boilerplate. It is designed for modern .NET scenarios — Native AOT, file-based apps, and the new free-threaded CPython builds — where deeper interop libraries can't go yet.

> **Free-threaded Python (PEP 703).** DotNetPy has been audited against pythonnet PR #2721's risk categories and verified to run correctly on CPython **3.13t** and **3.14t** alongside the classic GIL build. The audit, fixes, and verification matrix live in [`docs/FREETHREADED-AUDIT.md`](https://github.com/rkttu/dotnetpy/blob/main/docs/FREETHREADED-AUDIT.md). For concurrent callers that need hard isolation, `Python.CreateIsolated()` gives each thread its own Python namespace.

## 📚 Documentation

| Document | Description |
| ---------- | ------------- |
| [Usage Examples](https://github.com/rkttu/dotnetpy/blob/main/docs/USAGE.md) | Detailed code examples and patterns |
| [Security Guide](https://github.com/rkttu/dotnetpy/blob/main/docs/SECURITY.md) | Security considerations and safe usage |
| [uv Integration](https://github.com/rkttu/dotnetpy/blob/main/docs/UV-INTEGRATION.md) | Declarative Python environment management |
| [Performance](https://github.com/rkttu/dotnetpy/blob/main/docs/PERFORMANCE.md) | Thread safety, concurrency, and isolated executors |
| [Free-threaded Audit](https://github.com/rkttu/dotnetpy/blob/main/docs/FREETHREADED-AUDIT.md) | PEP 703 audit, fixes, and 3-way verification matrix |
| [Comparison](https://github.com/rkttu/dotnetpy/blob/main/docs/COMPARISON.md) | Decision tree across DotNetPy / pythonnet / CSnakes / IronPython |
| [Testing](https://github.com/rkttu/dotnetpy/blob/main/docs/TESTING.md) | Running integration tests |
| [**Code Samples**](https://github.com/rkttu/dotnetpy/tree/main/samples) | Runnable sample applications |

## Project Philosophy

DotNetPy is built around four core principles:

### 1. Declarative Python Control from .NET

Write Python code as strings within your C# code, with full control over execution and data flow. No separate Python files, no Source Generators, no complex setup — just define your Python logic inline and execute it.

```csharp
// Define and execute Python declaratively from C#
using var result = executor.ExecuteAndCapture(@"
    import statistics
    result = {'mean': statistics.mean(data), 'stdev': statistics.stdev(data)}
", new Dictionary<string, object?> { { "data", myNumbers } });
```

### 2. File-based App & Native AOT Ready

Designed from the ground up for modern .NET scenarios:

- **File-based Apps (.NET 10+)**: Works perfectly with `dotnet run script.cs` — no project file required
- **Native AOT**: The only .NET-Python interop library that supports `PublishAot=true`
- **Minimal Dependencies**: No heavy runtime requirements

```bash
# Just run it — no csproj needed
dotnet run my-script.cs
```

### 3. First-class uv Integration

Declaratively manage Python environments using [uv](https://github.com/astral-sh/uv):

```csharp
// Define your Python environment in C#
using var project = PythonProject.CreateBuilder()
    .WithProjectName("my-analysis")
    .WithPythonVersion(">=3.10")
    .AddDependencies("numpy>=1.24.0", "pandas>=2.0.0")
    .Build();

await project.InitializeAsync();  // Downloads Python, creates venv, installs packages
```

### 4. Free-threaded Python ready, with hard isolation on demand

DotNetPy is audited against PEP 703 risk categories (see [`FREETHREADED-AUDIT.md`](https://github.com/rkttu/dotnetpy/blob/main/docs/FREETHREADED-AUDIT.md)) and runs identically on classic GIL builds and on `python3.13t` / `python3.14t`. For concurrent callers that need a private Python namespace — plugin systems, multi-tenant scripting, or parallel ML inference — `Python.CreateIsolated()` produces an executor with its own globals dict:

```csharp
// Each thread gets its own Python namespace; same variable names cannot collide.
Parallel.For(0, 16, callerId =>
{
    using var iso = Python.CreateIsolated();
    iso.Execute($"seed = {callerId}");
    using var result = iso.ExecuteAndCapture("result = seed * 2 + 1");
    // 'seed' and 'result' belong to this caller alone.
});
```

## ⚠️ Security Considerations

DotNetPy executes arbitrary Python code with the **same privileges as the host .NET process**. Never pass untrusted or user-provided input directly to execution methods.

```csharp
// ❌ DANGEROUS: User input executed as code
executor.Execute(userInput); // Remote Code Execution vulnerability!

// ✅ SAFE: User data passed as variables, not code
executor.Execute("result = sum(numbers)", new Dictionary<string, object?> { { "numbers", userNumbers } });
```

DotNetPy includes a built-in Roslyn analyzer that detects potential code injection at compile time.

📖 **[Full Security Guide →](https://github.com/rkttu/dotnetpy/blob/main/docs/SECURITY.md)**

## Features

- **Automatic Python Discovery**: Cross-platform automatic detection of installed Python distributions with configurable requirements (version, architecture).
- **Runtime Information**: Query and inspect the currently active Python runtime configuration.
- **Execute Python Code**: Run multi-line Python scripts.
- **Evaluate Expressions**: Directly evaluate single-line Python expressions and get the result.
- **Data Marshaling**:
  - Pass complex .NET objects (like arrays and dictionaries) to Python.
  - Convert Python objects (including dictionaries, lists, numbers, and strings) back into .NET types.
- **Variable Management**:
  - `ExecuteAndCapture`: Execute code and capture a specific variable (by convention, `result`) into a .NET object.
  - `CaptureVariable(s)`: Capture one or more global variables from the Python session after execution.
  - `DeleteVariable(s)`: Remove variables from the Python session.
  - `VariableExists`: Check if a variable exists in the Python session.
  - `GetExistingVariables`: Returns a list of variables that actually exist from a given list of variable names.
  - `ClearGlobals`: Clear all global variables from the Python session.
- **Free-threaded Python (PEP 703)**: Audited and verified against CPython 3.13t and 3.14t; runs identically alongside classic GIL builds. See [`docs/FREETHREADED-AUDIT.md`](https://github.com/rkttu/dotnetpy/blob/main/docs/FREETHREADED-AUDIT.md).
- **Isolated Executors**: `Python.CreateIsolated()` gives each caller its own Python namespace — plugin systems, multi-tenant scripting, and parallel ML inference without shared-state pollution.
- **Native C-API exports**: Build the AOT-published `dotnetpy-native.dll` and call DotNetPy from C/C++/Rust through plain `extern "C"` exports — see [`samples/native-aot`](https://github.com/rkttu/dotnetpy/tree/main/samples/native-aot).
- **No Boilerplate**: The library handles the complexities of the Python C API, providing a clean interface.

## Getting Started

### Prerequisites

- .NET 8.0 or later.
- A Python installation (e.g., Python 3.13). You will need the path to the Python shared library (`pythonXX.dll` on Windows, `libpythonX.X.so` on Linux).
- (Optional) uv for declarative environment management.

### Initialization

To start using DotNetPy, you need to initialize the Python engine with the path to your Python library.

```csharp
using DotNetPy;

// Path to your Python shared library
var pythonLibraryPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Programs", "Python", "Python313", "python313.dll");

// Initialize the Python engine
Python.Initialize(pythonLibraryPath);

// Get an executor instance
var executor = Python.GetInstance();
```

## Quick Examples

```csharp
// Evaluate expressions
var sum = executor.Evaluate("sum([1,2,3,4,5])")?.GetInt32(); // 15

// Execute scripts and capture results
using var result = executor.ExecuteAndCapture(@"
    import math
    result = {'sqrt': math.sqrt(16), 'pi': math.pi}
");
Console.WriteLine(result?.GetDouble("sqrt")); // 4

// Pass .NET data to Python
var numbers = new[] { 10, 20, 30 };
using var stats = executor.ExecuteAndCapture(@"
    result = {'sum': sum(data), 'avg': sum(data)/len(data)}
", new Dictionary<string, object?> { { "data", numbers } });
```

📖 **[Full Usage Examples →](https://github.com/rkttu/dotnetpy/blob/main/docs/USAGE.md)**

## Comparison with Other Libraries

Wondering how DotNetPy compares to pythonnet or CSnakes? Check out our [detailed comparison guide](https://github.com/rkttu/dotnetpy/blob/main/docs/COMPARISON.md) to understand the differences and choose the right tool for your needs.

## Performance and Concurrency

DotNetPy ships two concurrency models. The default shared singleton (`Python.GetInstance`) serializes via the GIL on classic CPython and behaves the same under free-threaded builds — best for sequential or I/O-bound work where you want variables to persist across calls. For genuine parallelism, `Python.CreateIsolated()` returns an executor with its own namespace, which combined with free-threaded CPython (3.13t / 3.14t) lets independent callers run side-by-side without contention.

📖 **[Performance & Concurrency Details →](https://github.com/rkttu/dotnetpy/blob/main/docs/PERFORMANCE.md)**

## uv Integration

Declaratively manage Python environments using [uv](https://github.com/astral-sh/uv):

```csharp
using DotNetPy;
using DotNetPy.Uv;

// Define your Python project declaratively
using var project = PythonProject.CreateBuilder()
    .WithProjectName("my-data-analysis")
    .WithPythonVersion(">=3.10")
    .AddDependency("numpy", ">=1.24.0")
    .AddDependency("pandas", ">=2.0.0")
    .Build();

await project.InitializeAsync();  // Downloads Python, creates venv, installs packages

var executor = project.GetExecutor();
executor.Execute("import numpy as np; print(np.mean([1,2,3]))");
```

📖 **[Full uv Integration Guide →](https://github.com/rkttu/dotnetpy/blob/main/docs/UV-INTEGRATION.md)**

## Samples

Ready-to-run sample applications are available in the [`samples/`](https://github.com/rkttu/dotnetpy/tree/main/samples) directory:

| Sample | Description |
| -------- | ------------- |
| [quickstart](https://github.com/rkttu/dotnetpy/tree/main/samples/quickstart) | Minimal example - .NET ↔ Python data flow |
| [uv-integration](https://github.com/rkttu/dotnetpy/tree/main/samples/uv-integration) | Comprehensive test with uv-managed Python |
| [declarative-python](https://github.com/rkttu/dotnetpy/tree/main/samples/declarative-python) | Declarative environment setup with PythonProjectBuilder |
| [native-aot](https://github.com/rkttu/dotnetpy/tree/main/samples/native-aot) | Drive the AOT-published `dotnetpy-native.dll` through its C exports; doubles as a free-threaded Python smoke test |

```bash
# Run the quickstart sample
cd samples/quickstart
dotnet run quickstart.cs
```

📖 **[All Samples →](https://github.com/rkttu/dotnetpy/tree/main/samples)**

## Integration Tests

📖 **[Testing Guide →](https://github.com/rkttu/dotnetpy/blob/main/docs/TESTING.md)**

## Roadmap

The following features are planned for future releases:

- ✅ **Automatic Python Discovery** _(Completed)_: Cross-platform automatic detection and discovery of installed Python distributions, eliminating the need for manual library path configuration.
- ✅ **Virtual Environment (venv) Support** _(Completed)_: Enhanced support for working with Python virtual environments, including automatic activation and package management via `LoadVirtualEnvironment()` extension method.
- ✅ **uv Integration** _(Completed)_: Declarative Python environment management using the uv package manager with `PythonProject` and `PythonProjectBuilder` classes.
- ✅ **Free-threaded Python (PEP 703)** _(Completed)_: Audited against pythonnet PR #2721's risk categories and verified on CPython 3.13t / 3.14t alongside the classic GIL build. See [`docs/FREETHREADED-AUDIT.md`](https://github.com/rkttu/dotnetpy/blob/main/docs/FREETHREADED-AUDIT.md).
- ✅ **Isolated Executors** _(Completed)_: `Python.CreateIsolated()` produces per-namespace executors so concurrent callers don't share `__main__` globals — the recommended pattern under free-threaded Python.
- **Embeddable Python Support (Windows)**: Automatic setup and configuration of embeddable Python packages on Windows for simplified deployment scenarios.
- **AI and Data Science Scenarios**: Specialized support and optimizations for AI and data science workflows, including better integration with popular libraries like NumPy, Pandas, and machine learning frameworks.

## License

This project is licensed under the Apache License 2.0. Please see the [LICENSE.txt](https://github.com/rkttu/dotnetpy/blob/main/LICENSE.txt) file for details.
