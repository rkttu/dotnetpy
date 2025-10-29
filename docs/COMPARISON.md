# Comparison with Other Python Interop Libraries

DotNetPy is designed to be a **lightweight and AOT-compatible** Python interop library for .NET. This document compares DotNetPy with other popular solutions to help you choose the right tool for your needs.

## Quick Comparison

| Feature | DotNetPy | pythonnet | CSnakes |
|---------|----------|-----------|---------|
| **Complexity** | ⭐ Low | ⭐⭐⭐ High | ⭐⭐ Medium |
| **AOT Support** | ✅ Explicit | ❓ Unclear | ✅ Samples Available |
| **Type Safety** | Runtime | Runtime (dynamic) | Compile-time |
| **Bidirectional Interop** | ❌ Limited | ✅ Full Support | ✅ Supported |
| **Maturity** | ⚠️ Experimental | ✅ Stable (10+ years) | 🆕 Modern |
| **Setup Required** | Minimal | GIL Management | Source Generator |
| **Best For** | Scripting & AOT | Complex Integration | ML/AI Libraries |

## Detailed Comparison

### DotNetPy (This Project)

**Philosophy**: The lightest way to run Python from .NET

**Key Strengths**:
- ✅ **Zero Boilerplate**: No GIL management or Source Generator setup required
- ✅ **AOT-Friendly**: Explicitly designed for Native AOT scenarios
- ✅ **Minimal Learning Curve**: Start executing Python in just a few lines
- ✅ **Transparent Development**: Experimental status clearly communicated

**Example**:
```csharp
var executor = Python.GetInstance();
var result = executor.Evaluate("1+1");
var data = executor.ExecuteAndCapture("result = sum([1,2,3])");
```

**Ideal Use Cases**:
- Simple Python script execution
- Quick prototyping
- AOT compilation environments
- Using Python as a "calculator" or scripting engine
- Embedding Python in lightweight applications

**Limitations**:
- ⚠️ Experimental stage - not production-ready yet
- Limited bidirectional interop compared to alternatives
- Focused on executing Python code rather than deep integration

---

### Python.NET (pythonnet)

