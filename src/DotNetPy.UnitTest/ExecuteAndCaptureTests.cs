namespace DotNetPy.UnitTest;

[TestClass]
public sealed class ExecuteAndCaptureTests
{
    // 변수 주입 및 데이터 교환 테스트
    private static DotNetPyExecutor _executor = default!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        try
        {
            // Use automatic Python discovery
            Python.Initialize();
            _executor = Python.GetInstance();
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found: {ex.Message}");
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        // 각 테스트 전에 전역 변수 정리
        _executor.ClearGlobals();
    }

    [TestMethod]
    public void Execute_WithVariableInjection_UsesInjectedData()
    {
        // Skip on Linux CI where native Python extension modules don't work
        TestHelpers.SkipIfNativeExtensionsUnavailable();

        // Arrange
        var numbers = new[] { 10, 20, 30, 40, 50 };
        var variables = new Dictionary<string, object?> { { "numbers", numbers } };

        // Act
        _executor.Execute("result = sum(numbers)", variables);
        var result = _executor.CaptureVariable("result");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(150, result.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_WithVariableInjection_ReturnsStatistics()
    {
        // Skip on Linux CI where native Python extension modules don't work
        TestHelpers.SkipIfNativeExtensionsUnavailable();

        // Arrange
        var numbers = new[] { 10, 20, 30, 40, 50 };
        var code = @"
import statistics
result = {
 'sum': sum(numbers),
    'average': statistics.mean(numbers),
    'max': max(numbers),
    'min': min(numbers)
}
";

        // Act
        var result = _executor.ExecuteAndCapture(
            code,
            new Dictionary<string, object?> { { "numbers", numbers } });

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(150.0, result.GetDouble("sum"));
        Assert.AreEqual(30.0, result.GetDouble("average"));
        Assert.AreEqual(50, result.GetInt32("max"));
        Assert.AreEqual(10, result.GetInt32("min"));
    }

    [TestMethod]
    public void Execute_WithMultipleVariables_UsesAllVariables()
    {
        // Skip on Linux CI where native Python extension modules don't work
        TestHelpers.SkipIfNativeExtensionsUnavailable();

        // Arrange
        var variables = new Dictionary<string, object?>
        {
            { "x", 10 },
            { "y", 20 },
            { "name", "Test" }
        };

        // Act
        _executor.Execute("result = f'{name}: {x + y}'", variables);
        var result = _executor.CaptureVariable("result");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Test: 30", result.GetString());
    }
}
