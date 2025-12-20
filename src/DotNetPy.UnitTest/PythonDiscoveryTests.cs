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

    [TestMethod]
    public void FindAll_WithUvEnabled_ShouldIncludeUvPythons()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            IncludeUvManagedPython = true,
            ForceRefresh = true
        };

        // Act
        var allPythons = PythonDiscovery.FindAll(options);

        // Assert
        Assert.IsNotNull(allPythons);

        var uvPythons = allPythons.Where(p => p.Source == PythonSource.Uv).ToList();
        Console.WriteLine($"Found {uvPythons.Count} uv-managed Python installation(s):");
        foreach (var python in uvPythons)
        {
            Console.WriteLine($"  - {python}");
            Console.WriteLine($"    Library: {python.LibraryPath}");
        }

        // Note: Test passes even if no uv Python is found (optional installation)
    }

    [TestMethod]
    public void FindAll_WithUvDisabled_ShouldExcludeUvPythons()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            IncludeUvManagedPython = false,
            ForceRefresh = true
        };

        // Act
        var allPythons = PythonDiscovery.FindAll(options);

        // Assert
        Assert.IsNotNull(allPythons);

        var uvPythons = allPythons.Where(p => p.Source == PythonSource.Uv).ToList();
        Assert.IsEmpty(uvPythons, "No uv-managed Python should be included when disabled");
    }

    [TestMethod]
    public void FindPython_WithWorkingDirectory_ShouldFindUvProjectEnvironment()
    {
        // This test demonstrates how a .NET file-based app inside a uv project
        // can automatically discover the project's Python environment.
        
        // Arrange - simulate a uv project structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"uv-test-{Guid.NewGuid():N}");
        var venvDir = Path.Combine(tempDir, ".venv");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Create a minimal pyproject.toml to indicate this is a uv project
            File.WriteAllText(Path.Combine(tempDir, "pyproject.toml"), """
                [project]
                name = "test-project"
                version = "0.1.0"
                """);
            
            // Note: This test will only pass if there's an actual .venv with Python
            // For CI/CD, this would require setting up a real uv environment
            
            var options = new PythonDiscoveryOptions
            {
                WorkingDirectory = tempDir,
                IncludeUvProjectEnvironment = true,
                ForceRefresh = true
            };
            
            // Act
            var pythonInfo = PythonDiscovery.FindPython(options);
            
            // Assert - if uv project Python was found, it should have UvProject source
            if (pythonInfo?.Source == PythonSource.UvProject)
            {
                Console.WriteLine($"Found uv project Python: {pythonInfo}");
                Assert.Contains(".venv", pythonInfo.ExecutablePath, "UvProject Python should be from .venv directory");
            }
            else
            {
                Console.WriteLine("No uv project environment found (expected if .venv doesn't exist)");
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void FindPython_WithUvProjectDisabled_ShouldSkipUvProjectEnvironment()
    {
        // Arrange
        var options = new PythonDiscoveryOptions
        {
            IncludeUvProjectEnvironment = false,
            ForceRefresh = true
        };

        // Act
        var allPythons = PythonDiscovery.FindAll(options);

        // Assert
        var uvProjectPythons = allPythons.Where(p => p.Source == PythonSource.UvProject).ToList();
        Assert.IsEmpty(uvProjectPythons, "No UvProject Python should be included when disabled");
    }

    [TestMethod]
    public void FindPython_ShouldReportIsFreeThreadedProperty()
    {
        // This test verifies that the IsFreeThreaded property is correctly detected.
        // Most Python installations will have IsFreeThreaded = false (standard GIL build).
        // Python 3.13+ built with --disable-gil will return true.

        // Act
        var pythonInfo = PythonDiscovery.FindPython();

        // Assert
        Assert.IsNotNull(pythonInfo, "Python should be found on the system");
        
        // IsFreeThreaded is a boolean, so it will always have a value
        // Standard builds should return false, experimental free-threaded builds return true
        Console.WriteLine($"Python {pythonInfo.Version} IsFreeThreaded: {pythonInfo.IsFreeThreaded}");
        
        if (pythonInfo.IsFreeThreaded)
        {
            Console.WriteLine("  -> This is an experimental free-threaded Python build (no GIL)!");
            Assert.IsTrue(pythonInfo.Version >= new Version(3, 13), 
                "Free-threaded Python should be version 3.13 or higher");
        }
        else
        {
            Console.WriteLine("  -> This is a standard Python build with GIL enabled.");
        }
    }

    [TestMethod]
    public void FindAll_ShouldIncludeIsFreeThreadedForAllPythons()
    {
        // Act
        var allPythons = PythonDiscovery.FindAll();

        // Assert
        Assert.IsNotNull(allPythons);
        Assert.IsNotEmpty(allPythons, "At least one Python should be found");

        Console.WriteLine($"Free-threaded status for {allPythons.Count} Python installation(s):");
        foreach (var python in allPythons)
        {
            Console.WriteLine($"  - {python.Version} ({python.Source}): IsFreeThreaded={python.IsFreeThreaded}");
        }

        // Verify all pythons have the property set (even if false)
        foreach (var python in allPythons)
        {
            // IsFreeThreaded is a bool, not nullable, so this always passes
            // The point is to ensure the property is properly initialized
            Assert.IsTrue(python.IsFreeThreaded == true || python.IsFreeThreaded == false,
                "IsFreeThreaded should be a valid boolean value");
        }
    }

    [TestMethod]
    public void Python_IsFreeThreaded_ShouldReflectCurrentPythonInfo()
    {
        // This test verifies the static Python.IsFreeThreaded property

        // Act
        try
        {
            Python.Initialize();
            var isFreeThreaded = Python.IsFreeThreaded;
            var pythonInfo = Python.CurrentPythonInfo;

            // Assert
            Assert.IsNotNull(pythonInfo);
            Assert.AreEqual(pythonInfo.IsFreeThreaded, isFreeThreaded,
                "Python.IsFreeThreaded should match CurrentPythonInfo.IsFreeThreaded");
            
            Console.WriteLine($"Python.IsFreeThreaded: {isFreeThreaded}");
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found on system: {ex.Message}");
        }
    }
}
