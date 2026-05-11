# Code Samples

This directory contains runnable sample applications demonstrating DotNetPy features.

## Prerequisites

All samples require:
- **.NET 10 SDK** (for file-based apps with `dotnet run`)
- **Python 3.10+** installed on your system

Some samples additionally require:
- **[uv](https://docs.astral.sh/uv/)** for declarative environment management

## Available Samples

| Sample | Description | Requirements |
|--------|-------------|--------------|
| [quickstart](quickstart/) | Minimal example showing basic .NET ↔ Python data flow | Python |
| [uv-integration](uv-integration/) | Comprehensive test of DotNetPy with uv-managed Python | Python + uv |
| [declarative-python](declarative-python/) | Declarative Python environment setup with PythonProjectBuilder | uv |
| [native-aot](native-aot/) | P/Invoke consumer driving the AOT-compiled `DotNetPy.Native.Shared` DLL through its C exports; doubles as a free-threaded Python smoke test | Python + VS C++ tools (Windows) |
| [ml-embeddings](ml-embeddings/) | End-to-end semantic search with HuggingFace `sentence-transformers`; demonstrates real ML inference, .NET ↔ Python array marshalling, and the isolated-executor pattern | uv (downloads ~1 GB on first run) |
| [ml-whisper](ml-whisper/) | Speech-to-text with OpenAI's Whisper via HuggingFace `transformers`; transcribes audio with chunk-level timestamps and demonstrates per-worker isolated pipelines | uv (downloads ~1 GB on first run) |
| [ml-image-gen](ml-image-gen/) | Text-to-image generation with `stabilityai/sd-turbo` via HuggingFace `diffusers`; single-step CPU inference, metadata round-trips to .NET while the PNG stays Python-side | uv (downloads ~4 GB on first run) |

---

## quickstart

**The simplest possible example** - demonstrates passing .NET data to Python and getting results back.

```bash
cd samples/quickstart
dotnet run quickstart.cs
```

**What it does:**
- Initializes Python automatically
- Passes a .NET array to Python
- Calculates statistics using Python's built-in `statistics` module
- Returns results to .NET

**Expected output:**
```
Mean: 24.8°C ± 4.6
```

**Source:** [quickstart.cs](quickstart/quickstart.cs)

---

## uv-integration

**Comprehensive integration test** - runs from within a uv-managed Python environment.

### Setup

```bash
cd samples/uv-integration

# Create uv environment (one-time setup)
uv venv
uv pip install numpy pandas requests
```

### Run

```bash
dotnet run sample.cs
```

**What it tests:**
1. Python Discovery - Finds uv-managed Python automatically
2. Basic Execution - Simple math and expressions
3. Data Marshalling - .NET ↔ Python data transfer
4. Package Detection - Checks installed packages
5. NumPy Operations - Array and matrix operations
6. Pandas Operations - DataFrame operations
7. Variable Management - Create, capture, delete variables
8. Error Handling - Exception handling

**Expected output:**
```
=== DotNetPy + uv Integration Test ===

[1] Python Discovery
--------------------------------------------------
✓ Python initialized successfully
  Version:      3.13.x
  Architecture: X64
  Source:       Uv
  ...

[2] Basic Python Execution
--------------------------------------------------
  1+2+3+4+5 = 15
  π = 3.1415926536
  ...
```

**Source:** [sample.cs](uv-integration/sample.cs)

---

## declarative-python

**Declarative environment management** - create Python environments entirely from C# code.

```bash
cd samples/declarative-python
dotnet run sample.cs
```

**What it demonstrates:**
1. Check uv availability
2. Create Python project with `PythonProjectBuilder`
3. Generate `pyproject.toml` automatically
4. Initialize environment (downloads Python, creates venv, installs packages)
5. Run Python scripts via uv
6. Use embedded executor for direct Python interop
7. Pass .NET data to Python and get results
8. Install additional packages on the fly

**Key code pattern:**

```csharp
using var project = PythonProject.CreateBuilder()
    .WithProjectName("data-analysis-demo")
    .WithVersion("1.0.0")
    .WithPythonVersion(">=3.10")
    .AddDependencies("numpy>=1.24.0", "pandas>=2.0.0")
    .Build();

await project.InitializeAsync();

var executor = project.GetExecutor();
executor.Execute("import numpy as np; print(np.mean([1,2,3]))");
```

**Expected output:**
```
=== DotNetPy Declarative Python Environment Sample ===

[1] Checking uv availability
--------------------------------------------------
✓ uv is available: 0.x.x

[2] Creating Python project
--------------------------------------------------
  Project Name: data-analysis-demo
  Version:      1.0.0
  Dependencies: 2
  ...
```

**Source:** [sample.cs](declarative-python/sample.cs)

---

## native-aot

**P/Invoke consumer for the AOT-compiled native DLL** - drives `DotNetPy.Native.Shared`
through its C exports the way a real C/C++/Rust consumer would. Used as a smoke
test for the native AOT path and as a regression check for free-threaded Python
(PEP 703 / `python3.13t` / `python3.14t`) — see
[docs/FREETHREADED-AUDIT.md](../docs/FREETHREADED-AUDIT.md).

### Setup

```bash
# Publish the native shared library first (one-time per build).
# Requires the Visual Studio C++ build tools on Windows.
dotnet publish src/DotNetPy.Native.Shared/DotNetPy.Native.Shared.csproj \
    --configuration Release --runtime win-x64
```

### Run

```bash
cd samples/native-aot
dotnet run -- <path-to-python-shared-library>
```

**Source:** [Program.cs](native-aot/Program.cs) · [README](native-aot/README.md)

---

## Running Samples from the Repository Root

You can also run samples from the repository root:

```bash
# From repository root
dotnet run samples/quickstart/quickstart.cs
dotnet run samples/declarative-python/sample.cs

# For uv-integration, you need to be in the uv environment directory
cd samples/uv-integration
dotnet run sample.cs
```

## Creating Your Own Sample

Use this template for a minimal file-based app:

```csharp
#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:package DotNetPy

using DotNetPy;

// Initialize Python (auto-discovery)
Python.Initialize();
var executor = Python.GetInstance();

// Your code here
var result = executor.Evaluate("1 + 1");
Console.WriteLine($"Result: {result?.GetInt32()}");
```

Save as `my-sample.cs` and run:

```bash
dotnet run my-sample.cs
```

## Troubleshooting

### "Python not found"
- Ensure Python 3.10+ is installed
- For uv samples, make sure you're in a directory with `.venv`

### "uv is not installed"
- Install uv: `powershell -c "irm https://astral.sh/uv/install.ps1 | iex"` (Windows)
- Or: `curl -LsSf https://astral.sh/uv/install.sh | sh` (macOS/Linux)

### "Package not found"
- For uv-integration: Run `uv pip install <package>` first
- For declarative-python: Packages are installed automatically

### ".NET 10 required"
- These samples use file-based apps which require .NET 10+
- Install from: https://dotnet.microsoft.com/download/dotnet/10.0
