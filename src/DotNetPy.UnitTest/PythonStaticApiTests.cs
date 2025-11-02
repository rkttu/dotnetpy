namespace DotNetPy.UnitTest;

[TestClass]
public sealed class PythonStaticApiTests
{
    // Python 정적 클래스 API 테스트

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        try
        {
            // Use automatic Python discovery
            Python.Initialize();
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found: {ex.Message}");
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        Python.GetInstance().ClearGlobals();
    }

    [TestMethod]
    public void GetInstance_ReturnsSameInstance()
    {
        // Act
        var instance1 = Python.GetInstance();
        var instance2 = Python.GetInstance();

        // Assert
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void Execute_SimpleCode_ExecutesSuccessfully()
    {
        // Act & Assert - Should not throw
        Python.Execute("x = 10 + 20");

        // Verify execution
        Assert.IsTrue(Python.VariableExists("x"));
    }

    [TestMethod]
    public void Execute_WithVariables_InjectsVariables()
    {
        // Arrange
        var variables = new Dictionary<string, object?> { { "input", 42 } };

        // Act
        Python.Execute("output = input * 2", variables);

        // Assert
        Assert.IsTrue(Python.VariableExists("output"));
        var result = Python.CaptureVariable("output");
        Assert.AreEqual(84, result?.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_SimpleExpression_ReturnsResult()
    {
        // Act
        var result = Python.ExecuteAndCapture("result = 5 * 5");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(25, result.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_WithVariables_UsesVariablesAndReturnsResult()
    {
        // Arrange
        var variables = new Dictionary<string, object?> { { "base", 10 } };

        // Act
        var result = Python.ExecuteAndCapture("result = base ** 2", variables);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(100, result.GetInt32());
    }

    [TestMethod]
    public void ExecuteAndCapture_CustomResultVariable_CapturesCorrectVariable()
    {
        // Act
        var result = Python.ExecuteAndCapture("custom_var = 'custom value'", "custom_var");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("custom value", result.GetString());
    }

    [TestMethod]
    public void ExecuteAndCapture_WithVariablesAndCustomResultVariable_WorksCorrectly()
    {
        // Arrange
        var variables = new Dictionary<string, object?> { { "multiplier", 7 } };

        // Act
        var result = Python.ExecuteAndCapture(
          "my_result = multiplier * 6",
            variables,
     "my_result");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.GetInt32());
    }

    [TestMethod]
    public void Evaluate_SimpleExpression_ReturnsResult()
    {
        // Act
        var result = Python.Evaluate("2 + 2");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.GetInt32());
    }

    [TestMethod]
    public void VariableExists_ExistingVariable_ReturnsTrue()
    {
        // Arrange
        Python.Execute("test_var = 'exists'");

        // Act
        var exists = Python.VariableExists("test_var");

        // Assert
        Assert.IsTrue(exists);
    }

    [TestMethod]
    public void VariableExists_NonExistentVariable_ReturnsFalse()
    {
        // Act
        var exists = Python.VariableExists("does_not_exist");

        // Assert
        Assert.IsFalse(exists);
    }

    [TestMethod]
    public void GetExistingVariables_MixedVariables_ReturnsOnlyExisting()
    {
        // Arrange
        Python.Execute(@"
var_a = 1
var_b = 2
");

        // Act
        var existing = Python.GetExistingVariables("var_a", "var_b", "var_c");

        // Assert
        Assert.HasCount(2, existing);
        CollectionAssert.Contains(existing.ToList(), "var_a");
        CollectionAssert.Contains(existing.ToList(), "var_b");
    }

    [TestMethod]
    public void CaptureVariable_ExistingVariable_ReturnsValue()
    {
        // Arrange
        Python.Execute("captured = 123");

        // Act
        var value = Python.CaptureVariable("captured");

        // Assert
        Assert.IsNotNull(value);
        Assert.AreEqual(123, value.GetInt32());
    }

    [TestMethod]
    public void CaptureVariable_NonExistentVariable_ReturnsNull()
    {
        // Act
        var value = Python.CaptureVariable("not_there");

        // Assert
        Assert.IsNull(value);
    }

    [TestMethod]
    public void CaptureVariables_MultipleVariables_ReturnsAll()
    {
        // Arrange
        Python.Execute(@"
first = 'one'
second = 'two'
third = 'three'
");

        // Act
        using var captured = Python.CaptureVariables("first", "second", "third");

        // Assert
        Assert.AreEqual(3, captured.Count);
        Assert.AreEqual("one", captured["first"]?.GetString());
        Assert.AreEqual("two", captured["second"]?.GetString());
        Assert.AreEqual("three", captured["third"]?.GetString());
    }

    [TestMethod]
    public void DeleteVariable_ExistingVariable_DeletesAndReturnsTrue()
    {
        // Arrange
        Python.Execute("to_delete = 'temporary'");

        // Act
        var deleted = Python.DeleteVariable("to_delete");

        // Assert
        Assert.IsTrue(deleted);
        Assert.IsFalse(Python.VariableExists("to_delete"));
    }

    [TestMethod]
    public void DeleteVariable_NonExistentVariable_ReturnsFalse()
    {
        // Act
        var deleted = Python.DeleteVariable("never_existed");

        // Assert
        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public void DeleteVariables_MultipleVariables_DeletesOnlyExisting()
    {
        // Arrange
        Python.Execute(@"
del_a = 1
del_b = 2
");

        // Act
        var deleted = Python.DeleteVariables("del_a", "del_b", "del_c");

        // Assert
        Assert.HasCount(2, deleted);
        CollectionAssert.Contains(deleted.ToList(), "del_a");
        CollectionAssert.Contains(deleted.ToList(), "del_b");
        Assert.IsFalse(Python.VariableExists("del_a"));
        Assert.IsFalse(Python.VariableExists("del_b"));
    }

    [TestMethod]
    public void ClearGlobals_RemovesAllUserVariables()
    {
        // Arrange
        Python.Execute(@"
global_a = 1
global_b = 2
global_c = 3
");
        Assert.IsTrue(Python.VariableExists("global_a"));

        // Act
        Python.ClearGlobals();

        // Assert
        Assert.IsFalse(Python.VariableExists("global_a"));
        Assert.IsFalse(Python.VariableExists("global_b"));
        Assert.IsFalse(Python.VariableExists("global_c"));
    }
}
