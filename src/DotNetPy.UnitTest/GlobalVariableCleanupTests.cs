namespace DotNetPy.UnitTest;

[TestClass]
public sealed class GlobalVariableCleanupTests
{
    // 전역 변수 정리 테스트
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
    public void ClearGlobals_AfterExecute_RemovesUserVariables()
    {
        // Arrange
        _executor.Execute(@"
x = 10
y = 20
z = 30
");
        Assert.IsTrue(_executor.VariableExists("x"));

        // Act
        _executor.ClearGlobals();

        // Assert
        Assert.IsFalse(_executor.VariableExists("x"));
        Assert.IsFalse(_executor.VariableExists("y"));
        Assert.IsFalse(_executor.VariableExists("z"));
    }
}
