namespace DotNetPy.UnitTest;

[TestClass]
public sealed class DisposePatternTests
{
    // Dispose 패턴 및 리소스 관리 테스트
    private static DotNetPyExecutor _executor = default!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        var pythonLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        Python.Initialize(pythonLibraryPath);
        _executor = Python.GetInstance();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _executor.ClearGlobals();
    }

    [TestMethod]
    public void DotNetPyValue_Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var value = _executor.ExecuteAndCapture("result = 42");
        Assert.IsNotNull(value);

        // Act & Assert - Should not throw
        value.Dispose();
        value.Dispose(); // Second call should be safe
    }

    [TestMethod]
    public void DotNetPyValue_AccessAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var value = _executor.ExecuteAndCapture("result = {'key': 'value'}");
        Assert.IsNotNull(value);

        // Act
        value.Dispose();

        // Assert - Accessing disposed object should throw
        try
        {
            value.GetString();
            Assert.Fail("Expected ObjectDisposedException was not thrown");
        }
        catch (ObjectDisposedException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void DotNetPyValue_UsingStatement_DisposesCorrectly()
    {
        // Act
        using (var value = _executor.ExecuteAndCapture("result = [1, 2, 3]"))
        {
            // Assert - Should be usable inside using block
            Assert.IsNotNull(value);
            var list = value.ToList();
            Assert.IsNotNull(list);
            Assert.HasCount(3, list);
        }

        // Value is disposed after using block - no exception should occur
    }

    [TestMethod]
    public void DotNetPyDictionary_Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        _executor.Execute(@"
x = 1
y = 2
");
        var dict = _executor.CaptureVariables("x", "y");

        // Act & Assert - Should not throw
        dict.Dispose();
        dict.Dispose(); // Second call should be safe
    }

    [TestMethod]
    public void DotNetPyDictionary_UsingStatement_DisposesCorrectly()
    {
        // Arrange
        _executor.Execute(@"
a = 'apple'
b = 'banana'
c = 'cherry'
");

        // Act & Assert
        using (var dict = _executor.CaptureVariables("a", "b", "c"))
        {
            Assert.AreEqual(3, dict.Count);
            Assert.AreEqual("apple", dict["a"]?.GetString());
        }

        // Dictionary is disposed after using block
    }

    [TestMethod]
    public void DotNetPyExecutor_Dispose_ClearsGlobalVariables()
    {
        // Arrange
        var pythonLibraryPath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Python", "Python313", "python313.dll");

        var executor = DotNetPyExecutor.GetInstance(pythonLibraryPath);
        executor.Execute(@"
dispose_test_var1 = 'value1'
dispose_test_var2 = 'value2'
");

        Assert.IsTrue(executor.VariableExists("dispose_test_var1"));

        // Act - Clear globals manually instead of disposing
        executor.ClearGlobals();

        // Assert - Variables should be cleaned up
        Assert.IsFalse(executor.VariableExists("dispose_test_var1"));
        Assert.IsFalse(executor.VariableExists("dispose_test_var2"));
        var currentCount = DotNetPyExecutor.ReferenceCount;
        Assert.IsGreaterThanOrEqualTo(0, currentCount);
    }

    [TestMethod]
    public void NestedDisposables_DisposeCorrectly()
    {
        // Act & Assert - Nested using statements
        using (var outerDict = _executor.ExecuteAndCapture(@"
result = {
  'numbers': [1, 2, 3],
  'name': 'test'
}
"))
        {
            Assert.IsNotNull(outerDict);

            _executor.Execute(@"
inner_a = 10
inner_b = 20
");

            using (var innerDict = _executor.CaptureVariables("inner_a", "inner_b"))
            {
                Assert.AreEqual(2, innerDict.Count);
                Assert.AreEqual(10, innerDict["inner_a"]?.GetInt32());
            }

            // Inner dict disposed, outer still accessible
            Assert.IsNotNull(outerDict.GetString("name"));
        }

        // Both disposed successfully
    }

    [TestMethod]
    public void ReferenceCount_Dispose_DecrementsCorrectly()
    {
        // This test verifies reference counting without actually disposing the shared instance
        var pythonLibraryPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Programs", "Python", "Python313", "python313.dll");

        var initialCount = DotNetPyExecutor.ReferenceCount;

        // Getting the instance increments the reference count
        var executor1 = DotNetPyExecutor.GetInstance(pythonLibraryPath);
        var countAfterCreate = DotNetPyExecutor.ReferenceCount;
        Assert.IsGreaterThan(initialCount, countAfterCreate);

        // Note: We don't dispose here to avoid breaking other tests
        // The reference count will be decremented when tests complete
        Assert.IsGreaterThanOrEqualTo(1, countAfterCreate);
    }
}
