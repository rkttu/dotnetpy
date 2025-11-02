namespace DotNetPy.UnitTest;

[TestClass]
public sealed class ComplexScenarioTest
{
    // 복잡한 시나리오 테스트
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
    public void ComplexScenario_DataProcessingPipeline_WorksCorrectly()
    {
        // Arrange - .NET에서 데이터 준비
        var salesData = new[]
        {
            new { Product = "A", Sales = 100 },
            new { Product = "B", Sales = 200 },
            new { Product = "C", Sales = 150 }
        };

        // Act - Python으로 데이터 전송 및 처리
        var code = @"
total_sales = sum(item['Sales'] for item in sales_data)
average_sales = total_sales / len(sales_data)
result = {
    'total': total_sales,
  'average': average_sales,
    'count': len(sales_data)
}
";
        using var result = _executor.ExecuteAndCapture(
            code,
            new Dictionary<string, object?> { { "sales_data", salesData } }
            );

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(450.0, result.GetDouble("total"));
        Assert.AreEqual(150.0, result.GetDouble("average"));
        Assert.AreEqual(3, result.GetInt32("count"));
    }

    [TestMethod]
    public void ComplexScenario_MachineLearningSimulation_CalculatesCorrectly()
    {
        // Arrange
        var features = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var weights = new[] { 0.1, 0.2, 0.3, 0.4, 0.5 };

        // Act - 간단한 선형 모델 시뮬레이션
        var code = @"
result = sum(f * w for f, w in zip(features, weights))
";
        using var result = _executor.ExecuteAndCapture(
            code,
            new Dictionary<string, object?> {
                { "features", features },
                { "weights", weights }
            });

        // Assert
        Assert.IsNotNull(result);
        // 1*0.1 + 2*0.2 + 3*0.3 + 4*0.4 + 5*0.5 = 5.5
        var resultValue = result.GetDouble();
        Assert.IsNotNull(resultValue);
        Assert.AreEqual(5.5, resultValue.Value, 0.0001);
    }
}
