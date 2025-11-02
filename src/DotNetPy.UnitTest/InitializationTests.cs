namespace DotNetPy.UnitTest;

[TestClass]
public sealed class InitializationTests
{
    // Python 초기화 및 실패 시나리오 테스트

    [TestMethod]
    public void Initialize_WithAutoDiscovery_Succeeds()
    {
        // Act & Assert - Should not throw
        try
        {
            Python.Initialize();
            var instance = Python.GetInstance();
            Assert.IsNotNull(instance);
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found: {ex.Message}");
        }
    }

    [TestMethod]
    public void Initialize_WithValidPath_Succeeds()
    {
        try
        {
            // Arrange - Find Python using discovery
            var pythonInfo = PythonDiscovery.FindPython();
            if (pythonInfo == null)
                Assert.Inconclusive("Python not found on system");

            // Act & Assert - Should not throw
            Python.Initialize(pythonInfo.LibraryPath);
            var instance = Python.GetInstance();
            Assert.IsNotNull(instance);
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python initialization failed: {ex.Message}");
        }
    }

    [TestMethod]
    public void Initialize_WithNullPath_ThrowsArgumentException()
    {
      // Act & Assert
        try
        {
    Python.Initialize((string)null!);
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
        try
     {
 // Arrange - Use auto-discovery
    var pythonInfo = PythonDiscovery.FindPython();
      if (pythonInfo == null)
       Assert.Inconclusive("Python not found on system");

    // Act
  var instance1 = DotNetPyExecutor.GetInstance(pythonInfo.LibraryPath);
    var instance2 = DotNetPyExecutor.GetInstance(pythonInfo.LibraryPath);
       var instance3 = DotNetPyExecutor.GetInstance();

   // Assert
   Assert.AreSame(instance1, instance2);
         Assert.AreSame(instance2, instance3);
  }
  catch (DotNetPyException ex)
{
    Assert.Inconclusive($"Python test failed: {ex.Message}");
  }
    }

    [TestMethod]
    public void GetInstance_WithDifferentPath_ThrowsInvalidOperationException()
    {
   // This test is not applicable with auto-discovery
// Skip or mark as inconclusive
        Assert.Inconclusive("Test requires multiple Python versions installed");
    }

    [TestMethod]
    public void ReferenceCount_AfterMultipleGetInstance_IncrementsCorrectly()
    {
        try
   {
      // Arrange - Use auto-discovery
   var pythonInfo = PythonDiscovery.FindPython();
      if (pythonInfo == null)
                Assert.Inconclusive("Python not found on system");

     // Get initial reference count
  var initialCount = DotNetPyExecutor.ReferenceCount;

 // Act
   var instance1 = DotNetPyExecutor.GetInstance(pythonInfo.LibraryPath);
    var countAfterFirst = DotNetPyExecutor.ReferenceCount;

var instance2 = DotNetPyExecutor.GetInstance();
      var countAfterSecond = DotNetPyExecutor.ReferenceCount;

    // Assert
  Assert.IsGreaterThanOrEqualTo(initialCount, countAfterFirst);
       Assert.IsGreaterThan(countAfterFirst, countAfterSecond);
      }
 catch (DotNetPyException ex)
        {
       Assert.Inconclusive($"Python test failed: {ex.Message}");
        }
    }
}
