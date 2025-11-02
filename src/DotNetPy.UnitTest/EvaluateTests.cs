namespace DotNetPy.UnitTest;

[TestClass]
public sealed class EvaluateTests
{
    // 기본 실행 및 평가 테스트
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
    public void SimpleArithmetic_ReturnsCorrectResult()
    {
        // Arrange & Act
        var result = _executor.Evaluate("1 + 1");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.GetInt32());
    }

    [TestMethod]
    public void StringLength_ReturnsCorrectResult()
    {
        // Arrange & Act
        var result = _executor.Evaluate("len('hello')");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(5, result.GetInt32());
    }

    [TestMethod]
    public void ListSum_ReturnsCorrectResult()
    {
        // Arrange & Act
        var result = _executor.Evaluate("sum([1, 2, 3, 4, 5])");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(15, result.GetInt32());
    }

    [TestMethod]
    public void Execute_InvalidPythonCode_ThrowsException()
    {
        // Act & Assert
        try
        {
            _executor.Execute("this is not valid python code @#$%");
            Assert.Fail("Expected DotNetPyException was not thrown");
        }
        catch (DotNetPyException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void ExecuteAndCapture_SimpleMath_ReturnsResult()
    {
        // Arrange & Act
        var result = _executor.ExecuteAndCapture("result = 10 * 5");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(50, result.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_ImportModule_CalculatesSquareRoot()
    {
        // Arrange
        var code = @"
import math
result = math.sqrt(16)
";

        // Act
        var result = _executor.ExecuteAndCapture(code);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4.0, result.GetDouble());
    }
}
