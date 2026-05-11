// Run all tests sequentially
// SequentialTestRunner runs all tests in a single test method.
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
[assembly: DoNotParallelize]

namespace DotNetPy.UnitTest;

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

/// <summary>
/// Primes the Python singleton from a single, well-defined entry point so that
/// individual test classes don't race on the first <see cref="Python.Initialize()"/>
/// call. Honours the DOTNETPY_TEST_PYTHON_LIB environment variable so the entire
/// suite can be aimed at a specific Python build (e.g. a free-threaded 3.13t /
/// 3.14t shared library) without per-class edits.
/// </summary>
[TestClass]
public static class GlobalTestSetup
{
    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext context)
    {
        var explicitLib = Environment.GetEnvironmentVariable("DOTNETPY_TEST_PYTHON_LIB");
        if (string.IsNullOrWhiteSpace(explicitLib) || !File.Exists(explicitLib))
        {
            // No override; per-class ClassInitialize logic handles auto-discovery.
            return;
        }

        // Build a synthetic PythonInfo so that consumers of Python.CurrentPythonInfo
        // / Python.IsFreeThreaded see the actual properties of the override library
        // instead of a null. Detection is purely from the file name pattern; we don't
        // execute Python at this point because the runtime hasn't been initialized yet.
        var pythonInfo = BuildPythonInfoFromLibrary(explicitLib);

        // Use the lower-level executor entry point so we can attach the PythonInfo we
        // just synthesised. Python.Initialize(string) does not take a PythonInfo and
        // would leave Python.CurrentPythonInfo == null.
        _ = DotNetPyExecutor.GetInstance(explicitLib, pythonInfo);

        // Realize the Lazy<DotNetPyExecutor> backing Python.GetInstance() so that any
        // subsequent per-class call to the parameterless Python.Initialize() short-
        // circuits via IsValueCreated instead of attempting auto-discovery (which
        // would resolve to a different DLL and trigger an InvalidOperationException).
        _ = Python.GetInstance();

        context.WriteLine($"DotNetPy initialized with explicit library: {explicitLib}");
        context.WriteLine($"Version: {pythonInfo.Version}");
        context.WriteLine($"IsFreeThreaded: {pythonInfo.IsFreeThreaded}");
    }

    private static PythonInfo BuildPythonInfoFromLibrary(string libraryPath)
    {
        string libDir = Path.GetDirectoryName(libraryPath) ?? string.Empty;
        string libName = Path.GetFileNameWithoutExtension(libraryPath);

        // Match python3, python3.13, python313, python313t, python3.13t, libpython3.13, etc.
        // Group 1: major digit(s); Group 2: optional minor (if separate); Group 3: free-threaded suffix.
        var match = Regex.Match(libName, @"python(\d+)(?:\.(\d+))?(t)?", RegexOptions.IgnoreCase);

        int major = 0, minor = 0;
        if (match.Success)
        {
            string majorPart = match.Groups[1].Value;
            if (match.Groups[2].Success)
            {
                major = int.Parse(majorPart);
                minor = int.Parse(match.Groups[2].Value);
            }
            else if (majorPart.Length >= 2)
            {
                // python313 -> major=3, minor=13
                major = int.Parse(majorPart[..1]);
                minor = int.Parse(majorPart[1..]);
            }
            else
            {
                major = int.Parse(majorPart);
            }
        }

        bool isFreeThreaded =
            (match.Success && match.Groups[3].Success) ||
            libraryPath.Contains("freethreaded", StringComparison.OrdinalIgnoreCase);

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => DotNetPy.Architecture.X64,
            System.Runtime.InteropServices.Architecture.X86 => DotNetPy.Architecture.X86,
            System.Runtime.InteropServices.Architecture.Arm64 => DotNetPy.Architecture.Arm64,
            System.Runtime.InteropServices.Architecture.Arm => DotNetPy.Architecture.Arm,
            _ => DotNetPy.Architecture.Unknown,
        };

        string exeCandidate = OperatingSystem.IsWindows()
            ? Path.Combine(libDir, isFreeThreaded ? $"python{major}.{minor}t.exe" : "python.exe")
            : Path.Combine(libDir, isFreeThreaded ? $"python{major}.{minor}t" : "python");
        string exePath = File.Exists(exeCandidate) ? exeCandidate : libraryPath;

        return new PythonInfo
        {
            ExecutablePath = exePath,
            LibraryPath = libraryPath,
            Version = new Version(major, minor),
            Architecture = arch,
            Source = PythonSource.UserProvided,
            HomeDirectory = libDir,
            BasePrefix = libDir,
            IsFreeThreaded = isFreeThreaded,
        };
    }
}