**Repository**: [pythonnet/pythonnet](https://github.com/pythonnet/pythonnet)

**Philosophy**: Complete .NET ↔ Python bidirectional integration

**Key Strengths**:
- ✅ **Battle-Tested**: 10+ years of development, backed by .NET Foundation
- ✅ **Full Interop**: Seamlessly exchange objects between .NET and Python
- ✅ **Rich Ecosystem**: Extensive documentation and community support
- ✅ **Production-Ready**: Used in many enterprise applications

**Example**:
```csharp
using (Py.GIL()) {
    dynamic np = Py.Import("numpy");
    Console.WriteLine(np.cos(np.pi * 2));
    
    dynamic a = np.array(new List<float> { 1, 2, 3 });
    Console.WriteLine(a.dtype);
}
```

**Ideal Use Cases**:
- Complex Python library integration
- Bidirectional object exchange
- Production applications requiring stability
- Projects needing mature ecosystem support

**Considerations**:
- Requires GIL (Global Interpreter Lock) management
- More complex API surface
- Steeper learning curve
- AOT compatibility unclear

---

### CSnakes

**Repository**: [tonybaloney/CSnakes](https://github.com/tonybaloney/CSnakes)

**Philosophy**: Type-safe Python embedding through .NET Source Generators

**Key Strengths**:
- ✅ **Type Safety**: Compile-time type checking through Source Generators
- ✅ **Modern Approach**: Leverages latest .NET features
- ✅ **Python 3.13 Support**: Including free-threading mode
- ✅ **NumPy Integration**: Direct interop with NumPy arrays and .NET spans
- ✅ **Hot Reload**: Python code hot-reloading support

**Example**:
```python
# Python file with type hints
def hello_world(name: str, age: int) -> str:
    return f"Hello {name}, you must be {age} years old!"
```

```csharp
// Auto-generated C# method
public static string HelloWorld(string name, long age) { ... }
```

**Ideal Use Cases**:
- ML/AI library integration (transformers, PyTorch, etc.)
- Type-safe Python function calls
- Projects leveraging Python's data science ecosystem
- ASP.NET Core applications with Python backend

**Considerations**:
- Requires Source Generator setup and configuration
- Build-time code generation adds complexity
- Relatively new project (less mature than pythonnet)

---

## Decision Guide

### Choose **DotNetPy** if you need:
- 🎯 Simplest possible Python execution
- 🎯 AOT compilation support
- 🎯 Minimal dependencies and setup
- 🎯 Quick prototyping or scripting scenarios
- ⚠️ **Note**: Consider pythonnet for production use until DotNetPy matures

### Choose **pythonnet** if you need:
- 🎯 Production-ready stability
- 🎯 Complex bidirectional object exchange
- 🎯 Mature ecosystem and community support
- 🎯 Deep integration between .NET and Python codebases

### Choose **CSnakes** if you need:
- 🎯 Compile-time type safety
- 🎯 Modern .NET development experience
- 🎯 ML/AI library integration
- 🎯 Source Generator-based workflow

---

## Code Comparison: Same Task, Different Approaches

**Task**: Call a Python function to calculate statistics on an array

### DotNetPy
```csharp
var numbers = new[] { 10, 20, 30, 40, 50 };
var result = executor.ExecuteAndCapture(@"
    import statistics
    result = {
        'sum': sum(numbers),
        'average': statistics.mean(numbers)
    }
", new Dictionary<string, object?> { { "numbers", numbers } });

Console.WriteLine(result.GetDouble("average"));
```

### pythonnet
```csharp
using (Py.GIL()) {
    dynamic statistics = Py.Import("statistics");
    dynamic pyNumbers = new PyList(numbers.Select(x => new PyInt(x)).ToArray());
    
    double average = statistics.mean(pyNumbers);
    Console.WriteLine(average);
}
```

### CSnakes
```python
# stats.py
from typing import List
def calculate_average(numbers: List[int]) -> float:
    import statistics
    return statistics.mean(numbers)
```

```csharp
// Auto-generated binding
double average = Stats.CalculateAverage(new long[] { 10, 20, 30, 40, 50 });
Console.WriteLine(average);
```

---

## Performance Considerations

All three libraries use the Python C-API for in-process execution:

- **DotNetPy**: Minimal overhead, thin wrapper around C-API
- **pythonnet**: Additional overhead for GIL management and object marshaling
- **CSnakes**: Code generation eliminates runtime reflection, potentially fastest

For most applications, the performance difference is negligible compared to Python execution time itself.

---

## Ecosystem Maturity

```
pythonnet    [████████████████████] 10+ years, .NET Foundation
CSnakes      [████░░░░░░░░░░░░░░░░] ~2 years, actively developed
DotNetPy     [██░░░░░░░░░░░░░░░░░░] New, experimental
```

---

## Conclusion

**DotNetPy positions itself as the simplest entry point** for .NET developers who need to execute Python code without complexity. It's designed for scenarios where you need Python's capabilities without the overhead of full interop frameworks.

> **Honest Recommendation**: For production applications, we currently recommend **pythonnet** until DotNetPy reaches maturity. DotNetPy is ideal for experimentation, prototyping, and AOT scenarios where simplicity is paramount.

We believe in **healthy coexistence** with other projects in the .NET Python interop ecosystem. Each tool serves different needs, and we encourage you to choose the one that best fits your requirements.

---

## Additional Resources

- **pythonnet**: [GitHub](https://github.com/pythonnet/pythonnet) | [Wiki](https://github.com/pythonnet/pythonnet/wiki)
- **CSnakes**: [GitHub](https://github.com/tonybaloney/CSnakes) | [Documentation](https://tonybaloney.github.io/CSnakes/)
- **DotNetPy**: [GitHub](https://github.com/rkttu/dotnetpy) | [README](../README.md)

---

*Last Updated: 2025-10-29*