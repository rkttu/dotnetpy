# DotNetPy + uv Integration Sample

This directory contains a .NET 10 file-based app that tests DotNetPy with a uv-managed Python environment.

## Prerequisites

1. **.NET 10 SDK** or later
2. **uv** installed ([Installation guide](https://docs.astral.sh/uv/getting-started/installation/))

## Setup

### 1. Create a uv Python environment

```bash
# Create a new uv project (or use existing)
uv init
uv venv

# Install some packages for testing
uv pip install numpy pandas requests
```

### 2. Run the sample

```bash
# Make sure you're in the uv project directory
dotnet run sample.cs
```

## What the sample tests

1. **Python Discovery** - Verifies DotNetPy can find the uv-managed Python
2. **Basic Execution** - Simple math and evaluation
3. **Data Marshalling** - Passing .NET data to Python and back
4. **Package Detection** - Checks which packages are installed
5. **NumPy Operations** - Array and matrix operations (if installed)
6. **Pandas Operations** - DataFrame operations (if installed)
7. **Variable Management** - Create, capture, delete variables
8. **Error Handling** - Verify exception handling works

## Expected Output

```
=== DotNetPy + uv Integration Test ===

[1] Python Discovery
--------------------------------------------------
? Python initialized successfully
  Version:      3.12.0
  Architecture: X64
  Source:       Uv
  Executable:   /path/to/.venv/bin/python
  Library:      /path/to/libpython3.12.so

[2] Basic Python Execution
--------------------------------------------------
  1+2+3+4+5 = 15
  ¥ð = 3.1415926536
  e = 2.7182818285
  ¡î2 = 1.4142135624

...
```

## Troubleshooting

### Python not found
Make sure you're running from a directory with a `.venv` folder created by uv.

### Package not installed
Run `uv pip install <package>` to install missing packages.

### DotNetPy package not found
The `#:package DotNetPy@*` directive should automatically restore the package. If not, check your NuGet configuration.
