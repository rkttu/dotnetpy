# Integration Tests

This directory contains integration tests that verify DotNetPy works correctly with real Python environments managed by [uv](https://docs.astral.sh/uv/).

## Prerequisites

### Install uv

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

For more installation options, see: https://docs.astral.sh/uv/getting-started/installation/

## Running Integration Tests

### Run all integration tests

```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

### Run specific test classes

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

### Run with verbose output

```bash
dotnet test --filter "FullyQualifiedName~Integration" --logger "console;verbosity=detailed"
```

## Test Structure

| Test Class | Description |
|------------|-------------|
| `UvIntegrationTests` | Basic uv environment setup and Python script execution |
| `NumPyIntegrationTests` | NumPy array and matrix operations |
| `PandasIntegrationTests` | Pandas DataFrame operations |
| `DotNetPyWithUvTests` | DotNetPy library functionality with uv-managed Python |

## How It Works

1. **UvEnvironmentFixture** creates an isolated virtual environment using `uv venv`
2. Tests install required packages using `uv pip install`
3. Python scripts are executed in the isolated environment
4. The environment is automatically cleaned up after tests complete

## Skipping Tests

If `uv` is not installed, tests will be marked as **Inconclusive** with installation instructions. This allows CI/CD pipelines to gracefully skip these tests on systems without `uv`.

## Troubleshooting

### "uv is not installed" error

Make sure `uv` is in your PATH. Try running `uv --version` in your terminal.

### Python library not found in venv

On Windows, the Python DLL may not be copied to the virtual environment. The tests will fall back to using the system Python library path.

### Package installation fails

Check your internet connection and try running `uv pip install <package>` manually to see detailed error messages.
