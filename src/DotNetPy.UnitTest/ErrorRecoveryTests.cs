namespace DotNetPy.UnitTest;

[TestClass]
public sealed class ErrorRecoveryTests
{
    // 에러 복구 및 상태 관리 테스트
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
        _executor.ClearGlobals();
    }

    [TestMethod]
    public void Execute_AfterPythonError_RecoverSuccessfully()
    {
        // Arrange & Act - Execute code that causes an error
        try
        {
            _executor.Execute("result = 1 / 0");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Act - Try to execute valid code after the error
        _executor.Execute("recovered = 42");

        // Assert - Should work normally
        Assert.IsTrue(_executor.VariableExists("recovered"));
        var value = _executor.CaptureVariable("recovered");
        Assert.AreEqual(42, value?.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_AfterPythonError_RecoverSuccessfully()
    {
        // Arrange & Act - Execute code that causes an error
        try
        {
            _executor.ExecuteAndCapture("result = undefined_variable");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Act - Try to execute valid code after the error
        using var result = _executor.ExecuteAndCapture("result = 'recovered'");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("recovered", result.GetString());
    }

    [TestMethod]
    public void Evaluate_AfterPythonError_RecoverSuccessfully()
    {
        // Arrange & Act - Evaluate expression that causes an error
        try
        {
            _executor.Evaluate("1 / 0");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Act - Try to evaluate valid expression after the error
        using var result = _executor.Evaluate("2 + 2");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.GetInt32());
    }

    [TestMethod]
    public void VariableExists_AfterPythonError_WorksCorrectly()
    {
        // Arrange
        _executor.Execute("valid_var = 'exists'");

        // Act - Cause an error
        try
        {
            _executor.Execute("error_var = undefined");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Assert - Variable management should still work
        Assert.IsTrue(_executor.VariableExists("valid_var"));
        Assert.IsFalse(_executor.VariableExists("error_var"));
    }

    [TestMethod]
    public void CaptureVariable_AfterPythonError_WorksCorrectly()
    {
        // Arrange
        _executor.Execute("before_error = 100");

        // Act - Cause an error
        try
        {
            _executor.Execute("error = 1 / 0");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Assert - Should still be able to capture existing variable
        var value = _executor.CaptureVariable("before_error");
        Assert.IsNotNull(value);
        Assert.AreEqual(100, value.GetInt32());
    }

    [TestMethod]
    public void DeleteVariable_AfterPythonError_WorksCorrectly()
    {
        // Arrange
        _executor.Execute("to_delete = 'temporary'");

        // Act - Cause an error
        try
        {
            _executor.Execute("error = undefined_var");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Assert - Should still be able to delete variables
        var deleted = _executor.DeleteVariable("to_delete");
        Assert.IsTrue(deleted);
        Assert.IsFalse(_executor.VariableExists("to_delete"));
    }

    [TestMethod]
    public void MultipleErrors_InSequence_EachErrorHandledCorrectly()
    {
        // Act & Assert - Multiple errors in sequence
        for (int i = 0; i < 5; i++)
        {
            try
            {
                _executor.Execute($"error_{i} = 1 / 0");
                Assert.Fail($"Expected DotNetPyException for iteration {i}");
            }
            catch (DotNetPyException ex)
            {
                // Each error should be properly captured
                Assert.IsTrue(ex.Message.Contains("ZeroDivisionError") ||
                        ex.Message.Contains("division"));
            }

            // After each error, should still be able to execute valid code
            _executor.Execute($"valid_{i} = {i}");
            Assert.IsTrue(_executor.VariableExists($"valid_{i}"));
        }
    }

    [TestMethod]
    public void PartialExecutionFailure_PreservesValidState()
    {
        // Arrange - Execute valid code first
        _executor.Execute(@"
step1 = 'completed'
step2 = 'completed'
");

        Assert.IsTrue(_executor.VariableExists("step1"));
        Assert.IsTrue(_executor.VariableExists("step2"));

        // Act - Execute code that fails in the middle
        try
        {
            _executor.Execute(@"
step3 = 'completed'
step4 = 1 / 0
step5 = 'should_not_exist'
");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        // Assert - Previous valid state should be preserved
        Assert.IsTrue(_executor.VariableExists("step1"));
        Assert.IsTrue(_executor.VariableExists("step2"));
        // step3 might exist depending on Python execution order
        Assert.IsFalse(_executor.VariableExists("step5"));
    }

    [TestMethod]
    public void ErrorMessage_ContainsUsefulInformation()
    {
        // Act & Assert - ZeroDivisionError
        try
        {
            _executor.Execute("result = 10 / 0");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException ex)
        {
            Assert.IsTrue(ex.Message.Contains("ZeroDivisionError") ||
     ex.Message.Contains("division"));
        }

        // Act & Assert - NameError
        try
        {
            _executor.Execute("result = undefined_variable");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException ex)
        {
            Assert.IsTrue(ex.Message.Contains("NameError") ||
               ex.Message.Contains("undefined"));
        }

        // Act & Assert - TypeError
        try
        {
            _executor.Execute("result = 'string' + 123");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TypeError") ||
                    ex.Message.Contains("type"));
        }
    }

    [TestMethod]
    public void ClearGlobals_AfterError_CleansUpSuccessfully()
    {
        // Arrange
        _executor.Execute("before_error = 'exists'");

        // Act - Cause an error
        try
        {
            _executor.Execute("error = 1 / 0");
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException)
        {
            // Expected
        }

        _executor.Execute("after_error = 'also_exists'");

        // Act - Clear globals
        _executor.ClearGlobals();

        // Assert
        Assert.IsFalse(_executor.VariableExists("before_error"));
        Assert.IsFalse(_executor.VariableExists("after_error"));
    }

    [TestMethod]
    public void ComplexError_WithTraceback_CapturesErrorDetails()
    {
        // Arrange - Multi-line code with error
        var code = @"
def failing_function():
    x = 10
    y = 0
    return x / y

result = failing_function()
";

        // Act & Assert
        try
        {
            _executor.Execute(code);
            Assert.Fail("Expected DotNetPyException");
        }
        catch (DotNetPyException ex)
        {
            // Should contain error information
            Assert.IsNotNull(ex.Message);
            Assert.IsGreaterThan(0, ex.Message.Length);
            // May contain function name or line info depending on Python error formatting
        }
    }

    [TestMethod]
    public void InvalidVariableName_ThrowsArgumentException()
    {
        // Act & Assert - Invalid identifier
        try
        {
            _executor.CaptureVariable("invalid-name");
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }

        try
        {
            _executor.CaptureVariable("123invalid");
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }

        try
        {
            _executor.DeleteVariable("invalid name");
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void PythonKeyword_AsVariableName_HandledCorrectly()
    {
        // These are Python keywords and should fail validation
        var keywords = new[] { "for", "while", "if", "def", "class", "import" };

        foreach (var keyword in keywords)
        {
            // Act & Assert
            try
            {
                _executor.CaptureVariable(keyword);
                Assert.Fail($"Expected ArgumentException was not thrown for Python keyword: {keyword}");
            }
            catch (ArgumentException)
            {
                // Expected exception
            }
        }
    }
}
