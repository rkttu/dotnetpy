namespace DotNetPy.UnitTest;

[TestClass]
public sealed class ExecuteAndCaptureTests
{
    // 변수 주입 및 데이터 교환 테스트
    private static DotNetPyExecutor _executor = default!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Python 라이브러리 경로 설정 (환경에 맞게 수정 필요)
        var pythonLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        // Python이 설치되어 있지 않으면 테스트 스킵
        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        Python.Initialize(pythonLibraryPath);
        _executor = Python.GetInstance();
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
