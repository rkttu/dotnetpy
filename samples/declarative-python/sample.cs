#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

// =============================================================================
// DotNetPy Declarative Python Environment Sample
// =============================================================================
//
// This sample demonstrates how to use PythonProjectBuilder to declaratively
// create and manage Python environments with uv.
//
// Prerequisites:
//   1. .NET 10 SDK or later
//   2. uv installed (https://docs.astral.sh/uv/getting-started/installation/)
//
// Usage:
//   dotnet run sample.cs
//
// =============================================================================

using DotNetPy.Uv;

Console.WriteLine("=== DotNetPy Declarative Python Environment Sample ===");
Console.WriteLine();

// -----------------------------------------------------------------------------
// Step 1: Check if uv is available
// -----------------------------------------------------------------------------
Console.WriteLine("[1] Checking uv availability");
Console.WriteLine(new string('-', 50));

if (!UvCli.IsAvailable)
{
    Console.WriteLine("✗ uv is not installed!");
    Console.WriteLine();
    Console.WriteLine("Please install uv first:");
    Console.WriteLine(UvCli.InstallationInstructions);
    return 1;
}

var uvVersion = UvCli.Version;
Console.WriteLine($"✓ uv is available: {uvVersion}");
Console.WriteLine();

// -----------------------------------------------------------------------------
// Step 2: Create a Python project declaratively
// -----------------------------------------------------------------------------
Console.WriteLine("[2] Creating Python project");
Console.WriteLine(new string('-', 50));

// Define your Python project using the fluent builder API
//
// Working Directory Options:
// --------------------------
// By default, a temporary directory is created automatically:
//   - Location: %TEMP%/dotnetpy-projects/{projectName}-{guid}
//   - This temporary directory is DELETED when Dispose() is called.
//
// To persist the project to a specific location, use WithWorkingDirectory():
//   .WithWorkingDirectory(@"./MyPythonProjects/my-project")
//
// When using WithWorkingDirectory():
//   - The specified directory is used (created if it doesn't exist).
//   - The directory is NOT deleted when Dispose() is called.
//   - You can reuse the environment later by pointing to the same directory.
//
using var project = PythonProject.CreateBuilder()
    .WithProjectName("data-analysis-demo")
    .WithVersion("1.0.0")
    .WithDescription("A sample data analysis project using DotNetPy")
    .WithPythonVersion(">=3.10")
    .AddDependencies("numpy>=1.24.0", "pandas>=2.0.0")
    .AddDevDependency("pytest", ">=7.0.0")
    .WithUvSetting("python-preference", "only-managed")
    // .WithWorkingDirectory(@"./MyPythonProjects/persistent-env")  // Uncomment to persist
    .Build();

Console.WriteLine($"  Project Name: {project.ProjectName}");
Console.WriteLine($"  Version:      {project.Version}");
Console.WriteLine($"  Dependencies: {project.Dependencies.Count}");
Console.WriteLine();

// Preview the generated pyproject.toml
Console.WriteLine("[3] Generated pyproject.toml");
Console.WriteLine(new string('-', 50));
var tomlContent = project.GetPyProjectToml();
Console.WriteLine(tomlContent);
Console.WriteLine();

// -----------------------------------------------------------------------------
// Step 3: Initialize the environment (creates venv, installs packages)
// -----------------------------------------------------------------------------
Console.WriteLine("[4] Initializing Python environment");
Console.WriteLine(new string('-', 50));

try
{
    var initStart = DateTime.Now;
    await project.InitializeAsync();
    var initDuration = DateTime.Now - initStart;
    
    Console.WriteLine($"✓ Environment initialized successfully ({initDuration.TotalSeconds:F1}s)");
    Console.WriteLine($"  Working Dir:  {project.WorkingDirectory}");
    Console.WriteLine($"  Python:       {project.PythonExecutable}");
    Console.WriteLine($"  Library:      {project.PythonLibrary}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Initialization failed: {ex.Message}");
    return 1;
}

// -----------------------------------------------------------------------------
// Step 4: Run Python scripts via uv
// -----------------------------------------------------------------------------
Console.WriteLine("[5] Running Python via uv");
Console.WriteLine(new string('-', 50));

