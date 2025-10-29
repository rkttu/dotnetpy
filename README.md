# DotNetPy

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

> **⚠️ EXPERIMENTAL**: This library is currently in an experimental stage and requires extensive testing before being used in production environments. APIs may change without notice, and there may be undiscovered bugs or stability issues. Use at your own risk.

> **🤖 AI-Assisted Development**: Significant portions of this codebase were written with the assistance of AI code assistants. While the code has been reviewed and tested, users should be aware of this development approach.

> **💡 Recommendation**: If you need a stable, battle-tested solution for .NET and Python interoperability, we recommend using [Python.NET (pythonnet)](https://github.com/pythonnet/pythonnet) instead. Python.NET is a mature, well-maintained library with extensive community support and production-ready stability.

DotNetPy (pronounced `dot-net-pie`) is a .NET library that allows you to seamlessly execute Python code directly from your C# applications. It provides a simple and intuitive API to run Python scripts, evaluate expressions, and exchange data between .NET and Python environments.

## Features

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

- .NET 8.0 or later.
- A Python installation (e.g., Python 3.13). You will need the path to the Python shared library (`pythonXX.dll` on Windows, `libpythonX.X.so` on Linux).

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

## License

This project is licensed under the Apache License 2.0. Please see the `LICENSE.txt` file for details.
