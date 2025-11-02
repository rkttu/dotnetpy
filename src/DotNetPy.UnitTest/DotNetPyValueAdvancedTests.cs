namespace DotNetPy.UnitTest;

[TestClass]
public sealed class DotNetPyValueAdvancedTests
{
    // DotNetPyValue의 고급 기능 테스트
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
    public void GetProperty_WithPath_ReturnsNestedValue()
    {
        // Arrange
        var code = @"
result = {
 'person': {
        'name': 'Alice',
    'age': 30,
        'address': {
  'city': 'Seoul',
   'country': 'Korea'
        }
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var nameProperty = value?.GetProperty("person.name");
        var cityProperty = value?.GetProperty("person.address.city");

        // Assert
        Assert.IsNotNull(nameProperty);
        Assert.AreEqual("Alice", nameProperty.Value.GetString());
        Assert.IsNotNull(cityProperty);
        Assert.AreEqual("Seoul", cityProperty.Value.GetString());
    }

    [TestMethod]
    public void GetProperty_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var code = @"
result = {
    'valid': 'value'
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var property = value?.GetProperty("invalid.path");

        // Assert
        Assert.IsNull(property);
    }

    [TestMethod]
    public void GetProperty_WithEmptyPath_ReturnsRootElement()
    {
        // Arrange
        var code = "result = 42";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var property = value?.GetProperty("");

        // Assert
        Assert.IsNotNull(property);
        Assert.AreEqual(42, property.Value.GetInt32());
    }

    [TestMethod]
    public void GetString_WithPath_ReturnsNestedString()
    {
        // Arrange
        var code = @"
result = {
    'data': {
'message': 'Hello, World!'
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var message = value?.GetString("data.message");

        // Assert
        Assert.AreEqual("Hello, World!", message);
    }

    [TestMethod]
    public void GetInt32_WithPath_ReturnsNestedInteger()
    {
        // Arrange
        var code = @"
result = {
  'stats': {
        'count': 100
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var count = value?.GetInt32("stats.count");

        // Assert
        Assert.AreEqual(100, count);
    }

    [TestMethod]
    public void GetDouble_WithPath_ReturnsNestedDouble()
    {
        // Arrange
        var code = @"
result = {
    'measurements': {
        'temperature': 36.5
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var temperature = value?.GetDouble("measurements.temperature");

        // Assert
        Assert.IsNotNull(temperature);
        Assert.AreEqual(36.5, temperature.Value, 0.01);
    }

    [TestMethod]
    public void GetBoolean_WithPath_ReturnsNestedBoolean()
    {
        // Arrange
        var code = @"
result = {
    'flags': {
        'isActive': True,
        'isDeleted': False
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var isActive = value?.GetBoolean("flags.isActive");
        var isDeleted = value?.GetBoolean("flags.isDeleted");

        // Assert
        Assert.IsTrue(isActive);
        Assert.IsFalse(isDeleted);
    }

    [TestMethod]
    public void GetProperty_WithNullValue_ReturnsNull()
    {
        // Arrange
        var code = @"
result = {
    'nullable': None
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var property = value?.GetProperty("nullable");

        // Assert
        Assert.IsNotNull(property); // Property exists
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, property.Value.ValueKind);
    }

    [TestMethod]
    public void ToList_EmptyList_ReturnsEmptyList()
    {
        // Arrange
        var code = "result = []";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var list = value?.ToList();

        // Assert
        Assert.IsNotNull(list);
        Assert.HasCount(0, list);
    }

    [TestMethod]
    public void ToList_NonArrayValue_ReturnsNull()
    {
        // Arrange
        var code = "result = {'key': 'value'}";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var list = value?.ToList();

        // Assert
        Assert.IsNull(list);
    }

    [TestMethod]
    public void ToDictionary_EmptyDict_ReturnsEmptyDictionary()
    {
        // Arrange
        var code = "result = {}";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var dict = value?.ToDictionary();

        // Assert
        Assert.IsNotNull(dict);
        Assert.IsEmpty(dict);
    }

    [TestMethod]
    public void ToDictionary_NonObjectValue_ReturnsNull()
    {
        // Arrange
        var code = "result = [1, 2, 3]";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var dict = value?.ToDictionary();

        // Assert
        Assert.IsNull(dict);
    }

    [TestMethod]
    public void ToDictionary_WithNullValues_HandlesNullsCorrectly()
    {
        // Arrange
        var code = @"
result = {
    'exists': 'value',
    'nullable': None
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var dict = value?.ToDictionary();

        // Assert
        Assert.IsNotNull(dict);
        Assert.HasCount(2, dict);
        Assert.AreEqual("value", dict["exists"]);
        Assert.IsNull(dict["nullable"]);
    }

    [TestMethod]
    public void ToList_WithMixedTypes_ConvertsAllTypes()
    {
        // Arrange
        var code = @"
result = [42, 'string', True, None, 3.14, {'nested': 'dict'}]
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var list = value?.ToList();

        // Assert
        Assert.IsNotNull(list);
        Assert.HasCount(6, list);
        Assert.AreEqual(42L, list[0]);
        Assert.AreEqual("string", list[1]);
        Assert.IsTrue((bool?)list[2]);
        Assert.IsNull(list[3]);
        Assert.AreEqual(3.14, (double)list[4]!, 0.01);
        Assert.IsInstanceOfType<Dictionary<string, object?>>(list[5]);
    }

    [TestMethod]
    public void GetProperty_DeepNesting_ReturnsCorrectValue()
    {
        // Arrange
        var code = @"
result = {
    'level1': {
      'level2': {
            'level3': {
       'level4': {
     'deepValue': 'found it!'
     }
      }
        }
    }
}
";

        // Act
        using var value = _executor.ExecuteAndCapture(code);
        var deepValue = value?.GetString("level1.level2.level3.level4.deepValue");

        // Assert
        Assert.AreEqual("found it!", deepValue);
    }
}