var scriptResult = await project.RunScriptAsync(@"
import numpy as np
import pandas as pd
import sys

print(f'Python: {sys.version}')
print(f'NumPy:  {np.__version__}')
print(f'Pandas: {pd.__version__}')

# Create sample data
data = np.array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
print(f'')
print(f'Sample Data: {data}')
print(f'Mean:        {np.mean(data):.2f}')
print(f'Std Dev:     {np.std(data):.2f}')
print(f'Sum:         {np.sum(data)}')
");

if (scriptResult.Success)
{
    Console.WriteLine(scriptResult.Output);
}
else
{
    Console.WriteLine($"✗ Script failed: {scriptResult.Error}");
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// Step 5: Use embedded Python executor for high-performance interop
// -----------------------------------------------------------------------------
Console.WriteLine("[6] Using embedded Python executor");
Console.WriteLine(new string('-', 50));

try
{
    // GetExecutor() automatically loads the virtual environment's site-packages
    // You can pass autoLoadSitePackages: false if you want to manage sys.path manually
    var executor = project.GetExecutor();
    
    // Display the site-packages path that was loaded
    var sitePackagesPath = project.GetSitePackagesPath();
    Console.WriteLine($"  Site-packages: {sitePackagesPath}");
    
    // Execute Python code and capture results
    executor.Execute(@"
import numpy as np
import pandas as pd

# Create a DataFrame
df = pd.DataFrame({
    'Name': ['Alice', 'Bob', 'Charlie', 'Diana'],
    'Age': [25, 30, 35, 28],
    'Score': [85.5, 92.3, 78.9, 95.1]
})

# Calculate statistics
result = {
    'total_rows': len(df),
    'average_age': float(df['Age'].mean()),
    'max_score': float(df['Score'].max()),
    'min_score': float(df['Score'].min()),
    'age_std': float(df['Age'].std())
}
");
    
    using var stats = executor.CaptureVariable("result");
    var dict = stats?.ToDictionary();
    
    if (dict != null)
    {
        Console.WriteLine("  DataFrame Statistics:");
        Console.WriteLine($"    Total Rows:   {dict["total_rows"]}");
        Console.WriteLine($"    Average Age:  {dict["average_age"]:F1}");
        Console.WriteLine($"    Max Score:    {dict["max_score"]:F1}");
        Console.WriteLine($"    Min Score:    {dict["min_score"]:F1}");
        Console.WriteLine($"    Age Std Dev:  {dict["age_std"]:F2}");
    }
    Console.WriteLine();

    // Pass data from .NET to Python
    Console.WriteLine("[7] Passing .NET data to Python");
    Console.WriteLine(new string('-', 50));

    var netData = new[] { 10.5, 20.3, 30.7, 40.2, 50.8 };
    using var npResult = executor.ExecuteAndCapture(@"
import numpy as np

# 'net_data' is passed from .NET
arr = np.array(net_data)
result = {
    'input_data': net_data,
    'sum': float(np.sum(arr)),
    'mean': float(np.mean(arr)),
    'std': float(np.std(arr)),
    'squared': [float(x**2) for x in arr]
}
", new Dictionary<string, object?> { { "net_data", netData } });

    var npDict = npResult?.ToDictionary();
    if (npDict != null)
    {
        Console.WriteLine($"  Input Data: [{string.Join(", ", netData)}]");
        Console.WriteLine($"  Sum:        {npDict["sum"]:F1}");
        Console.WriteLine($"  Mean:       {npDict["mean"]:F2}");
        Console.WriteLine($"  Std Dev:    {npDict["std"]:F2}");
        
        var squared = npDict["squared"] as List<object?>;
        if (squared != null)
        {
            Console.WriteLine($"  Squared:    [{string.Join(", ", squared.Select(x => $"{x:F1}"))}]");
        }
    }
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Executor error: {ex.Message}");
}

// -----------------------------------------------------------------------------
// Step 6: Install additional packages on the fly
// -----------------------------------------------------------------------------
Console.WriteLine("[8] Installing additional packages");
Console.WriteLine(new string('-', 50));

try
{
    await project.InstallPackagesAsync(["scipy"]);
    Console.WriteLine("✓ scipy installed successfully");
    
    var scipyResult = await project.RunScriptAsync(@"
import scipy
print(f'SciPy version: {scipy.__version__}')

from scipy import stats
data = [1, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 5]
mode = stats.mode(data, keepdims=True)
print(f'Mode of data: {mode.mode[0]} (count: {mode.count[0]})')
");
    
    if (scipyResult.Success)
    {
        Console.WriteLine(scipyResult.Output?.Trim());
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  Note: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Sample Complete ===");

return 0;
