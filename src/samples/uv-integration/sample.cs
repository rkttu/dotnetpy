#!/usr/bin/env dotnet run
#:project ..\..\DotNetPy\DotNetPy.csproj

// =============================================================================
// DotNetPy + uv Integration Test
// =============================================================================
// 
// Prerequisites:
//   1. .NET 10 SDK
//   2. uv installed (https://docs.astral.sh/uv/)
//   3. Run this from a directory with a uv-managed Python environment
//
// Usage:
//   cd <uv-project-directory>
//   dotnet run sample.cs
//
// =============================================================================

using DotNetPy;
using System.Diagnostics;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);

var totalStopwatch = Stopwatch.StartNew();

Console.WriteLine("=== DotNetPy + uv Integration Test ===\n");

// -----------------------------------------------------------------------------
// 1. Automatic Python Discovery (should find uv-managed Python)
// -----------------------------------------------------------------------------
Console.WriteLine("[1] Python Discovery");
Console.WriteLine(new string('-', 50));

try
{
    Python.Initialize();
    Console.WriteLine("? Python initialized successfully");
    
    var pythonInfo = Python.CurrentPythonInfo;
    if (pythonInfo != null)
    {
        Console.WriteLine($"  Version:      {pythonInfo.Version}");
        Console.WriteLine($"  Architecture: {pythonInfo.Architecture}");
        Console.WriteLine($"  Source:       {pythonInfo.Source}");
        Console.WriteLine($"  Executable:   {pythonInfo.ExecutablePath}");
        Console.WriteLine($"  Library:      {pythonInfo.LibraryPath}");
    }
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"? Failed: {ex.Message}");
    return 1;
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 2. Basic Python Execution
// -----------------------------------------------------------------------------
Console.WriteLine("[2] Basic Python Execution");
Console.WriteLine(new string('-', 50));

var executor = Python.GetInstance();

// Simple evaluation
var sum = executor.Evaluate("1 + 2 + 3 + 4 + 5")?.GetInt32();
Console.WriteLine($"  1+2+3+4+5 = {sum}");

