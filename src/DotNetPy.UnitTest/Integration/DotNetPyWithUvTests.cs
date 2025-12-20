namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Integration tests verifying DotNetPy works correctly with uv-managed Python environments.
/// Tests the actual DotNetPy library functionality with third-party packages.
/// </summary>
[TestClass]
public sealed class DotNetPyWithUvTests
{
    private static UvEnvironmentFixture? _fixture;
    private static DotNetPyExecutor? _executor;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        if (!UvCliHelper.IsAvailable)
        {
            context.WriteLine(UvCliHelper.GetSkipMessage());
            return;
        }

        context.WriteLine($"uv CLI detected: {UvCliHelper.Version}");

        _fixture = new UvEnvironmentFixture();
        var initialized = await _fixture.InitializeAsync();
        
        if (initialized && !string.IsNullOrEmpty(_fixture.PythonLibrary))
        {
            try
            {
                // Initialize DotNetPy with the uv-managed Python
                Python.Initialize(_fixture.PythonLibrary);
                _executor = Python.GetInstance();
                context.WriteLine($"DotNetPy initialized with Python: {_fixture.PythonVersion}");
            }
            catch (Exception ex)
            {
                context.WriteLine($"DotNetPy initialization failed: {ex.Message}");
            }
        }
        else
        {
            context.WriteLine("Python library not found in uv environment.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _executor?.Dispose();
        _fixture?.Dispose();
    }

    private void EnsureReady()
    {
        if (!UvCliHelper.IsAvailable)
        {
            Assert.Inconclusive(UvCliHelper.GetSkipMessage());
        }
        if (_fixture == null || !_fixture.IsEnvironmentReady)
        {
            Assert.Inconclusive("UV environment is not available.");
        }
        if (_executor == null)
        {
            Assert.Inconclusive("DotNetPy executor is not available. Python library may not be found in venv.");
        }
    }

    [TestMethod]
    public void DotNetPyWithUv_BasicEvaluation()
    {
        EnsureReady();

        var result = _executor!.Evaluate("1 + 2 + 3");
        Assert.IsNotNull(result);
        Assert.AreEqual(6, result.GetInt32());
    }

    [TestMethod]
    public void DotNetPyWithUv_ExecuteAndCapture()
    {
        EnsureReady();

        using var result = _executor!.ExecuteAndCapture(@"
import math
result = {
    'pi': math.pi,
    'e': math.e,
    'sqrt2': math.sqrt(2)
}
");

        Assert.IsNotNull(result);
        var dict = result.ToDictionary();
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.ContainsKey("pi"));
        Assert.IsTrue(dict.ContainsKey("e"));
    }

    [TestMethod]
    public void DotNetPyWithUv_PassDataToScript()
    {
        EnsureReady();

        var numbers = new[] { 10, 20, 30, 40, 50 };

        using var result = _executor!.ExecuteAndCapture(@"
result = {
    'sum': sum(numbers),
    'count': len(numbers),
    'average': sum(numbers) / len(numbers)
}
", new Dictionary<string, object?> { { "numbers", numbers } });

        Assert.IsNotNull(result);
        Assert.AreEqual(150, result.GetInt32("sum"));
        Assert.AreEqual(5, result.GetInt32("count"));
        Assert.AreEqual(30.0, result.GetDouble("average"));
    }

    [TestMethod]
    public void DotNetPyWithUv_VariableManagement()
    {
        EnsureReady();

        // Create variables
        _executor!.Execute(@"
test_var_a = 100
test_var_b = 'hello'
test_var_c = [1, 2, 3]
");

        // Check existence
        Assert.IsTrue(_executor.VariableExists("test_var_a"));
        Assert.IsTrue(_executor.VariableExists("test_var_b"));
        Assert.IsFalse(_executor.VariableExists("test_var_nonexistent"));

        // Capture variable
        using var captured = _executor.CaptureVariable("test_var_a");
        Assert.IsNotNull(captured);
        Assert.AreEqual(100, captured.GetInt32());

        // Delete variable
        var deleted = _executor.DeleteVariable("test_var_a");
        Assert.IsTrue(deleted);
        Assert.IsFalse(_executor.VariableExists("test_var_a"));

        // Cleanup
        _executor.DeleteVariables("test_var_b", "test_var_c");
    }

    [TestMethod]
    public void DotNetPyWithUv_ComplexDataStructures()
    {
        EnsureReady();

        using var result = _executor!.ExecuteAndCapture(@"
result = {
    'nested': {
        'level1': {
            'level2': {
                'value': 42
            }
        }
    },
    'list_of_dicts': [
        {'name': 'item1', 'value': 1},
        {'name': 'item2', 'value': 2}
    ]
}
");

        Assert.IsNotNull(result);
        var dict = result.ToDictionary();
        Assert.IsNotNull(dict);

        // Check nested structure
        var nested = dict["nested"] as Dictionary<string, object?>;
        Assert.IsNotNull(nested);
        
        var level1 = nested["level1"] as Dictionary<string, object?>;
        Assert.IsNotNull(level1);
    }

    [TestMethod]
    public void DotNetPyWithUv_PythonVersionInfo()
    {
        EnsureReady();

        using var result = _executor!.ExecuteAndCapture(@"
import sys
result = {
    'version': sys.version,
    'major': sys.version_info.major,
    'minor': sys.version_info.minor
}
");

        Assert.IsNotNull(result);
        var major = result.GetInt32("major");
        Assert.IsNotNull(major);
        Assert.IsGreaterThanOrEqualTo(3, major.Value, "Python 3.x expected");
        
        Console.WriteLine($"Python version from uv environment: {result.GetString("version")}");
    }
}
