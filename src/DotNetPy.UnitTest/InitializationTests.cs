namespace DotNetPy.UnitTest;

[TestClass]
public sealed class InitializationTests
{
    // Python 초기화 및 실패 시나리오 테스트

    [TestMethod]
    public void Initialize_WithValidPath_Succeeds()
    {
        // Arrange
        var pythonLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        // Act & Assert - Should not throw
        Python.Initialize(pythonLibraryPath);
        var instance = Python.GetInstance();
        Assert.IsNotNull(instance);
    }

    [TestMethod]
    public void Initialize_WithNullPath_ThrowsArgumentException()
    {
        // Act & Assert
        try
        {
            Python.Initialize(null!);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void Initialize_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        try
        {
            Python.Initialize(string.Empty);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void Initialize_WithWhitespacePath_ThrowsArgumentException()
    {
        // Act & Assert
        try
        {
            Python.Initialize("   ");
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void GetInstance_WithNonExistentPath_ThrowsDotNetPyException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            "non_existent_python_library.dll");

        // Act & Assert
        try
        {
            DotNetPyExecutor.GetInstance(nonExistentPath);
            Assert.Fail("Expected DotNetPyException was not thrown");
        }
        catch (DotNetPyException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void GetInstance_WithInvalidLibraryPath_ThrowsDotNetPyException()
    {
        // Arrange - Create a temporary invalid file
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act & Assert
            try
            {
                DotNetPyExecutor.GetInstance(tempFile);
                Assert.Fail("Expected DotNetPyException was not thrown");
            }
            catch (DotNetPyException)
            {
                // Expected exception
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void GetInstance_MultipleCalls_ReturnsSameInstance()
    {
        // Arrange
        var pythonLibraryPath = Path.Combine(
       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python313", "python313.dll");

        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        // Act
        var instance1 = DotNetPyExecutor.GetInstance(pythonLibraryPath);
        var instance2 = DotNetPyExecutor.GetInstance(pythonLibraryPath);
        var instance3 = DotNetPyExecutor.GetInstance();

        // Assert
        Assert.AreSame(instance1, instance2);
        Assert.AreSame(instance2, instance3);
    }

    [TestMethod]
    public void GetInstance_WithDifferentPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var pythonLibraryPath1 = Path.Combine(
             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
           "Programs", "Python", "Python313", "python313.dll");

        var pythonLibraryPath2 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python312", "python312.dll");

        if (!File.Exists(pythonLibraryPath1))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath1}");

        // Initialize with first path
        DotNetPyExecutor.GetInstance(pythonLibraryPath1);

        // Act & Assert - Try to initialize with different path
        try
        {
            DotNetPyExecutor.GetInstance(pythonLibraryPath2);
            Assert.Fail("Expected InvalidOperationException was not thrown");
        }
        catch (DotNetPyException)
        {
            // Expected exception
        }
    }

    [TestMethod]
    public void ReferenceCount_AfterMultipleGetInstance_IncrementsCorrectly()
    {
        // Arrange
        var pythonLibraryPath = Path.Combine(
  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
   "Programs", "Python", "Python313", "python313.dll");

        if (!File.Exists(pythonLibraryPath))
            Assert.Inconclusive($"Python library not found at {pythonLibraryPath}");

        // Get initial reference count
        var initialCount = DotNetPyExecutor.ReferenceCount;

        // Act
        var instance1 = DotNetPyExecutor.GetInstance(pythonLibraryPath);
        var countAfterFirst = DotNetPyExecutor.ReferenceCount;

        var instance2 = DotNetPyExecutor.GetInstance();
        var countAfterSecond = DotNetPyExecutor.ReferenceCount;

        // Assert
        Assert.IsGreaterThanOrEqualTo(initialCount, countAfterFirst);
        Assert.IsGreaterThan(countAfterFirst, countAfterSecond);
    }
}
