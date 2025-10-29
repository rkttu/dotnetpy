namespace DotNetPy.UnitTest;

//[TestClass]
[Ignore("SequentialTestRunner로 통합됨")]
public sealed class CaptureManageVariableTests
{
    // 변수 캡처 및 관리 테스트
    private static DotNetPyExecutor _executor = default!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Python 라이브러리 경로 설정 (환경에 맞게 수정 필요)
        var pythonLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        // Python이 설치되어 있지 않으면 테스트 스킵
        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        Python.Initialize(pythonLibraryPath);
        _executor = Python.GetInstance();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        // 각 테스트 전에 전역 변수 정리
        _executor.ClearGlobals();
    }

    [TestMethod]
    public void CaptureVariable_ExistingVariable_ReturnsValue()
    {
        // Arrange
        _executor.Execute(@"
import math
pi = math.pi
");

        // Act
        var pi = _executor.CaptureVariable("pi");

        // Assert
        Assert.IsNotNull(pi);
        var piValue = pi.GetDouble();
        Assert.IsNotNull(piValue);
        Assert.AreEqual(Math.PI, piValue.Value, 0.0001);
    }

    [TestMethod]
    public void CaptureVariable_NonExistentVariable_ReturnsNull()
    {
        // Act
        var result = _executor.CaptureVariable("non_existent_var");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void CaptureVariables_MultipleVariables_ReturnsAll()
    {
        // Arrange
        _executor.Execute(@"
x = 10
y = 20
z = 30
");

        // Act
        using var results = _executor.CaptureVariables("x", "y", "z");

        // Assert
        Assert.IsNotNull(results);
        Assert.AreEqual(3, results.Count);
        Assert.AreEqual(10, results["x"]?.GetInt32());
        Assert.AreEqual(20, results["y"]?.GetInt32());
        Assert.AreEqual(30, results["z"]?.GetInt32());
    }

    [TestMethod]
    public void VariableExists_ExistingVariable_ReturnsTrue()
    {
        // Arrange
        _executor.Execute("test_var = 'exists'");

        // Act
        var exists = _executor.VariableExists("test_var");

        // Assert
        Assert.IsTrue(exists);
    }

    [TestMethod]
    public void VariableExists_NonExistentVariable_ReturnsFalse()
    {
        // Act
        var exists = _executor.VariableExists("non_existent");

        // Assert
        Assert.IsFalse(exists);
    }

    [TestMethod]
    public void GetExistingVariables_MixedVariables_ReturnsOnlyExisting()
    {
        // Arrange
        _executor.Execute(@"
apple = 'fruit'
banana = 'fruit'
carrot = 'vegetable'
");

        // Act
        var existing = _executor.GetExistingVariables("apple", "banana", "orange", "carrot", "potato");

        // Assert
        Assert.IsNotNull(existing);
        Assert.HasCount(3, existing);
        CollectionAssert.Contains(existing.ToList(), "apple");
        CollectionAssert.Contains(existing.ToList(), "banana");
        CollectionAssert.Contains(existing.ToList(), "carrot");
        CollectionAssert.DoesNotContain(existing.ToList(), "orange");
        CollectionAssert.DoesNotContain(existing.ToList(), "potato");
    }

    [TestMethod]
    public void DeleteVariable_ExistingVariable_DeletesAndReturnsTrue()
    {
        // Arrange
        _executor.Execute("temp_var = 'temporary'");
        Assert.IsTrue(_executor.VariableExists("temp_var"));

        // Act
        var deleted = _executor.DeleteVariable("temp_var");

        // Assert
        Assert.IsTrue(deleted);
        Assert.IsFalse(_executor.VariableExists("temp_var"));
    }

    [TestMethod]
    public void DeleteVariable_NonExistentVariable_ReturnsFalse()
    {
        // Act
        var deleted = _executor.DeleteVariable("non_existent");

        // Assert
        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public void DeleteVariables_MultipleVariables_DeletesOnlyExisting()
    {
        // Arrange
        _executor.Execute(@"
x = 10
y = 20
z = 30
");

        // Act
        var deleted = _executor.DeleteVariables("x", "y", "non_existent");

        // Assert
        Assert.IsNotNull(deleted);
        Assert.HasCount(2, deleted);
        CollectionAssert.Contains(deleted.ToList(), "x");
        CollectionAssert.Contains(deleted.ToList(), "y");
        Assert.IsFalse(_executor.VariableExists("x"));
        Assert.IsFalse(_executor.VariableExists("y"));
        Assert.IsTrue(_executor.VariableExists("z"));
    }
}
