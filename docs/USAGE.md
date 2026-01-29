# Usage Examples

Here are examples demonstrating how to use DotNetPy, based on the sample code in `Program.cs`.

## 1. Evaluating Simple Expressions

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

## 2. Executing Scripts and Capturing Results

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

## 3. Passing .NET Data to Python

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

## 4. Managing Python Variables

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

## 5. Converting Python Dictionaries to .NET

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

## 6. Converting Python Lists to .NET

The `ToList()` method converts a Python list to an `IReadOnlyList<object?>`.

```csharp
using var listResult = executor.ExecuteAndCapture(@"
result = [1, 2, 3, 'hello', {'key': 'value'}]
");

var list = listResult?.ToList();

if (list != null)
{
    Console.WriteLine(list[0]); // Output: 1
    Console.WriteLine(list[3]); // Output: hello
    
    var nested = (Dictionary<string, object?>)list[4];
    Console.WriteLine(nested["key"]); // Output: value
}
```

## 7. Checking Python Runtime Information

You can query information about the currently initialized Python runtime, including free-threading support detection.

```csharp
// Get information about the current Python installation
var pythonInfo = Python.CurrentPythonInfo;
if (pythonInfo != null)
{
    Console.WriteLine($"Version: {pythonInfo.Version}");
    Console.WriteLine($"Architecture: {pythonInfo.Architecture}");
    Console.WriteLine($"Source: {pythonInfo.Source}");
    Console.WriteLine($"Executable: {pythonInfo.ExecutablePath}");
    Console.WriteLine($"Library: {pythonInfo.LibraryPath}");
    Console.WriteLine($"Is Virtual Environment: {pythonInfo.IsVirtualEnvironment}");
    Console.WriteLine($"Site Packages: {pythonInfo.SitePackagesPath}");
    Console.WriteLine($"Is Free-Threaded: {pythonInfo.IsFreeThreaded}");
}

// Quick check for free-threading support (Python 3.13+ with --disable-gil)
if (Python.IsFreeThreaded)
{
    Console.WriteLine("This Python build supports free-threading (no GIL)!");
}
```
