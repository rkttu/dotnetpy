using System.Runtime.InteropServices;

namespace DotNetPy.Uv;

/// <summary>
/// Represents a Python project managed by uv, providing declarative Python environment management.
/// </summary>
public sealed class PythonProject : IDisposable
{
    private readonly string _projectName;
    private readonly string _version;
    private readonly string? _description;
    private readonly string? _pythonVersion;
    private readonly IReadOnlyList<PythonDependency> _dependencies;
    private readonly IReadOnlyList<PythonDependency> _devDependencies;
    private readonly IReadOnlyDictionary<string, string> _uvSettings;
    private readonly string _workingDirectory;
    private readonly bool _ownsWorkingDirectory;

    private string? _venvPath;
    private string? _pythonExecutable;
    private string? _pythonLibrary;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Gets the project name.
    /// </summary>
    public string ProjectName => _projectName;

    /// <summary>
    /// Gets the project version.
    /// </summary>
    public string Version => _version;

    /// <summary>
    /// Gets the working directory for this project.
    /// </summary>
    public string WorkingDirectory => _workingDirectory;

    /// <summary>
    /// Gets the virtual environment path.
    /// </summary>
    public string? VirtualEnvironmentPath => _venvPath;

    /// <summary>
    /// Gets the Python executable path.
    /// </summary>
    public string? PythonExecutable => _pythonExecutable;

    /// <summary>
    /// Gets the Python library path (for embedding).
    /// </summary>
    public string? PythonLibrary => _pythonLibrary;

    /// <summary>
    /// Gets whether the project has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the dependencies for this project.
    /// </summary>
    public IReadOnlyList<PythonDependency> Dependencies => _dependencies;

    /// <summary>
    /// Gets the development dependencies for this project.
    /// </summary>
    public IReadOnlyList<PythonDependency> DevDependencies => _devDependencies;

    internal PythonProject(
        string projectName,
        string version,
        string? description,
        string? pythonVersion,
        IReadOnlyList<PythonDependency> dependencies,
        IReadOnlyList<PythonDependency> devDependencies,
        IReadOnlyDictionary<string, string> uvSettings,
        string? workingDirectory)
    {
        _projectName = projectName;
        _version = version;
        _description = description;
        _pythonVersion = pythonVersion;
        _dependencies = dependencies;
        _devDependencies = devDependencies;
        _uvSettings = uvSettings;

        if (string.IsNullOrEmpty(workingDirectory))
        {
            _workingDirectory = Path.Combine(
                Path.GetTempPath(),
                "dotnetpy-projects",
                $"{projectName}-{Guid.NewGuid():N}"[..32]);
            _ownsWorkingDirectory = true;
        }
        else
        {
            _workingDirectory = workingDirectory;
            _ownsWorkingDirectory = false;
        }
    }

    /// <summary>
    /// Creates a new PythonProjectBuilder for fluent configuration.
    /// </summary>
    public static PythonProjectBuilder CreateBuilder() => new();

    /// <summary>
    /// Initializes the Python environment asynchronously.
    /// This creates the pyproject.toml, virtual environment, and installs dependencies.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    /// <exception cref="DotNetPyException">Thrown when initialization fails.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        ThrowIfDisposed();

        // Ensure uv is available
        UvCli.EnsureAvailable();

        // Create working directory
        Directory.CreateDirectory(_workingDirectory);

        // Generate and write pyproject.toml
        var pyprojectPath = Path.Combine(_workingDirectory, "pyproject.toml");
        var tomlContent = GeneratePyProjectToml();
        await File.WriteAllTextAsync(pyprojectPath, tomlContent, cancellationToken).ConfigureAwait(false);

        // Initialize uv project and sync dependencies
        var syncResult = await UvCli.RunAsync("sync", _workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!syncResult.Success)
        {
            throw new DotNetPyException(
                $"Failed to sync Python environment: {syncResult.Error}\n{syncResult.Output}");
        }

        // Set up paths
        _venvPath = Path.Combine(_workingDirectory, ".venv");
        await SetPythonPathsAsync(cancellationToken).ConfigureAwait(false);

