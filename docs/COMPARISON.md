# C# to Python Interop Library Comparison

This document compares various approaches to executing Python code from C#, plus one library (DotWrap) that works in the opposite direction.

## Comparison at a Glance

| Feature | **DotNetPy** | **DotWrap** | **CSnakes** | **pythonnet** | **IronPython** |
| --------- | ------------- | ------------- | ------------- | --------------- | ---------------- |
| **Direction** | C# → Python | Python → C# | C# → Python | C# ↔ Python | C# ↔ Python |
| **Python Version** | 3.8+ (CPython) | N/A (generates Py pkg) | 3.9-3.13 | 3.6+ | 3.4 (Custom impl) |
| **.NET Version** | .NET 10+ | .NET 8+ | .NET 8-9 | .NET 6+ | .NET 6+ |
| **Native AOT** | ✅ Supported | ✅ Generates AOT libs | ❌ Not supported | ❌ Not supported | ❌ Not supported |
| **GIL Management** | Automatic (internal) | N/A | Automatic (internal) | Manual required | None (custom runtime) |
| **Free-threaded Python (PEP 703)** | ✅ Verified on 3.13t & 3.14t | N/A | ❓ Not documented | 🟡 In progress (PR #2721, 3.14t only) | N/A (own runtime, no GIL) |
| **Isolated Namespaces** | ✅ `Python.CreateIsolated()` | N/A | ❌ Not exposed | ❌ Single interpreter | ✅ `ScriptScope` per call |
| **Source Generator** | ❌ Not required | ✅ Required | ✅ Required | ❌ Not required | ❌ Not required |
| **NumPy/Pandas** | ✅ Supported | N/A | ✅ Zero-copy | ✅ Supported | ❌ Not available |
| **Bidirectional Calls** | C#→Py only | Py→C# only | C#→Py only | ✅ Bidirectional | ✅ Bidirectional |
| **Learning Difficulty** | ⭐ Very Low | ⭐⭐ Low | ⭐⭐⭐⭐ High | ⭐⭐⭐ Medium | ⭐⭐ Low |
| **uv Integration** | ✅ Built-in | ❌ None | ✅ Supported | ❌ None | ❌ None |
| **File-based App** | ✅ Supported | N/A | ❌ Not supported | ❌ Not supported | ❌ Not supported |
| **Data Conversion** | JSON-based | CFFI (native) | Direct object + Zero-copy | Direct object | Direct object |
| **Maturity** | 🆕 Experimental | 🆕 New | 🆕 New (2024) | ✅ Stable (15+ years) | ✅ Stable |

> **About the free-threaded row.** "Verified" means the library has been audited
> against [pythonnet PR #2721](https://github.com/pythonnet/pythonnet/pull/2721)'s
> risk categories and a test matrix is run on CPython 3.13t and 3.14t in
> addition to the classic GIL build. DotNetPy's matrix is documented in
> [`FREETHREADED-AUDIT.md`](FREETHREADED-AUDIT.md). pythonnet's PR #2721
> targets 3.14t only — 3.13t is blocked by a transitive `cffi` dependency in
> `clr-loader`. CSnakes and other entries reflect what is publicly documented
> at the time of writing; correct via PR if their status changes.

---

## Quick Decision Guide

```text
What are you doing?

  Calling Python from C#?
    │
    ├── Need Native AOT (single binary, no .NET runtime)?
    │     └── DotNetPy  (the only AOT-capable option in this list)
    │
    ├── Need free-threaded Python (3.13t / 3.14t) today?
    │     └── DotNetPy  (verified; pythonnet 3.14t support is in-flight,
    │                    pythonnet 3.13t is blocked by cffi/clr-loader)
    │
    ├── Need many concurrent callers without state pollution?
    │     └── DotNetPy.CreateIsolated()  (per-caller Python namespace)
    │
    ├── Prefer Source Generator + strong typing from Python type hints?
    │     └── CSnakes  (zero-copy NumPy, compile-time validation)
    │
    ├── Need to *manipulate* Python objects directly with `dynamic`?
    │     └── pythonnet  (15+ years stable, but no AOT, manual GIL)
    │
    └── Just want the cheapest path to "run this Python snippet"?
          └── DotNetPy  (4 lines, no project file, no Source Generator)

  Calling .NET from Python?
    │
    ├── Want a pip-installable Python package wrapping your .NET AOT lib?
    │     └── DotWrap  (auto-generates CPython extension via CFFI)
    │
    └── Want to import .NET assemblies from a Python script?
          └── pythonnet  (`import clr` reverse direction)

  Need Python-as-a-scripting-language inside .NET, without CPython?
    └── IronPython  (custom runtime, no GIL, but Python 3.4 level only,
                     no NumPy/Pandas, no C extensions)
```

---

## 1. DotNetPy

> **Philosophy**: The lightest way to run Python from .NET

### Key Features

- **Zero Boilerplate**: No GIL management or Source Generator setup required
- **Native AOT Support**: Generate native binaries with `PublishAot=true`
- **File-based App Support**: Perfect for .NET 10's `dotnet run script.cs` scenarios
- **uv Integration**: Declarative Python environment management built into the library
- **Automatic Python Discovery**: Auto-detection of system, PATH, and uv environments

### Code Example

```csharp
using DotNetPy;

Python.Initialize();  // Automatic Python discovery
var executor = Python.GetInstance();

// Simple evaluation
var result = executor.Evaluate("1 + 2 + 3")?.GetInt32();

// Data passing and capture
var numbers = new[] { 1, 2, 3, 4, 5 };
using var stats = executor.ExecuteAndCapture(@"
    result = {'sum': sum(numbers), 'mean': sum(numbers)/len(numbers)}
", new Dictionary<string, object?> { { "numbers", numbers } });

Console.WriteLine(stats?.GetDouble("mean"));  // 3.0
```

### Declarative Environment Management (uv)

```csharp
using var project = PythonProject.CreateBuilder()
    .WithProjectName("my-analysis")
    .WithPythonVersion(">=3.10")
    .AddDependencies("numpy>=1.24.0", "pandas>=2.0.0")
    .Build();

await project.InitializeAsync();
var executor = project.GetExecutor();
```

### Pros

- Minimal barrier to entry (under 5 lines)
- Native AOT support
- File-based App (.NET 10) support
- uv-based declarative environment management

### Cons

- Cannot directly manipulate Python objects
- No bidirectional calls
- Experimental stage

### Recommended Use Cases

- Scripting/automation
- Python calls from Native AOT apps
- File-based App scenarios
- Simple data processing

---

## 2. DotWrap

> **Philosophy**: Expose .NET AOT libraries to Python with auto-generated wrappers

**GitHub**: <https://github.com/connorivy/DotWrap>

### Key Features of DotWrap

- **Reverse Direction**: Python calls C# (opposite of DotNetPy)
- **Native AOT Based**: Compiles C# to native code, no .NET runtime required at Python side
- **Auto-generated Python Package**: Creates Python package with type hints and docstrings
- **CPython Extension Module**: Uses CFFI for low-overhead native calls (~0.3µs per call)
- **Source Generator**: Automatically generates interop code from `[DotWrapExpose]` attribute

### Code Example of DotWrap

**C# code (exposed to Python):**

```csharp
using DotWrap;

namespace CoolCalc;

[DotWrapExpose]  // Mark class for Python exposure
public class Calculator
{
    public int Add(int a, int b) => a + b;
}
```

**Build and publish:**

```bash
dotnet publish -r linux-x64  # Generates Python package
```

**Python usage:**

```python
import cool_calc

calc = cool_calc.Calculator()
print(calc.add(2, 3))  # Output: 5
```

### Comparison between DotWrap and DotNetPy

| Aspect | DotNetPy | DotWrap |
| -------- | ---------- | --------- |
| **Direction** | C# → Python | Python → C# |
| **Use Case** | Run Python code from .NET | Run .NET code from Python |
| **Runtime Required** | Python runtime in .NET app | No .NET runtime in Python |
| **Native AOT** | ✅ Supports AOT .NET apps | ✅ Generates AOT native libs |
| **Package Generation** | N/A | Auto-generates Python package |
| **Interop Mechanism** | Python C API | CFFI extension module |

### Pros of DotWrap

- No .NET runtime required for Python users
- Native speed with CPython extension modules
- Auto-generated type hints and docstrings
- Very low call overhead (~0.3µs)

### Cons of DotWrap

- Opposite direction (Python→C#, not C#→Python)
- Requires publish step to generate Python package
- Limited to exposing .NET types, not running Python code

### Recommended Use Cases of DotWrap

- Exposing high-performance C# libraries to Python
- Creating Python packages from existing .NET code
- When Python developers need to use .NET functionality

> **Note**: DotWrap and DotNetPy serve **opposite purposes**. DotWrap is for calling C# from Python, while DotNetPy is for calling Python from C#. They are complementary, not competing libraries.

---

## 3. CSnakes

> **Philosophy**: Type safety and high-performance AI/ML workloads

**GitHub**: <https://github.com/tonybaloney/csnakes>

### Key Features of CSnakes

- **Source Generator**: Automatically generates C# interfaces from Python files
- **Type Hint Based**: Creates strongly-typed C# methods from Python type hints
- **Zero-copy**: Direct memory sharing for NumPy arrays
- **Automatic GIL Management**: Internal synchronization handling
- **Microsoft Sponsored**: Led by Anthony Shaw (Python team)

### Code Example of CSnakes

**Python file (`example.py`)**:

```python
def calculate_stats(numbers: list[int]) -> dict[str, float]:
    import statistics
    return {
        "mean": statistics.mean(numbers),
        "stdev": statistics.stdev(numbers)
    }
```

**C# code**:

```csharp
// Use the class auto-generated by Source Generator
using var env = Python.CreateEnvironment();
var example = env.Example();  // Class generated from example.py

var stats = example.CalculateStats(new[] { 1, 2, 3, 4, 5 });
Console.WriteLine(stats["mean"]);  // 3.0
```

### Comparison between CSnakes and DotNetPy

| Aspect | DotNetPy | CSnakes |
| -------- | ---------- | --------- |
| Setup Complexity | Very low | High (Source Generator required) |
| Type Hints | Not required | Required |
| Performance (NumPy) | Standard | Zero-copy (high performance) |
| Native AOT | ✅ Supported | ❌ Not supported |
| File-based App | ✅ Supported | ❌ Not supported |
| uv Integration | ✅ Built-in | ✅ Supported |
| Use Cases | Scripting/automation | AI/ML production |

### Pros of CSnakes

- Type-safe Python calls
- NumPy Zero-copy (high performance)
- Compile-time validation

### Cons of CSnakes

- Source Generator setup required
- Type hints required in Python code
- Steep learning curve
- No Native AOT support

### Recommended Use Cases of CSnakes

- AI/ML production workloads
- High-performance NumPy-based computations
- Integration with large Python codebases

---

## 4. pythonnet (Python.NET)

> **Philosophy**: Complete bidirectional interoperability

**GitHub**: <https://github.com/pythonnet/pythonnet>

### Key Features of pythonnet

- **15+ Years of History**: Stable and proven
- **Bidirectional Calls**: C# ↔ Python bidirectional calls possible
- **Direct Object Manipulation**: Use Python objects directly with `dynamic` keyword
- **Manual GIL Management Required**: `using (Py.GIL())` pattern is mandatory

### Code Example of pythonnet

```csharp
using Python.Runtime;

// Manual GIL management required
using (Py.GIL())
{
    dynamic np = Py.Import("numpy");
    dynamic arr = np.array(new[] { 1, 2, 3, 4, 5 });
    Console.WriteLine(np.sum(arr));  // 15
    
    dynamic pd = Py.Import("pandas");
    dynamic df = pd.DataFrame(new Dictionary<string, object> {
        { "col1", new[] { 1, 2, 3 } },
        { "col2", new[] { 4, 5, 6 } }
    });
    Console.WriteLine(df.describe());
}
```

### Calling .NET from Python (Reverse Direction)

```python
import clr
clr.AddReference("System.Windows.Forms")
from System.Windows.Forms import Form, Application

form = Form()
form.Text = "Hello from Python!"
Application.Run(form)
```

### Comparison between pythonnet and DotNetPy

| Aspect | DotNetPy | pythonnet |
| -------- | ---------- | ----------- |
| GIL Management | Automatic | Manual (`Py.GIL()`) |
| Python Objects | Converted to values | Direct manipulation |
| Bidirectional | ❌ C#→Py only | ✅ Full bidirectional |
| Native AOT | ✅ Supported | ❌ Not supported |
| File-based App | ✅ Supported | ❌ Not supported |
| Learning Curve | Low | Medium |
| Environment Management | uv integration | Manual |

### Pros of pythonnet

- Bidirectional support (Python ↔ .NET)
- Direct Python object manipulation (`dynamic`)
- Stable and proven
- Rich documentation and community

### Cons of pythonnet

- Manual GIL management required
- No Native AOT support
- Medium entry difficulty
- `PYTHONNET_PYDLL` environment variable setup required

### Recommended Use Cases of pythonnet

- Using .NET libraries from Python
- Deep integration with complex Python codebases
- When fine-grained control over Python objects is needed

---

## 5. IronPython

> **Philosophy**: .NET native Python runtime

**GitHub**: <https://github.com/IronLanguages/ironpython3>

### Key Features of IronPython

- **Python Reimplemented in .NET**: Custom Python runtime, not CPython
- **No GIL**: Uses .NET threading model
- **Native .NET Integration**: Python code compiles to MSIL
- **Python 3.4 Level**: Latest Python features not supported

### Code Example of IronPython

```csharp
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

ScriptEngine engine = Python.CreateEngine();
ScriptScope scope = engine.CreateScope();

// Set variables
scope.SetVariable("x", 10);
scope.SetVariable("y", 20);

// Execute
engine.Execute("result = x + y", scope);
int result = scope.GetVariable<int>("result");
Console.WriteLine(result);  // 30
```

### Comparison between IronPython and DotNetPy

| Aspect | DotNetPy | IronPython |
| -------- | ---------- | ------------ |
| Python Runtime | CPython required | Built-in |
| NumPy/Pandas | ✅ Available | ❌ Not available |
| Python Version | 3.8+ | 3.4 (outdated) |
| Multithreading | GIL limited | Free |
| C Extensions | ✅ Supported | ❌ Not supported |
| Native AOT | ✅ Supported | ❌ Not supported |

### Pros of IronPython

- True multithreading without GIL
- Native .NET integration
- No external Python installation required

### Cons of IronPython

- **NumPy/Pandas not available** (C extension not supported)
- Python 3.4 level (some 3.x features incomplete)
- Limited compatibility with CPython ecosystem
- Slower execution speed

### Recommended Use Cases of IronPython

- Pure Python scripting (with .NET libraries)
- When multithreading is critical
- Simple scripts without C extension dependencies

---

## Recommendations by Use Case

| Use Case | Recommended Library | Reason |
| ---------- | --------------------- | -------- |
| **Rapid Prototyping** | **DotNetPy** | Zero boilerplate, quick start |
| **Native AOT Apps** | **DotNetPy** | Only one with AOT support for C#→Python |
| **File-based App (.NET 10)** | **DotNetPy** | `dotnet run script.cs` scenario support |
| **Free-threaded Python (PEP 703)** | **DotNetPy** | Only library verified on 3.13t & 3.14t today |
| **Concurrent / Multi-tenant Hosts** | **DotNetPy** | `CreateIsolated()` per-caller namespace |
| **AI/ML Production** | **CSnakes** | Zero-copy NumPy, type safety |
| **Python ↔ .NET Bidirectional** | **pythonnet** | Only one with full bidirectional support |
| **Pure .NET Scripting** | **IronPython** | No CPython installation required |
| **Expose C# to Python** | **DotWrap** | Auto-generate Python packages from .NET AOT |
| **uv Environment Management** | **DotNetPy** | Declarative environment management built-in |

---

## DotNetPy's Unique Value

### Key Differentiators

1. **Lowest Barrier to Entry**: Python execution possible in under 5 lines
2. **Only Native AOT Support**: The only choice for .NET AOT scenarios
3. **Free-threaded Python verified today**: The only library in this list with a public audit and verification matrix on CPython 3.13t and 3.14t (see [`FREETHREADED-AUDIT.md`](FREETHREADED-AUDIT.md))
4. **Isolated executors**: `Python.CreateIsolated()` produces per-caller Python namespaces — concurrent hosts, plugin sandboxes, and multi-tenant scripting without shared `__main__` pollution
5. **File-based App Support**: Perfect support for .NET 10's `dotnet run script.cs`
6. **uv Integration**: Declarative Python environment management built into the library
7. **Transparent Development**: Experimental status explicitly stated, AI-assisted development disclosed

### Limitations (To Be Acknowledged)

- Experimental stage, use caution in production
- Cannot directly manipulate Python objects (compared to pythonnet)
- No bidirectional calls
- Lack of community/documentation (new project)

---

## Migration Guide

### pythonnet → DotNetPy

**Before (pythonnet):**

```csharp
using Python.Runtime;

Runtime.PythonDLL = "/path/to/python3.dll";
PythonEngine.Initialize();

using (Py.GIL())
{
    dynamic np = Py.Import("numpy");
    dynamic result = np.sum(np.array(new[] { 1, 2, 3 }));
    Console.WriteLine(result);
}

PythonEngine.Shutdown();
```

**After (DotNetPy):**

```csharp
using DotNetPy;

Python.Initialize();
var executor = Python.GetInstance();

using var result = executor.ExecuteAndCapture(@"
    import numpy as np
    result = int(np.sum(np.array([1, 2, 3])))
");
Console.WriteLine(result?.GetInt32());
```

### When NOT to Choose DotNetPy

- ❌ When you need to directly manipulate Python objects → **pythonnet**
- ❌ When you need to call .NET code from Python → **pythonnet**
- ❌ When NumPy Zero-copy performance is critical → **CSnakes**
- ❌ When production stability is the top priority → **pythonnet**
- ❌ When you only need pure Python without C extensions → **IronPython**
