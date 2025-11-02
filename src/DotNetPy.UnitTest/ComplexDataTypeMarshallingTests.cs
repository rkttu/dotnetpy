namespace DotNetPy.UnitTest;

[TestClass]
public sealed class ComplexDataTypeMarshallingTests
{
    // 복잡한 데이터 타입 마샬링 테스트
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
    public void MarshalDateTime_ToAndFromPython_PreservesValue()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var variables = new Dictionary<string, object?> { { "date_value", now } };

        // Act
        var code = @"
import json
result = json.loads(json.dumps(date_value))
";
        using var result = _executor.ExecuteAndCapture(code, variables);
        var dateString = result?.GetString();

        // Assert
        Assert.IsNotNull(dateString);
        var parsedDate = DateTime.Parse(dateString);
        Assert.AreEqual(now.Year, parsedDate.Year);
        Assert.AreEqual(now.Month, parsedDate.Month);
        Assert.AreEqual(now.Day, parsedDate.Day);
    }

    [TestMethod]
    public void MarshalDateTimeOffset_ToAndFromPython_PreservesValue()
    {
        // Arrange
        var dateTimeOffset = DateTimeOffset.UtcNow;
        var variables = new Dictionary<string, object?> { { "dto_value", dateTimeOffset } };

        // Act
        var code = @"
result = dto_value
";
        using var result = _executor.ExecuteAndCapture(code, variables);
        var dtoString = result?.GetString();

        // Assert
        Assert.IsNotNull(dtoString);
        var parsed = DateTimeOffset.Parse(dtoString);
        Assert.AreEqual(dateTimeOffset.Year, parsed.Year);
        Assert.AreEqual(dateTimeOffset.Month, parsed.Month);
        Assert.AreEqual(dateTimeOffset.Day, parsed.Day);
    }

    [TestMethod]
    public void MarshalGuid_ToAndFromPython_PreservesValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var variables = new Dictionary<string, object?> { { "guid_value", guid } };

        // Act
        var code = @"
