using DotNetPy;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);

// ====================================================================
// Automatic Python Discovery
// ====================================================================
Console.WriteLine("=== Automatic Python Discovery ===");

try
{
    // Option 1: Automatic discovery (simplest - finds the best Python automatically)
    Python.Initialize();
    Console.WriteLine("✓ Python initialized with automatic discovery");

    // You can also specify requirements:
    // Python.Initialize(new PythonDiscoveryOptions 
    // { 
    //   MinimumVersion = new Version(3, 10),
    //     RequiredArchitecture = Architecture.X64
    // });
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"✗ Auto-discovery failed: {ex.Message}");
    Console.WriteLine("  Please install Python from https://www.python.org/");
    Environment.Exit(1);
}

// Display current Python info
var currentPython = Python.CurrentPythonInfo;
if (currentPython != null)
{
    Console.WriteLine("\n=== Currently Using Python ===");
    Console.WriteLine($"Version: {currentPython.Version}");
    Console.WriteLine($"Architecture: {currentPython.Architecture}");
    Console.WriteLine($"Source: {currentPython.Source}");
    Console.WriteLine($"Executable: {currentPython.ExecutablePath}");
    Console.WriteLine($"Library: {currentPython.LibraryPath}");
    if (!string.IsNullOrEmpty(currentPython.HomeDirectory))
    {
        Console.WriteLine($"Home Directory: {currentPython.HomeDirectory}");
    }
}

// Discover all available Python installations
var allPythons = PythonDiscovery.FindAll();
Console.WriteLine($"\nFound {allPythons.Count} Python installation(s):");
foreach (var python in allPythons)
{
    Console.WriteLine($"  - {python}");
}

Console.WriteLine("\n=== Basic Evaluation ===");

var executor = Python.GetInstance();

// Use Evaluate
Console.WriteLine(executor.Evaluate("1+1")?.GetInt32());
Console.WriteLine(executor.Evaluate("sum([1,2,3,4,5])")?.GetInt32());
Console.WriteLine(executor.Evaluate("len('hello')")?.GetInt32());

// Use ExecuteAndCapture
Console.WriteLine(executor.ExecuteAndCapture("result = 1+1")?.GetInt32());
Console.WriteLine(executor.ExecuteAndCapture(@"
	import math
	result = math.sqrt(16)
")?.GetDouble());
Console.WriteLine(executor.ExecuteAndCapture(@"
	data = [1, 2, 3, 4, 5]
	result = {
	    'sum': sum(data),
	    'mean': sum(data) / len(data)
	}
")?.ToDictionary());

// Preparing Data in .NET
var numbers = new[] { 10, 20, 30, 40, 50 };
Console.WriteLine($"Input: [{string.Join(", ", numbers)}]");

// Calculating Statistics with Python
using var result = executor.ExecuteAndCapture(@"
    import statistics
    
    result = {
        'sum': sum(numbers),
        'average': statistics.mean(numbers),
        'max': max(numbers),
        'min': min(numbers)
    }
", new Dictionary<string, object?> { { "numbers", numbers }, });

// Output
if (result != null)
{
    Console.WriteLine();
    Console.WriteLine("Result:");
    Console.WriteLine($"- Sum: {result.GetDouble("sum")}");
    Console.WriteLine($"- Avg: {result.GetDouble("average")}");
    Console.WriteLine($"- Max: {result.GetInt32("max")}");
    Console.WriteLine($"- Min: {result.GetInt32("min")}");
}

// New Feature: CaptureVariable
Console.WriteLine("\n=== CaptureVariable Examples ===");

// Execute code and capture variables later
executor.Execute(@"
    import math
    pi = math.pi
    e = math.e
    golden_ratio = (1 + math.sqrt(5)) / 2
");

var pi = executor.CaptureVariable("pi");
var e = executor.CaptureVariable("e");
var golden = executor.CaptureVariable("golden_ratio");

Console.WriteLine($"Pi: {pi?.GetDouble()}");
Console.WriteLine($"E: {e?.GetDouble()}");
Console.WriteLine($"Golden Ratio: {golden?.GetDouble()}");

// Capture multiple variables at once
using var constants = executor.CaptureVariables("pi", "e", "golden_ratio");
Console.WriteLine($"\nMultiple capture - Pi: {constants["pi"]?.GetDouble()}");

// Capture non-existent variable (returns null)
var notExist = executor.CaptureVariable("non_existent_var");
Console.WriteLine($"Non-existent variable: {(notExist == null ? "null" : "found")}");

// Delete individual variable
Console.WriteLine("\n=== DeleteVariable Examples ===");
executor.Execute("temp_var = 'temporary value'");
Console.WriteLine($"temp_var exists: {executor.CaptureVariable("temp_var") != null}");

bool deleted = executor.DeleteVariable("temp_var");
Console.WriteLine($"Deleted temp_var: {deleted}");
Console.WriteLine($"temp_var exists after delete: {executor.CaptureVariable("temp_var") != null}");

// Delete multiple variables
executor.Execute(@"
    x = 10
    y = 20
    z = 30
");
var deletedVars = executor.DeleteVariables("x", "y", "non_existent");
Console.WriteLine($"Deleted variables: {string.Join(", ", deletedVars)}");
Console.WriteLine($"z still exists: {executor.CaptureVariable("z") != null}");

// Check variable existence
Console.WriteLine("\n=== VariableExists Examples ===");
executor.Execute(@"
    apple = 'fruit'
    banana = 'fruit'
    carrot = 'vegetable'
");

Console.WriteLine($"apple exists: {executor.VariableExists("apple")}");
Console.WriteLine($"orange exists: {executor.VariableExists("orange")}");

var existing = executor.GetExistingVariables("apple", "banana", "orange", "carrot", "potato");
Console.WriteLine($"Existing variables: {string.Join(", ", existing)}");


// Example for ToDictionary
Console.WriteLine("\n=== ToDictionary Example ===");
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

if (jsonDoc != null)
{
    var dictionary = jsonDoc.ToDictionary();
    if (dictionary != null)
    {
        Console.WriteLine("Converted Dictionary:");
        // To print the dictionary in a readable format, let's iterate through it.
        foreach (var kvp in dictionary)
        {
            if (kvp.Value is List<object?> list)
            {
                Console.WriteLine($"- {kvp.Key}: [{string.Join(", ", list)}]");
            }
            else if (kvp.Value is Dictionary<string, object?> dict)
            {
                Console.WriteLine($"- {kvp.Key}:");
                foreach (var innerKvp in dict)
                {
                    Console.WriteLine($"  - {innerKvp.Key}: {innerKvp.Value}");
                }
            }
            else
            {
                Console.WriteLine($"- {kvp.Key}: {kvp.Value}");
            }
        }

        // Accessing nested dictionary
        if (dictionary.TryGetValue("address", out var addressObj) && addressObj is Dictionary<string, object?> addressDict)
        {
            Console.WriteLine("\nNested Address accessed directly:");
            Console.WriteLine($"- Street: {addressDict["street"]}");
            Console.WriteLine($"- City: {addressDict["city"]}");
        }
    }
}
