namespace DotNetPy.UnitTest;

[TestClass]
public sealed class PythonDiscoveryTests
{
    [TestMethod]
    public void FindPython_ShouldReturnPythonInfo()
    {
        // Act
        var pythonInfo = PythonDiscovery.FindPython();

        // Assert
        Assert.IsNotNull(pythonInfo, "Python should be found on the system");
        Assert.IsNotNull(pythonInfo.ExecutablePath);
        Assert.IsTrue(File.Exists(pythonInfo.ExecutablePath), $"Executable should exist: {pythonInfo.ExecutablePath}");
        Assert.IsNotNull(pythonInfo.LibraryPath);
        Assert.IsTrue(File.Exists(pythonInfo.LibraryPath), $"Library should exist: {pythonInfo.LibraryPath}");
        Assert.IsGreaterThanOrEqualTo(3, pythonInfo.Version.Major, "Python version should be 3.x or higher");

        Console.WriteLine($"Found: {pythonInfo}");
        Console.WriteLine($"Library: {pythonInfo.LibraryPath}");
    }

    [TestMethod]
    public void FindPython_WithMinimumVersion_ShouldReturnMatchingPython()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            MinimumVersion = new Version(3, 8)
        };

        // Act
        var pythonInfo = PythonDiscovery.FindPython(options);

        // Assert
        if (pythonInfo != null)
        {
            Assert.IsTrue(pythonInfo.Version >= new Version(3, 8),
        $"Python version {pythonInfo.Version} should be >= 3.8");
        }
    }

    [TestMethod]
    public void FindPython_WithArchitecture_ShouldReturnMatchingPython()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            RequiredArchitecture = Architecture.X64
        };

        // Act
        var pythonInfo = PythonDiscovery.FindPython(options);

        // Assert
        if (pythonInfo != null)
        {
            Assert.AreEqual(Architecture.X64, pythonInfo.Architecture,
                  "Python architecture should be X64");
        }
    }

    [TestMethod]
    public void FindAll_ShouldReturnMultiplePythons()
    {
        // Act
        var allPythons = PythonDiscovery.FindAll();

        // Assert
        Assert.IsNotNull(allPythons);
        Assert.IsNotEmpty(allPythons, "At least one Python should be found");

        Console.WriteLine($"Found {allPythons.Count} Python installation(s):");
        foreach (var python in allPythons)
        {
            Console.WriteLine($"  - {python}");
            Console.WriteLine($"    Library: {python.LibraryPath}");
        }
    }

    [TestMethod]
    public void FindAll_ShouldOrderByPriority()
    {
        // Act
        var allPythons = PythonDiscovery.FindAll();

        // Assert
        if (allPythons.Count > 1)
        {
            // Verify that PATH-based Python comes first if available
            var pathPython = allPythons.FirstOrDefault(p => p.Source == PythonSource.Path);
            if (pathPython != null)
            {
                Assert.AreSame(pathPython, allPythons[0],
                    "PATH-based Python should be first in the list");
            }
        }
    }

    [TestMethod]
    public void ClearCache_ShouldAllowRediscovery()
    {
        // Arrange
        var first = PythonDiscovery.FindPython();

        // Act
        PythonDiscovery.ClearCache();
        var second = PythonDiscovery.FindPython();

        // Assert
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.LibraryPath, second.LibraryPath);
    }

    [TestMethod]
    public void PythonInfo_ToString_ShouldReturnReadableFormat()
    {
        // Act
        var pythonInfo = PythonDiscovery.FindPython();

        // Assert
        if (pythonInfo != null)
        {
            var str = pythonInfo.ToString();
            Assert.Contains("Python", str, "ToString should contain 'Python'");
            Assert.Contains(pythonInfo.Version.ToString(), str, "ToString should contain version");
            Console.WriteLine($"ToString result: {str}");
        }
    }

    [TestMethod]
    public void Initialize_WithAutoDiscovery_ShouldSucceed()
    {
        // Arrange
        PythonDiscovery.ClearCache();

        // Act & Assert - Should not throw
        try
        {
            Python.Initialize();
            var instance = Python.GetInstance();
            Assert.IsNotNull(instance);

            // Verify it works
            var result = instance.Evaluate("1 + 1");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.GetInt32());
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found on system: {ex.Message}");
        }
    }

    [TestMethod]
    public void Initialize_WithOptions_ShouldUseFilteredPython()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            MinimumVersion = new Version(3, 8)
        };

        // Act & Assert
        try
        {
            Python.Initialize(options);
            var instance = Python.GetInstance();
            Assert.IsNotNull(instance);
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python 3.8+ not found on system: {ex.Message}");
        }
    }
}