result = guid_value
";
        using var result = _executor.ExecuteAndCapture(code, variables);
        var guidString = result?.GetString();

        // Assert
        Assert.IsNotNull(guidString);
        var parsedGuid = Guid.Parse(guidString);
        Assert.AreEqual(guid, parsedGuid);
    }

    [TestMethod]
    public void MarshalAnonymousType_ToAndFromPython_WorksCorrectly()
    {
        // Arrange
        var anonymousObject = new
        {
            Name = "John",
            Age = 30,
            IsActive = true
        };
        var variables = new Dictionary<string, object?> { { "person", anonymousObject } };

        // Act
        var code = @"
result = {
  'name': person['Name'],
  'age': person['Age'],
  'isActive': person['IsActive']
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual("John", result?.GetString("name"));
        Assert.AreEqual(30, result?.GetInt32("age"));
        Assert.IsTrue(result?.GetBoolean("isActive"));
    }

    [TestMethod]
    public void MarshalComplexObject_WithProperties_WorksCorrectly()
    {
        // Arrange
        var person = new Person
        {
            FirstName = "Alice",
            LastName = "Smith",
            Age = 25,
            Email = "alice@example.com"
        };
        var variables = new Dictionary<string, object?> { { "person", person } };

        // Act
        var code = @"
result = {
    'fullName': person['FirstName'] + ' ' + person['LastName'],
    'age': person['Age'],
    'email': person['Email']
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual("Alice Smith", result?.GetString("fullName"));
        Assert.AreEqual(25, result?.GetInt32("age"));
        Assert.AreEqual("alice@example.com", result?.GetString("email"));
    }

    [TestMethod]
    public void MarshalNestedObjects_PreservesStructure()
    {
        // Arrange
        var company = new Company
        {
            Name = "TechCorp",
            Location = new Address
            {
                Street = "123 Tech Street",
                City = "Seoul",
                Country = "Korea"
            }
        };
        var variables = new Dictionary<string, object?> { { "company", company } };

        // Act
        var code = @"
result = {
  'companyName': company['Name'],
 'city': company['Location']['City'],
'address': company['Location']['Street'] + ', ' + company['Location']['City']
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual("TechCorp", result?.GetString("companyName"));
        Assert.AreEqual("Seoul", result?.GetString("city"));
        Assert.AreEqual("123 Tech Street, Seoul", result?.GetString("address"));
    }

    [TestMethod]
    public void MarshalListOfComplexObjects_WorksCorrectly()
    {
        // Arrange
        var people = new List<Person>
        {
   new() { FirstName = "Alice", LastName = "Smith", Age = 25 },
    new() { FirstName = "Bob", LastName = "Jones", Age = 30 },
       new() { FirstName = "Charlie", LastName = "Brown", Age = 35 }
     };
        var variables = new Dictionary<string, object?> { { "people", people } };

        // Act
        var code = @"
result = {
    'count': len(people),
  'firstPerson': people[0]['FirstName'],
'totalAge': sum(p['Age'] for p in people)
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual(3, result?.GetInt32("count"));
        Assert.AreEqual("Alice", result?.GetString("firstPerson"));
        Assert.AreEqual(90, result?.GetInt32("totalAge"));
    }

    [TestMethod]
    public void MarshalDictionaryOfMixedTypes_WorksCorrectly()
    {
        // Arrange
        var mixedDict = new Dictionary<string, object?>
        {
 { "stringValue", "text" },
            { "intValue", 42 },
            { "doubleValue", 3.14 },
       { "boolValue", true },
  { "nullValue", null },
    { "dateValue", DateTime.UtcNow },
   { "guidValue", Guid.NewGuid() }
        };
        var variables = new Dictionary<string, object?> { { "mixed", mixedDict } };

        // Act
        var code = @"
result = {
    'hasString': 'stringValue' in mixed,
'hasInt': 'intValue' in mixed,
    'hasNull': 'nullValue' in mixed,
    'stringValue': mixed['stringValue'],
    'intValue': mixed['intValue']
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.IsTrue(result?.GetBoolean("hasString"));
        Assert.IsTrue(result?.GetBoolean("hasInt"));
        Assert.IsTrue(result?.GetBoolean("hasNull"));
        Assert.AreEqual("text", result?.GetString("stringValue"));
        Assert.AreEqual(42, result?.GetInt32("intValue"));
    }

    [TestMethod]
    public void MarshalNumericTypes_PreservesValues()
    {
        // Arrange
        var numbers = new Dictionary<string, object?>
  {
   { "byte_val", (byte)255 },
            { "sbyte_val", (sbyte)-128 },
       { "short_val", (short)-32768 },
   { "ushort_val", (ushort)65535 },
     { "int_val", -2147483648 },
        { "uint_val", 4294967295U },
            { "long_val", -9223372036854775808L },
            { "ulong_val", 18446744073709551615UL },
  { "float_val", 3.14f },
   { "double_val", 2.718281828 },
            { "decimal_val", 123.456m }
        };
        var variables = new Dictionary<string, object?> { { "numbers", numbers } };

        // Act
        var code = @"
result = {
    'byte_val': numbers['byte_val'],
    'int_val': numbers['int_val'],
    'float_val': numbers['float_val'],
    'double_val': numbers['double_val']
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual(255, result?.GetInt32("byte_val"));
        Assert.AreEqual(-2147483648, result?.GetInt32("int_val"));
        Assert.IsNotNull(result?.GetDouble("float_val"));
        Assert.IsNotNull(result?.GetDouble("double_val"));
    }

    [TestMethod]
    public void MarshalEmptyCollections_WorksCorrectly()
    {
        // Arrange
        var variables = new Dictionary<string, object?>
   {
       { "empty_list", new List<object>() },
            { "empty_dict", new Dictionary<string, object>() },
     { "empty_array", Array.Empty<int>() }
   };

        // Act
        var code = @"
result = {
  'list_len': len(empty_list),
    'dict_len': len(empty_dict),
    'array_len': len(empty_array)
}
";
        using var result = _executor.ExecuteAndCapture(code, variables);

        // Assert
        Assert.AreEqual(0, result?.GetInt32("list_len"));
        Assert.AreEqual(0, result?.GetInt32("dict_len"));
        Assert.AreEqual(0, result?.GetInt32("array_len"));
    }

    // Helper classes for testing
    private class Person
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Email { get; set; }
    }

    private class Address
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    private class Company
    {
        public string Name { get; set; } = string.Empty;
        public Address? Location { get; set; }
    }
}
