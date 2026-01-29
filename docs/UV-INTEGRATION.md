# uv Integration

DotNetPy supports declaratively managing Python environments using [uv](https://github.com/astral-sh/uv). This allows you to define your Python project configuration in C# and have DotNetPy handle environment setup automatically.

## Prerequisites

- [uv](https://docs.astral.sh/uv/getting-started/installation/) installed on your system
- .NET 8.0 or later

### Install uv

**Windows (PowerShell):**

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

**macOS/Linux:**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

## Basic Usage

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

## Generated pyproject.toml

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

## PythonProjectBuilder Features

### Declarative Dependency Management

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

### Development Dependencies

```csharp
.AddDevDependency("pytest", ">=7.0.0")
.AddDevDependency("black")
.AddDevDependency("mypy", ">=1.0.0")
```

### Python Version Constraints

```csharp
// Minimum version (normalized to >=)
.WithPythonVersion("3.10")

// Explicit constraint
.WithPythonVersion(">=3.10,<4.0")
```

### Custom Working Directory

```csharp
// Use a specific directory (persistent)
.WithWorkingDirectory(@"C:\Projects\my-python-env")

// Or omit to use a temporary directory (cleaned up on Dispose)
```

### uv-specific Settings

```csharp
.WithUvSetting("python-preference", "only-managed")
.WithUvSetting("compile-bytecode", "true")
```

## API Reference

### PythonProjectBuilder

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

### PythonProject

| Property/Method | Description |
| ----------------- | ------------- |
| `ProjectName` | The project name |
| `WorkingDirectory` | The project directory |
| `VirtualEnvironmentPath` | Path to the virtual environment |
| `PythonExecutable` | Path to Python executable |
| `PythonLibrary` | Path to Python library (for embedding) |
| `IsInitialized` | Whether the project is ready |
| `Dependencies` | The runtime dependencies for this project |
| `DevDependencies` | The development dependencies for this project |
| `InitializeAsync()` | Set up the environment |
| `RunScriptAsync(script)` | Run a Python script |
| `RunPythonAsync(args)` | Run Python with arguments |
| `GetExecutor()` | Get embedded Python executor |
| `InstallPackagesAsync(...)` | Install additional packages |
| `GetPyProjectToml()` | Get the TOML content |
| `GetSitePackagesPath()` | Get the site-packages directory path |

### UvCli

| Property/Method | Description |
| ----------------- | ------------- |
| `IsAvailable` | Check if uv is installed |
| `Version` | Get uv version |
| `EnsureAvailable()` | Throw if uv not available |
| `RunAsync(args)` | Run uv command |
| `TryInstallAsync()` | Attempt to install uv |
| `InstallationInstructions` | Get install instructions |

### DotNetPyExecutor Extension Methods

| Method | Description |
| -------- | ------------- |
| `LoadVirtualEnvironment(venvPath)` | Loads a virtual environment's site-packages into sys.path |
| `LoadVirtualEnvironment(project)` | Loads a PythonProject's virtual environment into sys.path |

## Benefits for .NET Developers

1. **No Python Knowledge Required**: Define dependencies in familiar C# syntax
2. **Reproducible Environments**: pyproject.toml can be version-controlled
3. **Zero System Dependencies**: uv downloads Python automatically
4. **Isolated Environments**: Each project gets its own virtual environment
5. **CI/CD Ready**: Works consistently across different machines
6. **Type-Safe Configuration**: Compile-time validation of your Python setup

## Sample Application

The `src/samples/uv-integration` directory contains a .NET 10 file-based app that tests DotNetPy with a uv-managed Python environment.

### Setup

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

### What the sample tests

1. **Python Discovery** - Verifies DotNetPy can find the uv-managed Python
2. **Basic Execution** - Simple math and evaluation
3. **Data Marshalling** - Passing .NET data to Python and back
4. **Package Detection** - Checks which packages are installed
5. **NumPy Operations** - Array and matrix operations (if installed)
6. **Pandas Operations** - DataFrame operations (if installed)
7. **Variable Management** - Create, capture, delete variables
8. **Error Handling** - Verify exception handling works

### Expected Output

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

### Troubleshooting

- **Python not found**: Make sure you're running from a directory with a `.venv` folder created by uv.
- **Package not installed**: Run `uv pip install <package>` to install missing packages.
- **DotNetPy package not found**: The `#:package DotNetPy@*` directive should automatically restore the package. If not, check your NuGet configuration.
