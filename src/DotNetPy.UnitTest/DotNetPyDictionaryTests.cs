namespace DotNetPy.UnitTest;

[TestClass]
public sealed class DotNetPyDictionaryTests
{
    // DotNetPyDictionary 전체 메서드 테스트
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
    public void Indexer_ExistingKey_ReturnsValue()
    {
        // Arrange
        _executor.Execute(@"
x = 10
y = 20
z = 30
");

        // Act
        using var dict = _executor.CaptureVariables("x", "y", "z");

        // Assert
        Assert.IsNotNull(dict["x"]);
        Assert.AreEqual(10, dict["x"]?.GetInt32());
        Assert.IsNotNull(dict["y"]);
        Assert.AreEqual(20, dict["y"]?.GetInt32());
    }

    [TestMethod]
    public void Indexer_NonExistentKey_ThrowsKeyNotFoundException()
    {
        // Arrange
        _executor.Execute("x = 10");
        using var dict = _executor.CaptureVariables("x");

        // Act & Assert
        try
        {
            var _ = dict["non_existent"];
            Assert.Fail("Expected KeyNotFoundException was not thrown");
        }
        catch (KeyNotFoundException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void ContainsKey_ExistingKey_ReturnsTrue()
    {
        // Arrange
        _executor.Execute(@"
apple = 'fruit'
banana = 'fruit'
");

        // Act
        using var dict = _executor.CaptureVariables("apple", "banana");

        // Assert
        Assert.IsTrue(dict.ContainsKey("apple"));
        Assert.IsTrue(dict.ContainsKey("banana"));
    }

    [TestMethod]
    public void ContainsKey_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        _executor.Execute("apple = 'fruit'");

        // Act
        using var dict = _executor.CaptureVariables("apple");

        // Assert
        Assert.IsFalse(dict.ContainsKey("orange"));
    }

    [TestMethod]
    public void TryGetValue_ExistingKey_ReturnsTrueAndValue()
    {
        // Arrange
        _executor.Execute("test_var = 42");

        // Act
        using var dict = _executor.CaptureVariables("test_var");
        var success = dict.TryGetValue("test_var", out var value);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(value);
        Assert.AreEqual(42, value.GetInt32());
    }

    [TestMethod]
    public void TryGetValue_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        _executor.Execute("test_var = 42");

        // Act
        using var dict = _executor.CaptureVariables("test_var");
        var success = dict.TryGetValue("non_existent", out var value);

        // Assert
        Assert.IsFalse(success);
        Assert.IsNull(value);
    }

    [TestMethod]
    public void Keys_MultipleVariables_ReturnsAllKeys()
    {
        // Arrange
        _executor.Execute(@"
var1 = 1
var2 = 2
var3 = 3
");

        // Act
        using var dict = _executor.CaptureVariables("var1", "var2", "var3");
        var keys = dict.Keys.ToList();

        // Assert
        Assert.HasCount(3, keys);
        CollectionAssert.Contains(keys, "var1");
        CollectionAssert.Contains(keys, "var2");
        CollectionAssert.Contains(keys, "var3");
    }

    [TestMethod]
    public void Values_MultipleVariables_ReturnsAllValues()
    {
        // Arrange
        _executor.Execute(@"
num1 = 10
num2 = 20
num3 = 30
");

        // Act
        using var dict = _executor.CaptureVariables("num1", "num2", "num3");
        var values = dict.Values.ToList();

        // Assert
        Assert.HasCount(3, values);
        Assert.IsTrue(values.Any(v => v?.GetInt32() == 10));
        Assert.IsTrue(values.Any(v => v?.GetInt32() == 20));
        Assert.IsTrue(values.Any(v => v?.GetInt32() == 30));
    }

    [TestMethod]
    public void Count_MultipleVariables_ReturnsCorrectCount()
    {
        // Arrange
        _executor.Execute(@"
a = 1
b = 2
c = 3
d = 4
");

        // Act
        using var dict = _executor.CaptureVariables("a", "b", "c", "d");

        // Assert
        Assert.AreEqual(4, dict.Count);
    }

    [TestMethod]
    public void Count_EmptyDictionary_ReturnsZero()
    {
        // Act
        using var dict = _executor.CaptureVariables();

        // Assert
        Assert.AreEqual(0, dict.Count);
    }

    [TestMethod]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        _executor.Execute("x = 10");
        var dict = _executor.CaptureVariables("x");

        // Act & Assert - Should not throw
        dict.Dispose();
        dict.Dispose(); // Second call should be safe
    }

    [TestMethod]
    public void Dispose_DisposesAllValues()
    {
        // Arrange
        _executor.Execute(@"
v1 = 'test1'
v2 = 'test2'
v3 = 'test3'
");

        // Act
        var dict = _executor.CaptureVariables("v1", "v2", "v3");

        // Access values before dispose
        Assert.IsNotNull(dict["v1"]);

        // Dispose
        dict.Dispose();

        // Assert - Values should be disposed (accessing them would be invalid)
        // We can't test the disposed state directly, but we verified disposal doesn't throw
    }

    [TestMethod]
    public void CaptureVariables_WithNullVariables_ReturnsNullValues()
    {
        // Arrange
        _executor.Execute(@"
exists = 42
");

        // Act - Capture both existing and non-existing variables
        using var dict = _executor.CaptureVariables("exists", "not_exists");

        // Assert
        Assert.AreEqual(2, dict.Count);
        Assert.IsNotNull(dict["exists"]);
        Assert.AreEqual(42, dict["exists"]?.GetInt32());
        Assert.IsNull(dict["not_exists"]);
    }
}
