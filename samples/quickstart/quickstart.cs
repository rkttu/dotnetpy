#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

using DotNetPy;

Python.Initialize();
var executor = Python.GetInstance();
var temperatures = new[] { 23.5, 19.2, 31.8, 27.4, 22.1 };
using var result = executor.ExecuteAndCapture(@"
    import statistics
    result = {'mean': statistics.mean(data), 'stdev': statistics.stdev(data)}
", new Dictionary<string, object?> { { "data", temperatures } });
Console.WriteLine($"Mean: {result?.GetDouble("mean"):F1}°C ± {result?.GetDouble("stdev"):F1}");
