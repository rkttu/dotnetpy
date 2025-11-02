namespace DotNetPy.UnitTest;

//[TestClass]
public sealed class SequentialTestRunner
{
    private static DotNetPyExecutor _executor = default!;
    private static readonly List<TestResult> _testResults = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Python 라이브러리 경로 설정
        var pythonLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        // Python이 설치되어 있지 않으면 테스트 스킵
        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        Python.Initialize(pythonLibraryPath);
        _executor = Python.GetInstance();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        // 테스트 결과 요약 출력
        Console.WriteLine("\n========================================");
        Console.WriteLine("테스트 실행 결과 요약");
        Console.WriteLine("========================================");

        var passed = _testResults.Count(r => r.Passed);
        var failed = _testResults.Count(r => !r.Passed);
        var total = _testResults.Count;

        foreach (var result in _testResults)
        {
            var status = result.Passed ? "✓ PASSED" : "✗ FAILED";
            Console.WriteLine($"{status}: {result.TestName}");
            if (!result.Passed && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                Console.WriteLine($"  Error: {result.ErrorMessage}");
            }
        }

        Console.WriteLine($"\n총 {total}개 테스트 중 {passed}개 성공, {failed}개 실패");
        Console.WriteLine("========================================\n");
    }

    [TestMethod]
    public void RunAllTestsSequentially()
    {
        _testResults.Clear();

        // 1. Evaluate Tests
        RunTestGroup("EvaluateTests", RunEvaluateTests);

        // 2. Execute And Capture Tests
        RunTestGroup("ExecuteAndCaptureTests", RunExecuteAndCaptureTests);

        // 3. Capture Manage Variable Tests
        RunTestGroup("CaptureManageVariableTests", RunCaptureManageVariableTests);

        // 4. Marshalling Tests
        RunTestGroup("MarshallingTests", RunMarshallingTests);

        // 5. Exception Handling Tests
        RunTestGroup("ExceptionHandlingTests", RunExceptionHandlingTests);

        // 6. Global Variable Cleanup Tests
        RunTestGroup("GlobalVariableCleanupTests", RunGlobalVariableCleanupTests);

        // 7. Complex Scenario Tests
        RunTestGroup("ComplexScenarioTests", RunComplexScenarioTests);

        // 실패한 테스트가 있으면 Assert 실패
        var failedTests = _testResults.Where(r => !r.Passed).ToList();
        if (failedTests.Any())
        {
            var failedNames = string.Join(", ", failedTests.Select(t => t.TestName));
            Assert.Fail($"{failedTests.Count}개의 테스트가 실패했습니다: {failedNames}");
        }
    }

    private static void RunTestGroup(string groupName, Action action)
    {
        Console.WriteLine($"\n--- {groupName} 실행 중 ---");
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"테스트 그룹 '{groupName}' 실행 중 예외 발생: {ex.Message}");
        }
    }

    private static void RunTest(string testName, Action testAction)
    {
        // 각 테스트 전에 전역 변수 정리
        _executor.ClearGlobals();

        Console.Write($"  {testName}... ");
        try
        {
            testAction();
            _testResults.Add(new TestResult { TestName = testName, Passed = true });
            Console.WriteLine("✓");
        }
        catch (Exception ex)
        {
            _testResults.Add(new TestResult
            {
                TestName = testName,
                Passed = false,
                ErrorMessage = ex.Message
            });
            Console.WriteLine($"✗ ({ex.Message})");
        }
    }

    #region EvaluateTests

    private static void RunEvaluateTests()
    {
        RunTest("SimpleArithmetic_ReturnsCorrectResult", () =>
        {
            var result = _executor.Evaluate("1 + 1");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.GetInt32());
        });

        RunTest("StringLength_ReturnsCorrectResult", () =>
     {
         var result = _executor.Evaluate("len('hello')");
         Assert.IsNotNull(result);
         Assert.AreEqual(5, result.GetInt32());
     });

        RunTest("ListSum_ReturnsCorrectResult", () =>
        {
            var result = _executor.Evaluate("sum([1, 2, 3, 4, 5])");
            Assert.IsNotNull(result);
            Assert.AreEqual(15, result.GetInt32());
        });

        RunTest("Execute_InvalidPythonCode_ThrowsException", () =>
{
    try
    {
        _executor.Execute("this is not valid python code @#$%");
        Assert.Fail("Expected DotNetPyException was not thrown");
    }
    catch (DotNetPyException)
    {
        // Expected exception
    }
});

        RunTest("ExecuteAndCapture_SimpleMath_ReturnsResult", () =>
    {
        var result = _executor.ExecuteAndCapture("result = 10 * 5");
        Assert.IsNotNull(result);
        Assert.AreEqual(50, result.GetInt32());
    });

        RunTest("ExecuteAndCapture_ImportModule_CalculatesSquareRoot", () =>
   {
       var code = @"
import math
result = math.sqrt(16)
";
       var result = _executor.ExecuteAndCapture(code);
       Assert.IsNotNull(result);
       Assert.AreEqual(4.0, result.GetDouble());
   });
    }

    #endregion

    #region ExecuteAndCaptureTests

    private static void RunExecuteAndCaptureTests()
    {
        RunTest("Execute_WithVariableInjection_UsesInjectedData", () =>
        {
            var numbers = new[] { 10, 20, 30, 40, 50 };
            var variables = new Dictionary<string, object?> { { "numbers", numbers } };

            _executor.Execute("result = sum(numbers)", variables);
            var result = _executor.CaptureVariable("result");

            Assert.IsNotNull(result);
            Assert.AreEqual(150, result.GetInt32());
        });

        RunTest("ExecuteAndCapture_WithVariableInjection_ReturnsStatistics", () =>
        {
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
            var result = _executor.ExecuteAndCapture(
          code,
           new Dictionary<string, object?> { { "numbers", numbers } });

            Assert.IsNotNull(result);
            Assert.AreEqual(150.0, result.GetDouble("sum"));
            Assert.AreEqual(30.0, result.GetDouble("average"));
            Assert.AreEqual(50, result.GetInt32("max"));
            Assert.AreEqual(10, result.GetInt32("min"));
        });

        RunTest("Execute_WithMultipleVariables_UsesAllVariables", () =>
        {
            var variables = new Dictionary<string, object?>
      {
              { "x", 10 },
            { "y", 20 },
 { "name", "Test" }
  };

            _executor.Execute("result = f'{name}: {x + y}'", variables);
            var result = _executor.CaptureVariable("result");

            Assert.IsNotNull(result);
            Assert.AreEqual("Test: 30", result.GetString());
        });
    }

    #endregion

    #region CaptureManageVariableTests

    private static void RunCaptureManageVariableTests()
    {
        RunTest("CaptureVariable_ExistingVariable_ReturnsValue", () =>
        {
            _executor.Execute(@"
import math
pi = math.pi
");
            var pi = _executor.CaptureVariable("pi");

            Assert.IsNotNull(pi);
            var piValue = pi.GetDouble();
            Assert.IsNotNull(piValue);
            Assert.AreEqual(Math.PI, piValue.Value, 0.0001);
        });

        RunTest("CaptureVariable_NonExistentVariable_ReturnsNull", () =>
        {
            var result = _executor.CaptureVariable("non_existent_var");
            Assert.IsNull(result);
        });

        RunTest("CaptureVariables_MultipleVariables_ReturnsAll", () =>
        {
            _executor.Execute(@"
x = 10
y = 20
z = 30
");
            using var results = _executor.CaptureVariables("x", "y", "z");

            Assert.IsNotNull(results);
            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(10, results["x"]?.GetInt32());
            Assert.AreEqual(20, results["y"]?.GetInt32());
            Assert.AreEqual(30, results["z"]?.GetInt32());
        });

        RunTest("VariableExists_ExistingVariable_ReturnsTrue", () =>
        {
            _executor.Execute("test_var = 'exists'");
            var exists = _executor.VariableExists("test_var");
            Assert.IsTrue(exists);
        });

        RunTest("VariableExists_NonExistentVariable_ReturnsFalse", () =>
        {
            var exists = _executor.VariableExists("non_existent");
            Assert.IsFalse(exists);
        });

        RunTest("GetExistingVariables_MixedVariables_ReturnsOnlyExisting", () =>
   {
       _executor.Execute(@"
apple = 'fruit'
banana = 'fruit'
carrot = 'vegetable'
");
       var existing = _executor.GetExistingVariables("apple", "banana", "orange", "carrot", "potato");

       Assert.IsNotNull(existing);
       Assert.HasCount(3, existing);
       CollectionAssert.Contains(existing.ToList(), "apple");
       CollectionAssert.Contains(existing.ToList(), "banana");
       CollectionAssert.Contains(existing.ToList(), "carrot");
       CollectionAssert.DoesNotContain(existing.ToList(), "orange");
       CollectionAssert.DoesNotContain(existing.ToList(), "potato");
   });

        RunTest("DeleteVariable_ExistingVariable_DeletesAndReturnsTrue", () =>
          {
              _executor.Execute("temp_var = 'temporary'");
              Assert.IsTrue(_executor.VariableExists("temp_var"));

              var deleted = _executor.DeleteVariable("temp_var");

              Assert.IsTrue(deleted);
              Assert.IsFalse(_executor.VariableExists("temp_var"));
          });

        RunTest("DeleteVariable_NonExistentVariable_ReturnsFalse", () =>
      {
          var deleted = _executor.DeleteVariable("non_existent");
          Assert.IsFalse(deleted);
      });

        RunTest("DeleteVariables_MultipleVariables_DeletesOnlyExisting", () =>
            {
                _executor.Execute(@"
x = 10
y = 20
z = 30
");
                var deleted = _executor.DeleteVariables("x", "y", "non_existent");

                Assert.IsNotNull(deleted);
                Assert.HasCount(2, deleted);
                CollectionAssert.Contains(deleted.ToList(), "x");
                CollectionAssert.Contains(deleted.ToList(), "y");
                Assert.IsFalse(_executor.VariableExists("x"));
                Assert.IsFalse(_executor.VariableExists("y"));
                Assert.IsTrue(_executor.VariableExists("z"));
            });
    }

    #endregion

    #region MarshallingTests

    private static void RunMarshallingTests()
    {
        RunTest("ToDictionary_PythonDict_ConvertsToDotNetDictionary", () =>
        {
            var code = @"
result = {
    'name': 'John Doe',
    'age': 30,
    'isStudent': False
}
";
            using var pyValue = _executor.ExecuteAndCapture(code);
            var dict = pyValue?.ToDictionary();

            Assert.IsNotNull(dict);
            Assert.AreEqual("John Doe", dict["name"]);
            Assert.AreEqual(30L, dict["age"]);
            Assert.IsFalse((bool?)dict["isStudent"]);
        });

        RunTest("ToDictionary_NestedDict_ConvertsNestedStructure", () =>
                {
                    var code = @"
result = {
    'person': {
        'name': 'Alice',
        'age': 25
    },
    'address': {
  'city': 'Seoul',
        'country': 'Korea'
    }
}
";
                    using var pyValue = _executor.ExecuteAndCapture(code);
                    var dict = pyValue?.ToDictionary();

                    Assert.IsNotNull(dict);
                    Assert.IsTrue(dict.ContainsKey("person"));
                    var person = dict["person"] as Dictionary<string, object?>;
                    Assert.IsNotNull(person);
                    Assert.AreEqual("Alice", person["name"]);
                });

        RunTest("ToDictionary_WithList_ConvertsListInDictionary", () =>
            {
                var code = @"
result = {
    'name': 'Project',
    'tags': ['python', 'dotnet', 'interop']
}
";
                using var pyValue = _executor.ExecuteAndCapture(code);
                var dict = pyValue?.ToDictionary();

                Assert.IsNotNull(dict);
                Assert.AreEqual("Project", dict["name"]);
                var tags = dict["tags"] as List<object?>;
                Assert.IsNotNull(tags);
                Assert.HasCount(3, tags);
                Assert.AreEqual("python", tags[0]);
            });

        RunTest("ToList_PythonList_ConvertsToDotNetList", () =>
        {
            var code = "result = [1, 2, 3, 4, 5]";

            using var pyValue = _executor.ExecuteAndCapture(code);
            var list = pyValue?.ToList();

            Assert.IsNotNull(list);
            Assert.HasCount(5, list);
            Assert.AreEqual(1L, list[0]);
            Assert.AreEqual(5L, list[4]);
        });

        RunTest("GetString_StringValue_ReturnsString", () =>
        {
            var code = "result = 'Hello, World!'";

            using var pyValue = _executor.ExecuteAndCapture(code);
            Assert.AreEqual("Hello, World!", pyValue?.GetString());
        });

        RunTest("GetInt32_IntegerValue_ReturnsInteger", () =>
        {
            var code = "result = 42";

            using var pyValue = _executor.ExecuteAndCapture(code);
            Assert.AreEqual(42, pyValue?.GetInt32());
        });

        RunTest("GetDouble_FloatValue_ReturnsDouble", () =>
             {
                 var code = "result = 3.14159";

                 using var pyValue = _executor.ExecuteAndCapture(code);

                 var doubleValue = pyValue?.GetDouble();
                 Assert.IsNotNull(doubleValue);
                 Assert.AreEqual(3.14159, doubleValue.Value, 0.00001);
             });

        RunTest("GetBoolean_BooleanValue_ReturnsBoolean", () =>
        {
            var code1 = "result = True";
            var code2 = "result = False";

            using var pyValue1 = _executor.ExecuteAndCapture(code1);
            using var pyValue2 = _executor.ExecuteAndCapture(code2);

            Assert.IsTrue(pyValue1?.GetBoolean());
            Assert.IsFalse(pyValue2?.GetBoolean());
        });
    }

    #endregion

    #region ExceptionHandlingTests

    private static void RunExceptionHandlingTests()
    {
        RunTest("Execute_PythonRuntimeError_ThrowsException", () =>
{
    try
    {
        _executor.Execute("result = 1 / 0");
        Assert.Fail("Expected DotNetPyException was not thrown");
    }
    catch (DotNetPyException)
    {
        // Expected exception
    }
});

        RunTest("Execute_UndefinedVariable_ThrowsException", () =>
        {
            try
            {
                _executor.Execute("result = undefined_variable");
                Assert.Fail("Expected DotNetPyException was not thrown");
            }
            catch (DotNetPyException)
            {
                // Expected exception
            }
        });
    }

    #endregion

    #region GlobalVariableCleanupTests

    private static void RunGlobalVariableCleanupTests()
    {
        RunTest("ClearGlobals_AfterExecute_RemovesUserVariables", () =>
              {
                  _executor.Execute(@"
x = 10
y = 20
z = 30
");
                  Assert.IsTrue(_executor.VariableExists("x"));

                  _executor.ClearGlobals();

                  Assert.IsFalse(_executor.VariableExists("x"));
                  Assert.IsFalse(_executor.VariableExists("y"));
                  Assert.IsFalse(_executor.VariableExists("z"));
              });
    }

    #endregion

    #region ComplexScenarioTests

    private static void RunComplexScenarioTests()
    {
        RunTest("ComplexScenario_DataProcessingPipeline_WorksCorrectly", () =>
        {
            var salesData = new[]
                  {
    new { Product = "A", Sales = 100 },
       new { Product = "B", Sales = 200 },
   new { Product = "C", Sales = 150 }
    };

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

            Assert.IsNotNull(result);
            Assert.AreEqual(450.0, result.GetDouble("total"));
            Assert.AreEqual(150.0, result.GetDouble("average"));
            Assert.AreEqual(3, result.GetInt32("count"));
        });

        RunTest("ComplexScenario_MachineLearningSimulation_CalculatesCorrectly", () =>
        {
            var features = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            var weights = new[] { 0.1, 0.2, 0.3, 0.4, 0.5 };

            var code = @"
result = sum(f * w for f, w in zip(features, weights))
";
            using var result = _executor.ExecuteAndCapture(
           code,
             new Dictionary<string, object?> {
             { "features", features },
{ "weights", weights }
                });

            Assert.IsNotNull(result);
            // 1*0.1 + 2*0.2 + 3*0.3 + 4*0.4 + 5*0.5 = 5.5
            var resultValue = result.GetDouble();
            Assert.IsNotNull(resultValue);
            Assert.AreEqual(5.5, resultValue.Value, 0.0001);
        });
    }

    #endregion

    private sealed class TestResult
    {
        public string TestName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
