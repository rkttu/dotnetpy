using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Fixture for managing Python uv-based virtual environments in integration tests.
/// Provides setup and teardown for isolated Python environments with specific packages.
/// </summary>
public sealed class UvEnvironmentFixture : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _venvPath;
    private bool _disposed;

    /// <summary>
    /// Gets the path to the Python executable in the virtual environment.
    /// </summary>
    public string PythonExecutable { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the path to the Python library (.dll/.so/.dylib) in the virtual environment.
    /// </summary>
    public string PythonLibrary { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether uv is available on the system.
    /// </summary>
    public bool IsUvAvailable { get; private set; }

    /// <summary>
    /// Gets whether the virtual environment was successfully created.
    /// </summary>
    public bool IsEnvironmentReady { get; private set; }

    /// <summary>
    /// Gets the Python version string.
    /// </summary>
    public string? PythonVersion { get; private set; }

    public UvEnvironmentFixture()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "dotnetpy-integration-tests", Guid.NewGuid().ToString("N")[..8]);
        _venvPath = Path.Combine(_testDirectory, ".venv");
        Directory.CreateDirectory(_testDirectory);
    }

    /// <summary>
    /// Initializes the virtual environment with uv.
    /// </summary>
    /// <param name="pythonVersion">Python version to use (e.g., "3.11", "3.12"). If null, uses default.</param>
    /// <param name="packages">Optional packages to install.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public async Task<bool> InitializeAsync(string? pythonVersion = null, params string[] packages)
    {
        try
        {
            // Check if uv is available
            IsUvAvailable = await CheckUvAvailableAsync();
            if (!IsUvAvailable)
            {
                return false;
            }

            // Create virtual environment with uv
            var venvArgs = pythonVersion != null
                ? $"venv --python {pythonVersion} \"{_venvPath}\""
                : $"venv \"{_venvPath}\"";

            var venvResult = await RunUvCommandAsync(venvArgs);
            if (!venvResult.Success)
            {
                return false;
            }

            // Determine Python paths based on platform
            SetPythonPaths();

            if (!File.Exists(PythonExecutable))
            {
                return false;
            }

            // Get Python version
            PythonVersion = await GetPythonVersionAsync();

            // Install packages if specified
            if (packages.Length > 0)
            {
                var installResult = await InstallPackagesAsync(packages);
                if (!installResult)
                {
                    return false;
                }
            }

            IsEnvironmentReady = true;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs packages into the virtual environment using uv pip.
    /// </summary>
    public async Task<bool> InstallPackagesAsync(params string[] packages)
    {
        if (packages.Length == 0)
            return true;

        var packageList = string.Join(" ", packages);
        var result = await RunUvCommandAsync($"pip install {packageList}", _venvPath);
        return result.Success;
    }

    /// <summary>
    /// Runs a Python script in the virtual environment.
    /// </summary>
    public async Task<(bool Success, string Output, string Error)> RunPythonScriptAsync(string script)
    {
        var scriptPath = Path.Combine(_testDirectory, $"script_{Guid.NewGuid():N}.py");
        try
        {
            await File.WriteAllTextAsync(scriptPath, script);
            return await RunCommandAsync(PythonExecutable, $"\"{scriptPath}\"");
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    private async Task<bool> CheckUvAvailableAsync()
    {
        try
        {
            var result = await RunCommandAsync("uv", "--version");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private void SetPythonPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PythonExecutable = Path.Combine(_venvPath, "Scripts", "python.exe");
            
            // Find python DLL - could be pythonXY.dll
            var scriptsDir = Path.Combine(_venvPath, "Scripts");
            if (Directory.Exists(scriptsDir))
            {
                var pythonDlls = Directory.GetFiles(scriptsDir, "python*.dll")
                    .Where(f => !f.Contains("_d.dll")) // Exclude debug versions
                    .OrderByDescending(f => f.Length)
                    .ToList();
                
                if (pythonDlls.Count > 0)
                {
                    PythonLibrary = pythonDlls[0];
                    return;
                }
            }

            // Fallback: Find Python library using the venv's python executable
            // Run python to get the library path
            var pythonLibPath = GetPythonLibraryPathFromExecutable(PythonExecutable);
            if (!string.IsNullOrEmpty(pythonLibPath) && File.Exists(pythonLibPath))
            {
                PythonLibrary = pythonLibPath;
                return;
            }

            // Fallback: Try PYTHONHOME environment variable
            var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (!string.IsNullOrEmpty(pythonHome) && Directory.Exists(pythonHome))
            {
                var homeDlls = Directory.GetFiles(pythonHome, "python*.dll")
                    .Where(f => !f.Contains("_d.dll"))
                    .OrderByDescending(f => f.Length)
                    .ToList();
                if (homeDlls.Count > 0)
                {
                    PythonLibrary = homeDlls[0];
                    return;
                }
            }

            // Fallback: Use DotNetPy's discovery to find the library
            try
            {
                var discovered = PythonDiscovery.FindPython();
                if (discovered != null && !string.IsNullOrEmpty(discovered.LibraryPath))
                {
                    PythonLibrary = discovered.LibraryPath;
                }
            }
            catch
            {
                // Ignore discovery errors
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PythonExecutable = Path.Combine(_venvPath, "bin", "python");
            
            // Find libpython*.so
            var libDir = Path.Combine(_venvPath, "lib");
            if (Directory.Exists(libDir))
            {
                var soFiles = Directory.GetFiles(libDir, "libpython*.so*", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.Length)
                    .ToList();
                if (soFiles.Count > 0)
                {
                    PythonLibrary = soFiles[0];
                    return;
                }
            }

            // Fallback: Use discovery
            try
            {
                var discovered = PythonDiscovery.FindPython();
                if (discovered != null && !string.IsNullOrEmpty(discovered.LibraryPath))
                {
                    PythonLibrary = discovered.LibraryPath;
                }
            }
            catch
            {
                // Ignore discovery errors
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            PythonExecutable = Path.Combine(_venvPath, "bin", "python");
            
            // Find libpython*.dylib
            var libDir = Path.Combine(_venvPath, "lib");
            if (Directory.Exists(libDir))
            {
                var dylibFiles = Directory.GetFiles(libDir, "libpython*.dylib", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.Length)
                    .ToList();
                if (dylibFiles.Count > 0)
                {
                    PythonLibrary = dylibFiles[0];
                    return;
                }
            }

            // Fallback: Use discovery
            try
            {
                var discovered = PythonDiscovery.FindPython();
                if (discovered != null && !string.IsNullOrEmpty(discovered.LibraryPath))
                {
                    PythonLibrary = discovered.LibraryPath;
                }
            }
            catch
            {
                // Ignore discovery errors
            }
        }
    }

    private static string? GetPythonLibraryPathFromExecutable(string pythonExe)
    {
        if (!File.Exists(pythonExe))
            return null;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-c \"import sys; import os; print(os.path.join(sys.base_prefix, 'python' + str(sys.version_info.major) + str(sys.version_info.minor) + '.dll'))\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode == 0 && File.Exists(output))
            {
                return output;
            }

            // Try alternative: get from sys.base_prefix
            process.StartInfo.Arguments = "-c \"import sys; print(sys.base_prefix)\"";
            process.Start();
            var basePrefix = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode == 0 && Directory.Exists(basePrefix))
            {
                var dlls = Directory.GetFiles(basePrefix, "python*.dll")
                    .Where(f => !f.Contains("_d.dll"))
                    .OrderByDescending(f => f.Length)
                    .ToList();
                if (dlls.Count > 0)
                {
                    return dlls[0];
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    private async Task<string?> GetPythonVersionAsync()
    {
        var result = await RunCommandAsync(PythonExecutable, "--version");
        if (result.Success)
        {
            return result.Output.Trim();
        }
        return null;
    }

    private async Task<(bool Success, string Output, string Error)> RunUvCommandAsync(string arguments, string? workingDirectory = null)
    {
        return await RunCommandAsync("uv", arguments, workingDirectory);
    }

    private static async Task<(bool Success, string Output, string Error)> RunCommandAsync(
        string command,
        string arguments,
        string? workingDirectory = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        _disposed = true;
    }
}
