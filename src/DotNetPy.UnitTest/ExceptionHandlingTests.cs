namespace DotNetPy.UnitTest;

//[TestClass]
[Ignore("SequentialTestRunner로 통합됨")]
public sealed class ExceptionHandlingTests
{
    // 예외 처리 테스트
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
    public void Execute_PythonRuntimeError_ThrowsException()
    {
        // Act & Assert
        try
        {
            _executor.Execute("result = 1 / 0");
            Assert.Fail("Expected DotNetPyException was not thrown");
        }
        catch (DotNetPyException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void Execute_UndefinedVariable_ThrowsException()
    {
        // Act & Assert
        try
        {
            _executor.Execute("result = undefined_variable");
            Assert.Fail("Expected DotNetPyException was not thrown");
        }
        catch (DotNetPyException)
        {
            // Expected exception
        }
    }
}
