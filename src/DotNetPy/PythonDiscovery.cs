using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotNetPy;

/// <summary>
/// Provides automatic discovery of Python installations.
/// </summary>
public static partial class PythonDiscovery
{
    private static PythonInfo? _cachedDefault;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan _executionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Finds the best available Python installation.
    /// </summary>
    /// <param name="options">Optional discovery options.</param>
    /// <returns>Information about the discovered Python installation, or null if not found.</returns>
    public static PythonInfo? FindPython(PythonDiscoveryOptions? options = null)
    {
        lock (_cacheLock)
        {
            if (_cachedDefault != null && options?.ForceRefresh != true)
                return _cachedDefault;

            // Strategy 0: Find via uv project local environment (.venv) - highest priority
            if (options?.IncludeUvProjectEnvironment != false)
            {
                _cachedDefault = FindViaUvProject(options);
                if (_cachedDefault != null)
                    return _cachedDefault;
            }

            // Strategy 1: Find via 'python' or 'python3' command (most reliable)
            _cachedDefault = FindViaPythonCommand(options);
            if (_cachedDefault != null)
                return _cachedDefault;

            // Strategy 2: Find via Python Launcher (Windows only)
            if (OperatingSystem.IsWindows())
            {
                _cachedDefault = FindViaPyLauncher(options);
                if (_cachedDefault != null)
                    return _cachedDefault;
            }

            // Strategy 3: Find via uv managed Python installations (global)
            if (options?.IncludeUvManagedPython != false)
            {
                _cachedDefault = FindViaUv(options);
                if (_cachedDefault != null)
                    return _cachedDefault;
            }

            // Strategy 4: Find via standard paths
            _cachedDefault = FindViaStandardPaths(options);
            return _cachedDefault;
        }
    }

    /// <summary>
    /// Finds all available Python installations.
    /// </summary>
    /// <param name="options">Optional discovery options.</param>
    /// <returns>A list of all discovered Python installations.</returns>
    public static IReadOnlyList<PythonInfo> FindAll(PythonDiscoveryOptions? options = null)
    {
        var results = new List<PythonInfo>();

        // 0. From uv project local environment (.venv)
        if (options?.IncludeUvProjectEnvironment != false)
        {
            var fromUvProject = FindViaUvProject(options);
            if (fromUvProject != null)
                results.Add(fromUvProject);
        }

        // 1. From python/python3 command
        var fromCommand = FindViaPythonCommand(options);
        if (fromCommand != null)
            results.Add(fromCommand);

        // 2. From Python Launcher (Windows)
        if (OperatingSystem.IsWindows())
        {
            var fromLauncher = FindAllViaPyLauncher(options);
            results.AddRange(fromLauncher);
        }

        // 3. From uv managed Python installations (global)
        if (options?.IncludeUvManagedPython != false)
        {
            var fromUv = FindAllViaUv(options);
            results.AddRange(fromUv);
        }

        // 4. From standard paths
        var fromPaths = FindAllViaStandardPaths(options);
        results.AddRange(fromPaths);

        // Remove duplicates based on LibraryPath
        var unique = results
   .GroupBy(p => p.LibraryPath, StringComparer.OrdinalIgnoreCase)
  .Select(g => g.OrderByDescending(p => p.Priority).First())
      .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.Version)
    .ThenByDescending(p => (int)p.Architecture)
  .ToList();

