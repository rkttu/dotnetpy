# AGENTS.md - AI Agent Guidelines for DotNetPy

This document provides guidelines for AI agents working with the DotNetPy codebase.

## Project Overview

**DotNetPy** is a .NET library for seamless Python interop from C#. It enables executing Python code directly from .NET applications with minimal boilerplate.

### Key Features

- Native AOT support
- .NET 10 file-based apps (`dotnet run script.cs`)
- Declarative uv integration for Python environment management
- Built-in Roslyn security analyzer

## Project Structure

```text
DotNetPy/
├── src/
│   ├── DotNetPy/                    # Main library
│   │   ├── Python.cs                # Static API entry point
│   │   ├── DotNetPyExecutor.cs      # Core executor implementation
│   │   ├── DotNetPyValue.cs         # Python value wrapper
│   │   ├── DotNetPyDictionary.cs    # Dictionary marshalling
│   │   ├── PythonDiscovery.cs       # Auto-detect Python installations
│   │   └── Uv/                      # uv integration (PythonProject, UvCli)
│   ├── DotNetPy.Analyzers/          # Roslyn security analyzers
│   ├── DotNetPy.Native.Shared/      # Native interop code
│   └── DotNetPy.UnitTest/           # Unit tests
├── samples/
│   ├── quickstart/                  # Simple getting started example
│   ├── declarative-python/          # Declarative Python environment
│   └── uv-integration/              # uv + ASP.NET Core examples
└── docs/                            # Documentation
```

## Critical Knowledge

### ⚠️ Python Initialization (IMPORTANT)

**Always call `Python.Initialize()` before `Python.GetInstance()`.**

```csharp
// ❌ WRONG - Will throw ArgumentNullException (libraryPath)
var executor = Python.GetInstance();

// ✅ CORRECT - Initialize first
Python.Initialize();
var executor = Python.GetInstance();
```

The `Python.Initialize()` method:

1. Discovers installed Python installations
2. Loads the Python shared library
3. Sets up the interpreter

Without initialization, `GetInstance()` fails because no Python library path is configured.

### Two Usage Patterns

#### Pattern 1: Simple Static API (requires Initialize)

```csharp
using DotNetPy;

Python.Initialize();  // Required!
var executor = Python.GetInstance();
using var result = executor.ExecuteAndCapture("result = 1 + 1");
```

#### Pattern 2: uv Project Builder (handles initialization automatically)

```csharp
using DotNetPy.Uv;

using var project = PythonProject.CreateBuilder()
    .WithProjectName("my-project")
    .AddDependencies("numpy", "pandas")
    .Build();

await project.InitializeAsync();  // Handles Python setup
var executor = project.GetExecutor();  // No separate Initialize needed
```

## Code Patterns

### Passing .NET Data to Python

```csharp
var data = new[] { 1.0, 2.0, 3.0 };
using var result = executor.ExecuteAndCapture(@"
    import statistics
    result = {'mean': statistics.mean(values)}
", new Dictionary<string, object?> { { "values", data } });
```

### Reading Results from Python

```csharp
// Get primitive types
var intVal = result?.GetInt32("key");
var doubleVal = result?.GetDouble("key");
var strVal = result?.GetString("key");

// Get dictionary
var dict = result?.ToDictionary();
```

### Resource Management

Always use `using` for `DotNetPyValue` and `PythonProject`:

```csharp
using var result = executor.ExecuteAndCapture("...");  // Disposes Python objects
using var project = PythonProject.CreateBuilder().Build();  // Cleans up temp directories
```

## File-based Apps (.NET 10+)

Sample files use file-based app format with directives:

```csharp
#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

using DotNetPy;
// ... code
```

Run with: `dotnet run path/to/script.cs`

## Security Considerations

**Never pass untrusted input directly to execution methods:**

```csharp
// ❌ DANGEROUS - Code injection
executor.Execute(userInput);

// ✅ SAFE - Data passed as variables
executor.Execute("result = sum(numbers)", 
    new Dictionary<string, object?> { { "numbers", userNumbers } });
```

The project includes `DotNetPy.Analyzers` which detects potential code injection at compile time.

## Testing

Run tests with:

```bash
dotnet test src/DotNetPy.UnitTest/
```

Key test files:

- `InitializationTests.cs` - Python initialization tests
- `MarshallingTests.cs` - Data marshalling tests
- `UvCliTests.cs` - uv integration tests

## Common Errors and Solutions

| Error | Cause | Solution |
| ------- | ------- | ---------- |
| `ArgumentNullException: libraryPath` | `Python.GetInstance()` called without initialization | Call `Python.Initialize()` first |
| `DotNetPyException: No suitable Python installation found` | Python not installed or not discoverable | Install Python or use uv integration |
| `uv is not installed` | uv CLI not available | Install uv: `powershell -c "irm https://astral.sh/uv/install.ps1 \| iex"` |

## Dependencies

- **Required**: Python 3.8+ installed on the system (or managed by uv)
- **Optional**: [uv](https://github.com/astral-sh/uv) for declarative environment management

## When Modifying Code

1. **Adding new samples**: Use file-based app format with proper `#:` directives
2. **Testing changes**: Run unit tests and sample files
3. **Python code in strings**: Ensure proper indentation (Python is whitespace-sensitive)
4. **Async operations**: uv operations are async (`InitializeAsync`, `RunScriptAsync`)
