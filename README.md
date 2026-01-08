# DotNetPy

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

> **⚠️ EXPERIMENTAL**: This library is currently in an experimental stage and requires extensive testing before being used in production environments. APIs may change without notice, and there may be undiscovered issues.
> **🤖 AI-Assisted Development**: Significant portions of this codebase were written with the assistance of AI code assistants. While the code has been reviewed and tested, users should be aware of this development approach.
> **💡 Recommendation**: If you need a stable, battle-tested solution for .NET and Python interoperability, we recommend using [Python.NET (pythonnet)](https://github.com/pythonnet/pythonnet) instead. See our [comparison guide](docs/COMPARISON.md) for detailed differences.

DotNetPy (pronounced `dot-net-pie`) is a .NET library that allows you to seamlessly execute Python code directly from your C# applications. It provides a simple and intuitive API to run Python scripts and evaluate expressions with minimal boilerplate.

## Project Philosophy

DotNetPy is built around three core principles:

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

## Why DotNetPy?

DotNetPy is designed to be the **lightest way to run Python from .NET**:

- ✅ **Zero Boilerplate**: No GIL management or Source Generator setup required
- ✅ **AOT-Friendly**: The only .NET-Python interop library supporting Native AOT
- ✅ **File-based App Ready**: Works with `dotnet run script.cs` (.NET 10+)
- ✅ **uv Integration**: Declarative Python environment management built-in
- ✅ **Minimal Learning Curve**: Start executing Python in just a few lines
- ✅ **Transparent Development**: Experimental status clearly communicated

**Not sure which Python interop library to choose?** Check out our [detailed comparison](docs/COMPARISON.md) with pythonnet, CSnakes, DotWrap, and IronPython.

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
- **No Boilerplate**: The library handles the complexities of the Python C API, providing a clean interface.

## Getting Started

### Prerequisites

- .NET 10.0 or later.
- A Python installation (e.g., Python 3.13). You will need the path to the Python shared library (`pythonXX.dll` on Windows, `libpythonX.X.so` on Linux).
- (Optional) uv for declarative environment management (see [Samples](#samples) section).

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

## Usage Examples

Here are some examples demonstrating how to use DotNetPy, based on the sample code in `Program.cs`.

### 1. Evaluating Simple Expressions

The `Evaluate` method is perfect for running a single line of Python code and getting the result back immediately.

```csharp
// Returns a DotNetPyValue wrapping the integer 2
var sumResult = executor.Evaluate("1+1");
Console.WriteLine(sumResult?.GetInt32()); // Output: 2

// You can use built-in Python functions
var listSumResult = executor.Evaluate("sum([1,2,3,4,5])");
Console.WriteLine(listSumResult?.GetInt32()); // Output: 15

// And get results of different types
var lenResult = executor.Evaluate("len('hello')");
Console.WriteLine(lenResult?.GetInt32()); // Output: 5
```

### 2. Executing Scripts and Capturing Results

The `ExecuteAndCapture` method allows you to run a block of code and captures the value of a variable named `result`.

```csharp
// The value of 'result' is captured automatically
var simpleCalc = executor.ExecuteAndCapture("result = 1+1");
Console.WriteLine(simpleCalc?.GetInt32()); // Output: 2

// Multi-line scripts are supported
var sqrtResult = executor.ExecuteAndCapture(@"
    import math
    result = math.sqrt(16)
");
Console.WriteLine(sqrtResult?.GetDouble()); // Output: 4

// The result can be a complex type, like a dictionary
var dictResult = executor.ExecuteAndCapture(@"
    data = [1, 2, 3, 4, 5]
    result = {
        'sum': sum(data),
        'mean': sum(data) / len(data)
    }
");
// Convert the Python dict to a .NET Dictionary
var stats = dictResult?.ToDictionary();
Console.WriteLine(stats?["sum"]);   // Output: 15
Console.WriteLine(stats?["mean"]); // Output: 3
```

### 3. Passing .NET Data to Python

You can pass data from your C# code into the Python script. Here, a .NET array is passed to Python to calculate statistics.

```csharp
// 1. Prepare data in .NET
var numbers = new[] { 10, 20, 30, 40, 50 };

// 2. Pass it to the Python script as a global variable
using var result = executor.ExecuteAndCapture(@"
    import statistics
    
    # 'numbers' is available here because we passed it in
    result = {
        'sum': sum(numbers),
        'average': statistics.mean(numbers),
        'max': max(numbers),
        'min': min(numbers)
    }
", new Dictionary<string, object?> { { "numbers", numbers } });

// 3. Use the results in .NET
if (result != null)
{
    Console.WriteLine($"- Sum: {result.GetDouble("sum")}");       // Output: 150
    Console.WriteLine($"- Avg: {result.GetDouble("average")}"); // Output: 30
    Console.WriteLine($"- Max: {result.GetInt32("max")}");       // Output: 50
    Console.WriteLine($"- Min: {result.GetInt32("min")}");       // Output: 10
}
```

### 4. Managing Python Variables

You can execute code and then inspect, capture, or delete variables from the Python global scope.

```csharp
// Execute a script to define some variables
executor.Execute(@"
    import math
    pi = math.pi
    e = math.e
    golden_ratio = (1 + math.sqrt(5)) / 2
");

// Capture a single variable
var pi = executor.CaptureVariable("pi");
Console.WriteLine($"Pi: {pi?.GetDouble()}"); // Output: Pi: 3.14159...

// Capture multiple variables at once
using var constants = executor.CaptureVariables("pi", "e", "golden_ratio");
Console.WriteLine($"Multiple capture - Pi: {constants["pi"]?.GetDouble()}");

// Delete a variable
executor.Execute("temp_var = 'temporary value'");
bool deleted = executor.DeleteVariable("temp_var");
Console.WriteLine($"Deleted temp_var: {deleted}"); // Output: True
Console.WriteLine($"temp_var exists: {executor.VariableExists("temp_var")}"); // Output: False
```

### 5. Converting Python Dictionaries to .NET

The `ToDictionary()` method recursively converts a Python dictionary (and nested objects) into a `Dictionary<string, object?>`.

```csharp
using var jsonDoc = executor.ExecuteAndCapture(@"
result = {
    'name': 'John Doe',
    'age': 30,
    'isStudent': False,
    'courses': ['Math', 'Science'],
    'address': {
        'street': '123 Main St',
        'city': 'Anytown'
    }
}
");

var dictionary = jsonDoc?.ToDictionary();

if (dictionary != null)
{
    // Access top-level values
    Console.WriteLine(dictionary["name"]); // Output: John Doe
    
    // Access list
    var courses = (List<object?>)dictionary["courses"];
    Console.WriteLine(courses[0]); // Output: Math

    // Access nested dictionary
    var address = (Dictionary<string, object?>)dictionary["address"];
    Console.WriteLine(address["street"]); // Output: 123 Main St
}
```

## Comparison with Other Libraries

Wondering how DotNetPy compares to pythonnet or CSnakes? Check out our [detailed comparison guide](docs/COMPARISON.md) to understand the differences and choose the right tool for your needs.

## Performance and Concurrency Characteristics

### Thread Safety

DotNetPy is **thread-safe** through Python's Global Interpreter Lock (GIL). Multiple threads can safely call executor methods concurrently without additional synchronization. However, there are important performance considerations:

- Python execution is inherently **serialized** - only one thread executes Python code at a time due to the GIL
- Multiple concurrent threads will compete for the GIL, which can lead to performance degradation under high contention

### Performance Considerations

**DotNetPy is not designed for high-concurrency scenarios** involving many threads simultaneously executing Python code. The library is best suited for:

✅ **Recommended Use Cases:**

- Sequential Python script execution
- I/O-bound operations where threads naturally yield
- Low to moderate concurrency (2-5 concurrent operations)
- Scripting and automation tasks
- Data processing workflows with reasonable parallelism

❌ **Not Recommended:**

- High-frequency Python calls from 10+ concurrent threads
- CPU-intensive parallel processing relying on Python
- Real-time systems requiring predictable low-latency responses
- Scenarios where Python becomes a bottleneck in a high-throughput pipeline

### Design Philosophy

DotNetPy provides a **safe and convenient** bridge between .NET and Python, respecting Python's inherent characteristics rather than attempting to work around them. The library exposes Python's native behavior transparently:

- **GIL Contention**: Under extreme concurrency (e.g., 20+ threads), you may experience significant performance degradation or timeouts. This is a fundamental Python limitation, not a library bug.
- **No Magic Solutions**: We do not add complex synchronization layers that would hide Python's true performance characteristics or add unpredictable overhead.

### Alternative Approaches

For CPU-intensive parallel workloads, consider:

- **Pure .NET solutions** for performance-critical parallel processing
- **Python multiprocessing** (separate processes) for true parallelism in Python
- **Task-based patterns** that minimize concurrent Python calls

## Samples

### Declarative Python Environment with uv

DotNetPy supports declaratively managing Python environments using [uv](https://github.com/astral-sh/uv). This allows you to define your Python project configuration in C# and have DotNetPy handle environment setup automatically.

#### Sample Prerequisites

- [uv](https://docs.astral.sh/uv/getting-started/installation/) installed on your system
- .NET 10.0 or later

**Install uv:**

**Windows (PowerShell):**

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

**macOS/Linux:**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

#### Usage Example

```csharp
using DotNetPy;
using DotNetPy.Uv;

// Define your Python project declaratively
using var project = PythonProject.CreateBuilder()
    .WithProjectName("my-data-analysis")
    .WithVersion("1.0.0")
    .WithDescription("A sample data analysis project")
    .WithPythonVersion(">=3.10")
    .AddDependency("numpy", ">=1.24.0")
    .AddDependency("pandas", ">=2.0.0")
    .AddDependency("scikit-learn", ">=1.3.0")
    .Build();

// Initialize - this will:
// 1. Generate pyproject.toml
// 2. Download Python if not available (via uv)
// 3. Create a virtual environment
// 4. Install all dependencies
await project.InitializeAsync();

Console.WriteLine($"Environment ready at: {project.WorkingDirectory}");
Console.WriteLine($"Python: {project.PythonExecutable}");

// Option 1: Run Python scripts via uv
var result = await project.RunScriptAsync(@"
import numpy as np
import pandas as pd

data = np.array([1, 2, 3, 4, 5])
print(f'Mean: {np.mean(data)}')
");

Console.WriteLine(result.Output);

// Option 2: Use embedded executor for high-performance interop
var executor = project.GetExecutor();

executor.Execute(@"
import numpy as np
numbers = np.array([10, 20, 30])
result = {'mean': float(np.mean(numbers)), 'sum': int(np.sum(numbers))}
");

using var stats = executor.CaptureVariable("result");
var dict = stats?.ToDictionary();
Console.WriteLine($"Mean: {dict?["mean"]}, Sum: {dict?["sum"]}");
```

#### Generated pyproject.toml

The builder generates a standard `pyproject.toml` file:

```toml
[project]
name = "my-data-analysis"
version = "1.0.0"
description = "A sample data analysis project"
requires-python = ">=3.10"
dependencies = [
    "numpy>=1.24.0",
    "pandas>=2.0.0",
    "scikit-learn>=1.3.0",
]

[tool.uv]
managed = true
```

#### PythonProjectBuilder Features

**Declarative Dependency Management:**

```csharp
// Simple dependency
.AddDependency("numpy")

// With version constraint
.AddDependency("pandas", ">=2.0.0")

// With extras
.AddDependency("uvicorn", ">=0.20.0", "standard", "websockets")

// Parse PEP 508 strings
.AddDependencies("numpy>=1.24.0", "scipy>=1.10.0", "matplotlib>=3.7.0")
```

**Development Dependencies:**

```csharp
.AddDevDependency("pytest", ">=7.0.0")
.AddDevDependency("black")
.AddDevDependency("mypy", ">=1.0.0")
```

**Python Version Constraints:**

```csharp
// Minimum version (normalized to >=)
.WithPythonVersion("3.10")

// Explicit constraint
.WithPythonVersion(">=3.10,<4.0")
```

**Custom Working Directory:**

```csharp
// Use a specific directory (persistent)
.WithWorkingDirectory(@"C:\Projects\my-python-env")

// Or omit to use a temporary directory (cleaned up on Dispose)
```

**uv-specific Settings:**

```csharp
.WithUvSetting("python-preference", "only-managed")
.WithUvSetting("compile-bytecode", "true")
```

#### API Reference

**PythonProjectBuilder:**

| Method | Description |
| -------- | ------------- |
| `WithProjectName(name)` | Sets the project name |
| `WithVersion(version)` | Sets the project version |
| `WithDescription(description)` | Sets the project description |
| `WithPythonVersion(constraint)` | Sets Python version requirement |
| `AddDependency(...)` | Adds a runtime dependency |
| `AddDependencies(...)` | Adds multiple dependencies |
| `AddDevDependency(...)` | Adds a development dependency |
| `WithWorkingDirectory(path)` | Sets the project directory |
| `WithUvSetting(key, value)` | Adds uv-specific configuration |
| `Build()` | Creates the PythonProject |
| `GeneratePyProjectToml()` | Preview the TOML content |

**PythonProject:**

| Property/Method | Description |
| ----------------- | ------------- |
| `ProjectName` | The project name |
| `WorkingDirectory` | The project directory |
| `PythonExecutable` | Path to Python executable |
| `PythonLibrary` | Path to Python library (for embedding) |
| `IsInitialized` | Whether the project is ready |
| `InitializeAsync()` | Set up the environment |
| `RunScriptAsync(script)` | Run a Python script |
| `RunPythonAsync(args)` | Run Python with arguments |
| `GetExecutor()` | Get embedded Python executor |
| `InstallPackagesAsync(...)` | Install additional packages |
| `GetPyProjectToml()` | Get the TOML content |

**UvCli:**

| Property/Method | Description |
| ----------------- | ------------- |
| `IsAvailable` | Check if uv is installed |
| `Version` | Get uv version |
| `EnsureAvailable()` | Throw if uv not available |
| `RunAsync(args)` | Run uv command |
| `TryInstallAsync()` | Attempt to install uv |
| `InstallationInstructions` | Get install instructions |

#### Benefits for .NET Developers

1. **No Python Knowledge Required**: Define dependencies in familiar C# syntax
2. **Reproducible Environments**: pyproject.toml can be version-controlled
3. **Zero System Dependencies**: uv downloads Python automatically
4. **Isolated Environments**: Each project gets its own virtual environment
5. **CI/CD Ready**: Works consistently across different machines
6. **Type-Safe Configuration**: Compile-time validation of your Python setup

### uv Integration Sample

The `src/samples/uv-integration` directory contains a .NET 10 file-based app that tests DotNetPy with a uv-managed Python environment.

#### Setup

**1. Create a uv Python environment:**

```bash
# Create a new uv project (or use existing)
uv init
uv venv

# Install some packages for testing
uv pip install numpy pandas requests
```

**2. Run the sample:**

```bash
# Make sure you're in the uv project directory
dotnet run sample.cs
```

#### What the sample tests

1. **Python Discovery** - Verifies DotNetPy can find the uv-managed Python
2. **Basic Execution** - Simple math and evaluation
3. **Data Marshalling** - Passing .NET data to Python and back
4. **Package Detection** - Checks which packages are installed
5. **NumPy Operations** - Array and matrix operations (if installed)
6. **Pandas Operations** - DataFrame operations (if installed)
7. **Variable Management** - Create, capture, delete variables
8. **Error Handling** - Verify exception handling works

#### Expected Output

```text
=== DotNetPy + uv Integration Test ===

[1] Python Discovery
--------------------------------------------------
✓ Python initialized successfully
  Version:      3.12.0
  Architecture: X64
  Source:       Uv
  Executable:   /path/to/.venv/bin/python
  Library:      /path/to/libpython3.12.so

[2] Basic Python Execution
--------------------------------------------------
  1+2+3+4+5 = 15
  π = 3.1415926536
  e = 2.7182818285
  √2 = 1.4142135624

...
```

#### Troubleshooting

- **Python not found**: Make sure you're running from a directory with a `.venv` folder created by uv.
- **Package not installed**: Run `uv pip install <package>` to install missing packages.
- **DotNetPy package not found**: The `#:package DotNetPy@*` directive should automatically restore the package. If not, check your NuGet configuration.

## Integration Tests

The `src/DotNetPy.UnitTest/Integration` directory contains integration tests that verify DotNetPy works correctly with real Python environments managed by uv.

### Test Prerequisites

The integration tests require `uv` CLI to be installed on your system.

#### Windows

```powershell
# PowerShell (recommended)
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"

# Or using Scoop
scoop install uv

# Or using WinGet
winget install astral-sh.uv
```

#### macOS

```bash
# Using curl (recommended)
curl -LsSf https://astral.sh/uv/install.sh | sh

# Or using Homebrew
brew install uv
```

#### Linux

```bash
# Using curl (recommended)
curl -LsSf https://astral.sh/uv/install.sh | sh

# Or using pip
pip install uv
```

For more installation options, see: <https://docs.astral.sh/uv/getting-started/installation/>

### Running Integration Tests

**Run all integration tests:**

```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

**Run specific test classes:**

```bash
# Basic uv environment tests
dotnet test --filter "FullyQualifiedName~UvIntegrationTests"

# NumPy tests
dotnet test --filter "FullyQualifiedName~NumPyIntegrationTests"

# Pandas tests
dotnet test --filter "FullyQualifiedName~PandasIntegrationTests"

# DotNetPy with uv tests
dotnet test --filter "FullyQualifiedName~DotNetPyWithUvTests"
```

**Run with verbose output:**

```bash
dotnet test --filter "FullyQualifiedName~Integration" --logger "console;verbosity=detailed"
```

### Test Structure

| Test Class | Description |
| ------------ | ------------- |
| `UvIntegrationTests` | Basic uv environment setup and Python script execution |
| `NumPyIntegrationTests` | NumPy array and matrix operations |
| `PandasIntegrationTests` | Pandas DataFrame operations |
| `DotNetPyWithUvTests` | DotNetPy library functionality with uv-managed Python |

### How It Works

1. **UvEnvironmentFixture** creates an isolated virtual environment using `uv venv`
2. Tests install required packages using `uv pip install`
3. Python scripts are executed in the isolated environment
4. The environment is automatically cleaned up after tests complete

### Skipping Tests

If `uv` is not installed, tests will be marked as **Inconclusive** with installation instructions. This allows CI/CD pipelines to gracefully skip these tests on systems without `uv`.

### Test Troubleshooting

- **"uv is not installed" error**: Make sure `uv` is in your PATH. Try running `uv --version` in your terminal.
- **Python library not found in venv**: On Windows, the Python DLL may not be copied to the virtual environment. The tests will fall back to using the system Python library path.
- **Package installation fails**: Check your internet connection and try running `uv pip install <package>` manually to see detailed error messages.

## Roadmap

The following features are planned for future releases:

- ✅ **Automatic Python Discovery** _(Completed)_: Cross-platform automatic detection and discovery of installed Python distributions, eliminating the need for manual library path configuration.
- **Embeddable Python Support (Windows)**: Automatic setup and configuration of embeddable Python packages on Windows for simplified deployment scenarios.
- **Virtual Environment (venv) Support**: Enhanced support for working with Python virtual environments, including automatic activation and package management.
- **AI and Data Science Scenarios**: Specialized support and optimizations for AI and data science workflows, including better integration with popular libraries like NumPy, Pandas, and machine learning frameworks.

## License

This project is licensed under the Apache License 2.0. Please see the [LICENSE.txt](LICENSE.txt) file for details.