        return unique;
    }

    /// <summary>
    /// Clears the cached discovery result.
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedDefault = null;
        }
    }

    private static PythonInfo? FindViaUvProject(PythonDiscoveryOptions? options)
    {
        var startDir = options?.WorkingDirectory ?? Directory.GetCurrentDirectory();

        // Walk up the directory tree looking for uv project indicators
        var currentDir = startDir;
        while (!string.IsNullOrEmpty(currentDir))
        {
            try
            {
                // Check for .venv directory (created by 'uv sync' or 'uv venv')
                var venvDir = Path.Combine(currentDir, ".venv");
                if (Directory.Exists(venvDir))
                {
                    // Verify this is a uv project by checking for pyproject.toml or uv.lock
                    var hasUvProjectFile = File.Exists(Path.Combine(currentDir, "pyproject.toml")) ||
                                           File.Exists(Path.Combine(currentDir, "uv.lock"));

                    if (hasUvProjectFile)
                    {
                        var pythonExe = GetVenvPythonExecutable(venvDir);
                        if (!string.IsNullOrEmpty(pythonExe) && File.Exists(pythonExe))
                        {
                            var pythonInfo = QueryPythonExecutable(pythonExe, PythonSource.UvProject);
                            if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                            {
                                pythonInfo.Priority = CalculatePriority(pythonInfo);
                                return pythonInfo;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }

            // Move to parent directory
            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == currentDir)
                break;
            currentDir = parentDir;
        }

        return null;
    }

    private static string? GetVenvPythonExecutable(string venvDir)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: .venv/Scripts/python.exe
            var scriptsPath = Path.Combine(venvDir, "Scripts", "python.exe");
            if (File.Exists(scriptsPath))
                return scriptsPath;
        }
        else
        {
            // Unix: .venv/bin/python3 or .venv/bin/python
            var binPath = Path.Combine(venvDir, "bin", "python3");
            if (File.Exists(binPath))
                return binPath;

            binPath = Path.Combine(venvDir, "bin", "python");
            if (File.Exists(binPath))
                return binPath;
        }

        return null;
    }

    private static PythonInfo? FindViaPythonCommand(PythonDiscoveryOptions? options)
    {
        // Try 'python' first (Windows/Linux/macOS), then 'python3' (Linux/macOS)
        var commands = OperatingSystem.IsWindows()
            ? new[] { "python" }
     : new[] { "python3", "python" };

        foreach (var command in commands)
        {
            try
            {
                var pythonInfo = QueryPythonExecutable(command, PythonSource.Path);
                if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                {
                    pythonInfo.Priority = CalculatePriority(pythonInfo);
                    return pythonInfo;
                }
            }
            catch
            {
                // Ignore and try next command
            }
        }

        return null;
    }

    private static PythonInfo? FindViaPyLauncher(PythonDiscoveryOptions? options)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            // py.exe -0 lists all installed Python versions
            // The default one is usually the first or marked with *
            var output = ExecuteCommand("py", "-c \"import sys; print(sys.executable)\"");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var exePath = output.Trim();
                if (File.Exists(exePath))
                {
                    var pythonInfo = QueryPythonExecutable(exePath, PythonSource.PyLauncher);
                    if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                    {
                        pythonInfo.Priority = CalculatePriority(pythonInfo);
                        return pythonInfo;
                    }
                }
            }
        }
        catch
        {
            // Python Launcher not available
        }

        return null;
    }

    private static List<PythonInfo> FindAllViaPyLauncher(PythonDiscoveryOptions? options)
    {
        var results = new List<PythonInfo>();

        if (!OperatingSystem.IsWindows())
            return results;

        try
        {
            // py.exe -0p lists all Python installations with paths
            var output = ExecuteCommand("py", "-0p");
            if (string.IsNullOrWhiteSpace(output))
                return results;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    // Format: " -3.13-64C:\Users\...\Python\Python313\python.exe"
                    // or:     " *-3.13-64   C:\Users\...\Python\Python313\python.exe"
                    var match = PyLauncherLineRegex().Match(line);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var exePath = match.Groups[1].Value.Trim();
                        if (File.Exists(exePath))
                        {
                            var pythonInfo = QueryPythonExecutable(exePath, PythonSource.PyLauncher);
                            if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                            {
                                pythonInfo.Priority = CalculatePriority(pythonInfo);
                                results.Add(pythonInfo);
                            }
                        }
                    }
                }
                catch
                {
                    // Skip invalid lines
                }
            }
        }
        catch
        {
            // Python Launcher not available
        }

        return results;
    }

    private static PythonInfo? FindViaUv(PythonDiscoveryOptions? options)
    {
        var all = FindAllViaUv(options);
        return all.FirstOrDefault();
    }

    private static List<PythonInfo> FindAllViaUv(PythonDiscoveryOptions? options)
    {
        var results = new List<PythonInfo>();
        var uvPythonPaths = GetUvPythonPaths();

        foreach (var uvPath in uvPythonPaths)
        {
            try
            {
                if (!Directory.Exists(uvPath))
                    continue;

                // uv installs Python in subdirectories like:
                // Windows: cpython-3.12.0-windows-x86_64-none/python.exe
                // Linux/macOS: cpython-3.12.0-linux-x86_64-gnu/bin/python3
                var versionDirs = Directory.GetDirectories(uvPath);

                foreach (var versionDir in versionDirs)
                {
                    try
                    {
                        string? pythonExe = null;

                        if (OperatingSystem.IsWindows())
                        {
                            pythonExe = Path.Combine(versionDir, "python.exe");
                            if (!File.Exists(pythonExe))
                            {
                                // Also check in install subdirectory
                                pythonExe = Path.Combine(versionDir, "install", "python.exe");
                            }
                        }
                        else
                        {
                            pythonExe = Path.Combine(versionDir, "bin", "python3");
                            if (!File.Exists(pythonExe))
                            {
                                pythonExe = Path.Combine(versionDir, "bin", "python");
                            }
                            if (!File.Exists(pythonExe))
                            {
                                // Also check in install subdirectory
                                pythonExe = Path.Combine(versionDir, "install", "bin", "python3");
                            }
                        }

                        if (!string.IsNullOrEmpty(pythonExe) && File.Exists(pythonExe))
                        {
                            var pythonInfo = QueryPythonExecutable(pythonExe, PythonSource.Uv);
                            if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                            {
                                pythonInfo.Priority = CalculatePriority(pythonInfo);
                                results.Add(pythonInfo);
                            }
                        }
                    }
                    catch
                    {
                        // Skip invalid directories
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return results;
    }

    private static List<string> GetUvPythonPaths()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            // Windows: %LOCALAPPDATA%\uv\python or %APPDATA%\uv\python
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrEmpty(localAppData))
                paths.Add(Path.Combine(localAppData, "uv", "python"));

            if (!string.IsNullOrEmpty(appData))
                paths.Add(Path.Combine(appData, "uv", "python"));

            // Also check UV_PYTHON_INSTALL_DIR environment variable
            var uvInstallDir = Environment.GetEnvironmentVariable("UV_PYTHON_INSTALL_DIR");
            if (!string.IsNullOrEmpty(uvInstallDir))
                paths.Insert(0, uvInstallDir);
        }
        else
        {
            // Linux/macOS: $XDG_DATA_HOME/uv/python or ~/.local/share/uv/python
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(xdgDataHome))
                paths.Add(Path.Combine(xdgDataHome, "uv", "python"));

            if (!string.IsNullOrEmpty(homeDir))
                paths.Add(Path.Combine(homeDir, ".local", "share", "uv", "python"));

            // Also check UV_PYTHON_INSTALL_DIR environment variable
            var uvInstallDir = Environment.GetEnvironmentVariable("UV_PYTHON_INSTALL_DIR");
            if (!string.IsNullOrEmpty(uvInstallDir))
                paths.Insert(0, uvInstallDir);
        }

        return paths;
    }

    private static PythonInfo? FindViaStandardPaths(PythonDiscoveryOptions? options)
    {
        var all = FindAllViaStandardPaths(options);
        return all.FirstOrDefault();
    }

    private static List<PythonInfo> FindAllViaStandardPaths(PythonDiscoveryOptions? options)
    {
        var results = new List<PythonInfo>();
        var searchPaths = GetStandardSearchPaths();

        foreach (var searchPath in searchPaths)
        {
            try
            {
                if (!Directory.Exists(searchPath))
                    continue;

                // Look for python.exe (Windows) or python3/python (Unix)
                var patterns = OperatingSystem.IsWindows()
                   ? new[] { "python.exe" }
               : new[] { "python3", "python" };

                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(searchPath, pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            var pythonInfo = QueryPythonExecutable(file, PythonSource.StandardPath);
                            if (pythonInfo != null && MatchesOptions(pythonInfo, options))
                            {
                                pythonInfo.Priority = CalculatePriority(pythonInfo);
                                results.Add(pythonInfo);
                            }
                        }
                        catch
                        {
                            // Skip invalid executables
                        }
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return results;
    }

    private static List<string> GetStandardSearchPaths()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            // Windows standard paths
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            paths.Add(Path.Combine(localAppData, "Programs", "Python"));
            paths.Add(Path.Combine(programFiles, "Python"));
            if (!string.IsNullOrEmpty(programFilesX86))
                paths.Add(Path.Combine(programFilesX86, "Python"));

            // Microsoft Store Python
            paths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Unix standard paths
            paths.Add("/usr/bin");
            paths.Add("/usr/local/bin");
            paths.Add("/opt/python");
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pyenv", "versions"));
        }

        return paths;
    }

    private static PythonInfo? QueryPythonExecutable(string executablePath, PythonSource source)
    {
        try
        {
            // Get version
            var versionOutput = ExecuteCommand(executablePath, "--version");
            if (string.IsNullOrWhiteSpace(versionOutput))
                return null;

            var version = ParsePythonVersion(versionOutput);
            if (version == null)
                return null;

            // Get library path and other info using Python itself
            var script = @"
import sys
import sysconfig
import platform
print(sys.executable)
print(sysconfig.get_config_var('LIBDIR') or '')
print(sysconfig.get_config_var('LDLIBRARY') or '')
print(platform.machine())
print(sys.prefix)
";

            var infoOutput = ExecuteCommand(executablePath, $"-c \"{script.Replace("\"", "\\\"")}\"");
            if (string.IsNullOrWhiteSpace(infoOutput))
                return null;

            var lines = infoOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                   .Select(l => l.Trim())
                   .ToArray();

            if (lines.Length < 5)
                return null;

            var actualExePath = lines[0];
            var libDir = lines[1];
            var libName = lines[2];
            var machine = lines[3];
            var prefix = lines[4];

            // Determine library path
            var libraryPath = DetermineLibraryPath(actualExePath, libDir, libName, version);
            if (string.IsNullOrEmpty(libraryPath) || !File.Exists(libraryPath))
                return null;

            // Determine architecture
            var architecture = ParseArchitecture(machine);

            return new PythonInfo
            {
                ExecutablePath = actualExePath,
                LibraryPath = libraryPath,
                Version = version,
                Architecture = architecture,
                Source = source,
                HomeDirectory = prefix
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? DetermineLibraryPath(string exePath, string libDir, string libName, Version version)
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows, the DLL is usually in the same directory as python.exe
            var exeDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exeDir))
                return null;

            // Try python3XX.dll or pythonXX.dll
            var dllPatterns = new[]
          {
 $"python{version.Major}{version.Minor}.dll",
        $"python{version.Major}.dll",
        libName
            };

            foreach (var pattern in dllPatterns)
            {
                if (string.IsNullOrEmpty(pattern))
                    continue;

                var dllPath = Path.Combine(exeDir, pattern);
                if (File.Exists(dllPath))
                    return dllPath;
            }

            return null;
        }
        else
        {
            // On Unix, check libDir first
            if (!string.IsNullOrEmpty(libDir) && !string.IsNullOrEmpty(libName))
            {
                var soPath = Path.Combine(libDir, libName);
                if (File.Exists(soPath))
                    return soPath;
            }

            // Try common patterns
            var soPatterns = new[]
      {
      $"libpython{version.Major}.{version.Minor}.so",
         $"libpython{version.Major}.{version.Minor}m.so",
                $"libpython{version.Major}.so",
                libName
  };

            var searchDirs = new[] { libDir, "/usr/lib", "/usr/local/lib", "/usr/lib/x86_64-linux-gnu" }
                .Where(d => !string.IsNullOrEmpty(d));

            foreach (var dir in searchDirs)
            {
                foreach (var pattern in soPatterns)
                {
                    if (string.IsNullOrEmpty(pattern))
                        continue;

                    var soPath = Path.Combine(dir, pattern);
                    if (File.Exists(soPath))
                        return soPath;
                }
            }

            return null;
        }
    }

    private static Version? ParsePythonVersion(string versionOutput)
    {
        // Format: "Python 3.13.0" or "Python 3.13.0rc1"
        var match = PythonVersionRegex().Match(versionOutput);
        if (match.Success && Version.TryParse(match.Groups[1].Value, out var version))
            return version;

        return null;
    }

    private static Architecture ParseArchitecture(string machine)
    {
        return machine.ToLowerInvariant() switch
        {
            var m when m.Contains("x86_64") || m.Contains("amd64") => Architecture.X64,
            var m when m.Contains("i386") || m.Contains("i686") => Architecture.X86,
            var m when m.Contains("aarch64") || m.Contains("arm64") => Architecture.Arm64,
            var m when m.Contains("arm") => Architecture.Arm,
            _ => Architecture.Unknown
        };
    }

    private static bool MatchesOptions(PythonInfo python, PythonDiscoveryOptions? options)
    {
        if (options == null)
            return true;

        if (options.MinimumVersion != null && python.Version < options.MinimumVersion)
            return false;

        if (options.MaximumVersion != null && python.Version > options.MaximumVersion)
            return false;

        if (options.RequiredArchitecture.HasValue && python.Architecture != options.RequiredArchitecture.Value)
            return false;

        return true;
    }

    private static int CalculatePriority(PythonInfo python)
    {
        int priority = (int)python.Source;

        // Prefer newer versions
        priority += python.Version.Major * 10 + python.Version.Minor;

        // Prefer x64 over x86
        if (python.Architecture == Architecture.X64)
            priority += 5;

        return priority;
    }

    private static string ExecuteCommand(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(_executionTimeout))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore
            }
            throw new TimeoutException($"Command '{fileName} {arguments}' timed out");
        }

        return output;
    }

    [GeneratedRegex(@"Python\s+(\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PythonVersionRegex();

    [GeneratedRegex(@"\s*[\*\-]?\s*\-\d+\.\d+.*?\s+(.+\.exe)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PyLauncherLineRegex();
}