        if (!File.Exists(_pythonExecutable))
        {
            throw new DotNetPyException(
                $"Python executable not found at expected path: {_pythonExecutable}");
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Initializes the Python environment synchronously.
    /// </summary>
    /// <exception cref="DotNetPyException">Thrown when initialization fails.</exception>
    /// <remarks>
    /// The async work runs on a thread-pool thread to avoid sync-over-async deadlocks
    /// when called from threads that carry a SynchronizationContext (e.g. UI threads).
    /// Prefer <see cref="InitializeAsync(CancellationToken)"/> in async-friendly code paths.
    /// </remarks>
    public void Initialize()
    {
        Task.Run(() => InitializeAsync()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Installs additional packages into the environment.
    /// </summary>
    /// <param name="packages">The packages to install.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the installation operation.</returns>
    public async Task InstallPackagesAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        var packageList = string.Join(" ", packages);
        if (string.IsNullOrWhiteSpace(packageList))
            return;

        var result = await UvCli.RunAsync($"add {packageList}", _workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DotNetPyException($"Failed to install packages: {result.Error}");
        }
    }

    /// <summary>
    /// Installs additional packages into the environment.
    /// </summary>
    /// <param name="packages">The packages to install.</param>
    /// <remarks>
    /// The async work runs on a thread-pool thread to avoid sync-over-async deadlocks
    /// when called from threads that carry a SynchronizationContext (e.g. UI threads).
    /// </remarks>
    public void InstallPackages(params string[] packages)
    {
        Task.Run(() => InstallPackagesAsync(packages)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets a DotNetPyExecutor instance configured to use this project's Python environment.
    /// The executor is automatically configured with the virtual environment's site-packages
    /// in sys.path, enabling access to all installed packages.
    /// </summary>
    /// <param name="autoLoadSitePackages">
    /// If true (default), automatically adds the virtual environment's site-packages to sys.path.
    /// Set to false if you want to manage sys.path manually.
    /// </param>
    /// <returns>A DotNetPyExecutor instance configured for this project.</returns>
    /// <exception cref="DotNetPyException">Thrown when the project is not initialized.</exception>
    public DotNetPyExecutor GetExecutor(bool autoLoadSitePackages = true)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrEmpty(_pythonLibrary) || !File.Exists(_pythonLibrary))
        {
            throw new DotNetPyException(
                $"Python library not found. Expected at: {_pythonLibrary}");
        }

        // Set up virtual environment paths before initialization.
        // Run on the thread pool to avoid sync-over-async deadlocks when the caller
        // holds a SynchronizationContext (e.g. UI threads).
        Task.Run(() => SetupVirtualEnvironmentAsync()).GetAwaiter().GetResult();

        var executor = DotNetPyExecutor.GetInstance(_pythonLibrary, null);

        // Automatically load site-packages into sys.path
        if (autoLoadSitePackages && !string.IsNullOrEmpty(_venvPath))
        {
            executor.LoadVirtualEnvironment(_venvPath);
        }

        return executor;
    }

    /// <summary>
    /// Gets the site-packages directory path for this project's virtual environment.
    /// </summary>
    /// <returns>The site-packages path, or null if not found.</returns>
    public string? GetSitePackagesPath()
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_venvPath) || !Directory.Exists(_venvPath))
        {
            return null;
        }

