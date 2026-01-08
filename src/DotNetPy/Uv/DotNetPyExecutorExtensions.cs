using System.Runtime.InteropServices;

namespace DotNetPy.Uv;

/// <summary>
/// Extension methods for DotNetPyExecutor to work with virtual environments.
/// </summary>
public static class DotNetPyExecutorExtensions
{
    /// <summary>
    /// Loads a virtual environment's site-packages into the Python interpreter.
    /// This method adds the virtual environment's site-packages directory to sys.path,
    /// enabling the use of packages installed in the virtual environment.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <param name="venvPath">The path to the virtual environment directory (e.g., ".venv").</param>
    /// <returns>The site-packages path that was added, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when executor or venvPath is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the virtual environment directory does not exist.</exception>
    public static string? LoadVirtualEnvironment(this DotNetPyExecutor executor, string venvPath)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(venvPath);

        if (!Directory.Exists(venvPath))
        {
            throw new DirectoryNotFoundException(
                $"Virtual environment directory not found: {venvPath}");
        }

        var sitePackagesPath = FindSitePackages(venvPath);
        if (sitePackagesPath == null)
        {
            return null;
        }

        AddSitePackagesToPath(executor, sitePackagesPath);
        return sitePackagesPath;
    }

    /// <summary>
    /// Loads a virtual environment's site-packages into the Python interpreter.
    /// This is a convenience overload that accepts a PythonProject instance.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <param name="project">The PythonProject instance.</param>
    /// <returns>The site-packages path that was added, or null if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when executor or project is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the project is not initialized.</exception>
    public static string? LoadVirtualEnvironment(this DotNetPyExecutor executor, PythonProject project)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(project);

        if (!project.IsInitialized)
        {
            throw new InvalidOperationException(
                "The Python project must be initialized before loading its virtual environment.");
        }

        if (string.IsNullOrEmpty(project.VirtualEnvironmentPath))
        {
            throw new InvalidOperationException(
                "The Python project does not have a virtual environment path.");
        }

        return executor.LoadVirtualEnvironment(project.VirtualEnvironmentPath);
    }

    /// <summary>
    /// Finds the site-packages directory within a virtual environment.
    /// Supports both Windows (.venv/Lib/site-packages) and Unix (.venv/lib/pythonX.Y/site-packages) layouts.
    /// </summary>
    /// <param name="venvPath">The path to the virtual environment.</param>
    /// <returns>The path to site-packages, or null if not found.</returns>
    public static string? FindSitePackages(string venvPath)
    {
        ArgumentNullException.ThrowIfNull(venvPath);

        if (!Directory.Exists(venvPath))
        {
            return null;
        }

        // Windows: .venv/Lib/site-packages
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsPath = Path.Combine(venvPath, "Lib", "site-packages");
            if (Directory.Exists(windowsPath))
            {
                return windowsPath;
            }
        }

        // Unix (Linux/macOS): .venv/lib/pythonX.Y/site-packages
        var libPath = Path.Combine(venvPath, "lib");
        if (Directory.Exists(libPath))
        {
            // Find python* directories (e.g., python3.10, python3.11, python3.12)
            var pythonDirs = Directory.GetDirectories(libPath, "python*");
            foreach (var pythonDir in pythonDirs.OrderByDescending(d => d))
            {
                var sitePackagesPath = Path.Combine(pythonDir, "site-packages");
                if (Directory.Exists(sitePackagesPath))
                {
                    return sitePackagesPath;
                }
            }
        }

        // Fallback: Check Windows path on non-Windows (in case of cross-platform venvs)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsPath = Path.Combine(venvPath, "Lib", "site-packages");
            if (Directory.Exists(windowsPath))
            {
                return windowsPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all site-packages directories within a virtual environment.
    /// This can be useful when a venv has multiple Python version directories.
    /// </summary>
    /// <param name="venvPath">The path to the virtual environment.</param>
    /// <returns>An enumerable of site-packages paths.</returns>
    public static IEnumerable<string> FindAllSitePackages(string venvPath)
    {
        ArgumentNullException.ThrowIfNull(venvPath);

        if (!Directory.Exists(venvPath))
        {
            yield break;
        }

        // Windows: .venv/Lib/site-packages
        var windowsPath = Path.Combine(venvPath, "Lib", "site-packages");
        if (Directory.Exists(windowsPath))
        {
            yield return windowsPath;
        }

        // Unix (Linux/macOS): .venv/lib/pythonX.Y/site-packages
        var libPath = Path.Combine(venvPath, "lib");
        if (Directory.Exists(libPath))
        {
            var pythonDirs = Directory.GetDirectories(libPath, "python*");
            foreach (var pythonDir in pythonDirs)
            {
                var sitePackagesPath = Path.Combine(pythonDir, "site-packages");
                if (Directory.Exists(sitePackagesPath))
                {
                    yield return sitePackagesPath;
                }
            }
        }
    }

    /// <summary>
    /// Adds a site-packages path to Python's sys.path.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <param name="sitePackagesPath">The path to add.</param>
    private static void AddSitePackagesToPath(DotNetPyExecutor executor, string sitePackagesPath)
    {
        // Escape backslashes for Python string literal
        var escapedPath = sitePackagesPath.Replace("\\", "\\\\");

        executor.Execute($@"
import sys
_venv_site_packages = r'{escapedPath}'
if _venv_site_packages not in sys.path:
    sys.path.insert(0, _venv_site_packages)
del _venv_site_packages
");
    }

    /// <summary>
    /// Checks if a virtual environment's site-packages is already loaded in sys.path.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <param name="venvPath">The path to the virtual environment.</param>
    /// <returns>True if the site-packages is in sys.path, false otherwise.</returns>
    public static bool IsVirtualEnvironmentLoaded(this DotNetPyExecutor executor, string venvPath)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(venvPath);

        var sitePackagesPath = FindSitePackages(venvPath);
        if (sitePackagesPath == null)
        {
            return false;
        }

        var escapedPath = sitePackagesPath.Replace("\\", "\\\\");
        using var result = executor.ExecuteAndCapture($@"
import sys
result = r'{escapedPath}' in sys.path
");
        return result?.GetBoolean() ?? false;
    }

    /// <summary>
    /// Gets the current sys.path from Python.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <returns>A list of paths in sys.path.</returns>
    public static IReadOnlyList<string> GetSysPath(this DotNetPyExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        using var result = executor.ExecuteAndCapture(@"
import sys
result = list(sys.path)
");
        var list = result?.ToList();
        if (list == null)
        {
            return [];
        }

        return list.OfType<string>().ToList();
    }

    /// <summary>
    /// Adds a custom path to Python's sys.path.
    /// </summary>
    /// <param name="executor">The executor instance.</param>
    /// <param name="path">The path to add.</param>
    /// <param name="prepend">If true, adds to the beginning of sys.path; otherwise adds to the end.</param>
    public static void AddToSysPath(this DotNetPyExecutor executor, string path, bool prepend = true)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(path);

        var escapedPath = path.Replace("\\", "\\\\");
        var method = prepend ? "insert(0, _path)" : "append(_path)";

        executor.Execute($@"
import sys
_path = r'{escapedPath}'
if _path not in sys.path:
    sys.path.{method}
del _path
");
    }
}
