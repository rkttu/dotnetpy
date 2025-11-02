namespace DotNetPy.UnitTest;

[TestClass]
public sealed class MarshallingTests
{
    // 데이터 타입 변환 테스트
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
    public void ToDictionary_PythonDict_ConvertsToDotNetDictionary()
    {
        // Arrange
        var code = @"
result = {
    'name': 'John Doe',
    'age': 30,
    'isStudent': False
}
";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);
        var dict = pyValue?.ToDictionary();

        // Assert
        Assert.IsNotNull(dict);
        Assert.AreEqual("John Doe", dict["name"]);
        Assert.AreEqual(30L, dict["age"]);
        Assert.IsFalse((bool?)dict["isStudent"]);
    }

    [TestMethod]
    public void ToDictionary_NestedDict_ConvertsNestedStructure()
    {
        // Arrange
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

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);
        var dict = pyValue?.ToDictionary();

        // Assert
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.ContainsKey("person"));
        var person = dict["person"] as Dictionary<string, object?>;
        Assert.IsNotNull(person);
        Assert.AreEqual("Alice", person["name"]);
    }

    [TestMethod]
    public void ToDictionary_WithList_ConvertsListInDictionary()
    {
        // Arrange
        var code = @"
result = {
    'name': 'Project',
    'tags': ['python', 'dotnet', 'interop']
}
";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);
        var dict = pyValue?.ToDictionary();

        // Assert
        Assert.IsNotNull(dict);
        Assert.AreEqual("Project", dict["name"]);
        var tags = dict["tags"] as List<object?>;
        Assert.IsNotNull(tags);
        Assert.HasCount(3, tags);
        Assert.AreEqual("python", tags[0]);
    }

    [TestMethod]
    public void ToList_PythonList_ConvertsToDotNetList()
    {
        // Arrange
        var code = "result = [1, 2, 3, 4, 5]";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);
        var list = pyValue?.ToList();

        // Assert
        Assert.IsNotNull(list);
        Assert.HasCount(5, list);
        Assert.AreEqual(1L, list[0]);
        Assert.AreEqual(5L, list[4]);
    }

    [TestMethod]
    public void GetString_StringValue_ReturnsString()
    {
        // Arrange
        var code = "result = 'Hello, World!'";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);

        // Assert
        Assert.AreEqual("Hello, World!", pyValue?.GetString());
    }

    [TestMethod]
    public void GetInt32_IntegerValue_ReturnsInteger()
    {
        // Arrange
        var code = "result = 42";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);

        // Assert
        Assert.AreEqual(42, pyValue?.GetInt32());
    }

    [TestMethod]
    public void GetDouble_FloatValue_ReturnsDouble()
    {
        // Arrange
        var code = "result = 3.14159";

        // Act
        using var pyValue = _executor.ExecuteAndCapture(code);

        // Assert
        var doubleValue = pyValue?.GetDouble();
        Assert.IsNotNull(doubleValue);
        Assert.AreEqual(3.14159, doubleValue.Value, 0.00001);
    }

    [TestMethod]
    public void GetBoolean_BooleanValue_ReturnsBoolean()
    {
        // Arrange
        var code1 = "result = True";
        var code2 = "result = False";

        // Act
        using var pyValue1 = _executor.ExecuteAndCapture(code1);
        using var pyValue2 = _executor.ExecuteAndCapture(code2);

        // Assert
        Assert.IsTrue(pyValue1?.GetBoolean());
        Assert.IsFalse(pyValue2?.GetBoolean());
    }
}