        return DotNetPyExecutorExtensions.FindSitePackages(_venvPath);
    }

    /// <summary>
    /// Sets up environment variables for the virtual environment.
    /// </summary>
    private async Task SetupVirtualEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_venvPath) || !Directory.Exists(_venvPath))
            return;

        // Set VIRTUAL_ENV environment variable
        Environment.SetEnvironmentVariable("VIRTUAL_ENV", _venvPath);

        // Add virtual environment's bin/Scripts to PATH
        var pathSeparator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ";" : ":";
        var binPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(_venvPath, "Scripts")
            : Path.Combine(_venvPath, "bin");

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!currentPath.Contains(binPath))
        {
            Environment.SetEnvironmentVariable("PATH", $"{binPath}{pathSeparator}{currentPath}");
        }

        // Set PYTHONHOME to the virtual environment
        // This ensures Python uses the venv's site-packages
        var pythonHome = await GetPythonHomeAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(pythonHome))
        {
            Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);
        }
    }

    /// <summary>
    /// Gets the Python home directory from the virtual environment.
    /// </summary>
    private async Task<string?> GetPythonHomeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_pythonExecutable) || !File.Exists(_pythonExecutable))
            return null;

        try
        {
            var result = await UvCli.RunCommandAsync(
                _pythonExecutable,
                "-c \"import sys; print(sys.prefix)\"",
                _workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                return result.Output.Trim();
            }
        }
        catch
        {
            // Ignore errors
        }

        return _venvPath;
    }

    /// <summary>
    /// Runs a Python script in the project environment.
    /// </summary>
    /// <param name="script">The Python script content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing success status, output, and error.</returns>
    public async Task<(bool Success, string Output, string Error)> RunScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        var scriptPath = Path.Combine(_workingDirectory, $"_script_{Guid.NewGuid():N}.py");
        try
        {
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);
            return await UvCli.RunAsync($"run python \"{scriptPath}\"", _workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Runs a Python command in the project environment.
    /// </summary>
    /// <param name="pythonArgs">Arguments to pass to python.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing success status, output, and error.</returns>
    public async Task<(bool Success, string Output, string Error)> RunPythonAsync(
        string pythonArgs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        return await UvCli.RunAsync($"run python {pythonArgs}", _workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the pyproject.toml content for this project.
    /// </summary>
    /// <returns>The TOML content as a string.</returns>
    public string GetPyProjectToml()
    {
        return GeneratePyProjectToml();
    }

    private string GeneratePyProjectToml()
    {
        var builder = new PythonProjectBuilder()
            .WithProjectName(_projectName)
            .WithVersion(_version);

        if (!string.IsNullOrEmpty(_description))
            builder.WithDescription(_description);

        if (!string.IsNullOrEmpty(_pythonVersion))
            builder.WithPythonVersion(_pythonVersion);

        builder.AddDependencies([.. _dependencies]);

        foreach (var dep in _devDependencies)
        {
            builder.AddDevDependency(dep.Name, dep.VersionConstraint);
        }

        foreach (var (key, value) in _uvSettings)
        {
            builder.WithUvSetting(key, value);
        }

        return builder.GeneratePyProjectToml();
    }

    private async Task SetPythonPathsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_venvPath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _pythonExecutable = Path.Combine(_venvPath, "Scripts", "python.exe");
        }
        else
        {
            _pythonExecutable = Path.Combine(_venvPath, "bin", "python");
        }

        // Try to find Python library
        _pythonLibrary = await FindPythonLibraryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> FindPythonLibraryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_pythonExecutable) || !File.Exists(_pythonExecutable))
            return null;

        try
        {
            // Use Python to find its library path
            var result = await UvCli.RunCommandAsync(
                _pythonExecutable,
                "-c \"import sys; import os; v = sys.version_info; print(os.path.join(sys.base_prefix, f'python{v.major}{v.minor}.dll' if sys.platform == 'win32' else f'lib/libpython{v.major}.{v.minor}.so'))\"",
                _workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                var libPath = result.Output.Trim();
                if (File.Exists(libPath))
                    return libPath;
            }

            // Fallback: search in common locations
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check base prefix directory
                var basePrefixResult = await UvCli.RunCommandAsync(
                    _pythonExecutable,
                    "-c \"import sys; print(sys.base_prefix)\"",
                    _workingDirectory,
                    cancellationToken).ConfigureAwait(false);

                if (basePrefixResult.Success)
                {
                    var basePrefix = basePrefixResult.Output.Trim();
                    if (Directory.Exists(basePrefix))
                    {
                        var dlls = Directory.GetFiles(basePrefix, "python*.dll")
                            .Where(f => !f.Contains("_d.dll") && !f.EndsWith("w.dll"))
                            .OrderByDescending(f => f.Length)
                            .ToList();

                        if (dlls.Count > 0)
                            return dlls[0];
                    }
                }
            }
        }
        catch
        {
            // Ignore discovery errors
        }

        return null;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                "Python project has not been initialized. Call InitializeAsync() first.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PythonProject));
    }

    /// <summary>
    /// Releases resources used by this project.
    /// If the project owns its working directory, it will be deleted.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_ownsWorkingDirectory && Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        _disposed = true;
    }
}