// Math operations
using var mathResult = executor.ExecuteAndCapture(@"
import math
result = {
    'pi': math.pi,
    'e': math.e,
    'sqrt2': math.sqrt(2)
}
");
Console.WriteLine($"  ¥ð = {mathResult?.GetDouble("pi"):F10}");
Console.WriteLine($"  e = {mathResult?.GetDouble("e"):F10}");
Console.WriteLine($"  ¡î2 = {mathResult?.GetDouble("sqrt2"):F10}");

Console.WriteLine();

// -----------------------------------------------------------------------------
// 3. Data Marshalling (.NET ¡æ Python ¡æ .NET)
// -----------------------------------------------------------------------------
Console.WriteLine("[3] Data Marshalling");
Console.WriteLine(new string('-', 50));

var numbers = new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
Console.WriteLine($"  Input: [{string.Join(", ", numbers)}]");

using var stats = executor.ExecuteAndCapture(@"
import statistics

result = {
    'count': len(numbers),
    'sum': sum(numbers),
    'mean': statistics.mean(numbers),
    'median': statistics.median(numbers),
    'stdev': statistics.stdev(numbers),
    'min': min(numbers),
    'max': max(numbers)
}
", new Dictionary<string, object?> { { "numbers", numbers } });

if (stats != null)
{
    Console.WriteLine($"  Count:  {stats.GetInt32("count")}");
    Console.WriteLine($"  Sum:    {stats.GetInt32("sum")}");
    Console.WriteLine($"  Mean:   {stats.GetDouble("mean")}");
    Console.WriteLine($"  Median: {stats.GetDouble("median")}");
    Console.WriteLine($"  Stdev:  {stats.GetDouble("stdev"):F4}");
    Console.WriteLine($"  Min:    {stats.GetInt32("min")}");
    Console.WriteLine($"  Max:    {stats.GetInt32("max")}");
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 4. Check for uv-installed packages (NumPy, Pandas, etc.)
// -----------------------------------------------------------------------------
Console.WriteLine("[4] Package Availability Check");
Console.WriteLine(new string('-', 50));

var packagesToCheck = new[] { "numpy", "pandas", "requests", "scipy", "matplotlib" };

foreach (var pkg in packagesToCheck)
{
    try
    {
        executor.Execute($"import {pkg}");
        using var version = executor.ExecuteAndCapture($"result = {pkg}.__version__");
        Console.WriteLine($"  ? {pkg,-12} v{version?.GetString()}");
    }
    catch
    {
        Console.WriteLine($"  ? {pkg,-12} (not installed)");
    }
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 5. NumPy Test (if available)
// -----------------------------------------------------------------------------
Console.WriteLine("[5] NumPy Operations (if available)");
Console.WriteLine(new string('-', 50));

try
{
    using var npResult = executor.ExecuteAndCapture(@"
import numpy as np

arr = np.array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
matrix = np.array([[1, 2, 3], [4, 5, 6], [7, 8, 9]])

result = {
    'array_sum': int(np.sum(arr)),
    'array_mean': float(np.mean(arr)),
    'array_std': float(np.std(arr)),
    'matrix_det': float(np.linalg.det(matrix)),
    'matrix_trace': int(np.trace(matrix))
}
");

    if (npResult != null)
    {
        Console.WriteLine($"  Array sum:    {npResult.GetInt32("array_sum")}");
        Console.WriteLine($"  Array mean:   {npResult.GetDouble("array_mean")}");
        Console.WriteLine($"  Array std:    {npResult.GetDouble("array_std"):F4}");
        Console.WriteLine($"  Matrix det:   {npResult.GetDouble("matrix_det"):F4}");
        Console.WriteLine($"  Matrix trace: {npResult.GetInt32("matrix_trace")}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  Skipped: {ex.Message}");
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 6. Pandas Test (if available)
// -----------------------------------------------------------------------------
Console.WriteLine("[6] Pandas Operations (if available)");
Console.WriteLine(new string('-', 50));

try
{
    using var pdResult = executor.ExecuteAndCapture(@"
import pandas as pd

df = pd.DataFrame({
    'name': ['Alice', 'Bob', 'Charlie', 'Diana', 'Eve'],
    'age': [25, 30, 35, 28, 32],
    'score': [85.5, 92.0, 78.5, 95.0, 88.5]
})

result = {
    'rows': len(df),
    'columns': list(df.columns),
    'avg_age': float(df['age'].mean()),
    'avg_score': float(df['score'].mean()),
    'top_scorer': df.loc[df['score'].idxmax(), 'name']
}
");

    if (pdResult != null)
    {
        Console.WriteLine($"  Rows:        {pdResult.GetInt32("rows")}");
        Console.WriteLine($"  Avg age:     {pdResult.GetDouble("avg_age")}");
        Console.WriteLine($"  Avg score:   {pdResult.GetDouble("avg_score")}");
        Console.WriteLine($"  Top scorer:  {pdResult.GetString("top_scorer")}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  Skipped: {ex.Message}");
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 7. Variable Management
// -----------------------------------------------------------------------------
Console.WriteLine("[7] Variable Management");
Console.WriteLine(new string('-', 50));

executor.Execute(@"
test_string = 'Hello from Python!'
test_number = 42
test_list = [1, 2, 3, 4, 5]
test_dict = {'a': 1, 'b': 2, 'c': 3}
");

Console.WriteLine($"  test_string exists: {executor.VariableExists("test_string")}");
Console.WriteLine($"  test_number exists: {executor.VariableExists("test_number")}");
Console.WriteLine($"  unknown_var exists: {executor.VariableExists("unknown_var")}");

using var captured = executor.CaptureVariable("test_string");
Console.WriteLine($"  Captured test_string: {captured?.GetString()}");

var deleted = executor.DeleteVariable("test_string");
Console.WriteLine($"  Deleted test_string: {deleted}");
Console.WriteLine($"  test_string exists after delete: {executor.VariableExists("test_string")}");

// Cleanup
executor.DeleteVariables("test_number", "test_list", "test_dict");

Console.WriteLine();

// -----------------------------------------------------------------------------
// 8. Error Handling
// -----------------------------------------------------------------------------
Console.WriteLine("[8] Error Handling");
Console.WriteLine(new string('-', 50));

try
{
    executor.Execute("x = 1 / 0");
    Console.WriteLine("  ? Should have thrown");
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"  ? Caught expected error: ZeroDivisionError");
}

try
{
    executor.Execute("undefined_variable");
    Console.WriteLine("  ? Should have thrown");
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"  ? Caught expected error: NameError");
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// Summary
// -----------------------------------------------------------------------------
totalStopwatch.Stop();

Console.WriteLine("=== Test Complete ===");
Console.WriteLine($"All basic operations working correctly!");
Console.WriteLine($"Total execution time: {totalStopwatch.Elapsed.TotalSeconds:F3} seconds");

return 0;
