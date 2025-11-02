namespace DotNetPy;

/// <summary>
/// Static API for Python execution (for simple use cases).
/// </summary>
/// <remarks>
/// Use this in simple scenarios where DI is not used.
/// If you are using DI, it is recommended to register with AddPythonExecutor() and inject it.
/// </remarks>
public static class Python
{
    private static readonly Lazy<DotNetPyExecutor> _default =
        new Lazy<DotNetPyExecutor>(() => DotNetPyExecutor.GetInstance());

    /// <summary>
    /// The default Python executor instance.
    /// </summary>
    public static DotNetPyExecutor GetInstance()
        => _default.Value;

    /// <summary>
    /// Initializes Python with automatic discovery (finds the best available Python installation).
    /// </summary>
    /// <param name="options">Optional discovery options to filter Python installations.</param>
    /// <remarks>
    /// This method must be called before the first Python call.
    /// Calling it after initialization will throw an exception.
    /// </remarks>
    /// <exception cref="DotNetPyException">Thrown when no suitable Python installation is found.</exception>
    public static void Initialize(PythonDiscoveryOptions? options = null)
    {
        if (_default.IsValueCreated)
            return;

        var pythonInfo = PythonDiscovery.FindPython(options);
        if (pythonInfo == null)
        {
            throw new DotNetPyException(
                "No suitable Python installation found. Please install Python or specify the library path manually using Python.Initialize(string libraryPath).");
        }

        var _ = DotNetPyExecutor.GetInstance(pythonInfo.LibraryPath);
    }

    /// <summary>
    /// Initializes Python with a specific library path.
    /// </summary>
    /// <param name="libraryPath">The path to the Python shared library.</param>
    /// <remarks>
    /// This method must be called before the first Python call.
    /// Calling it after initialization will throw an exception.
    /// </remarks>
    public static void Initialize(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        if (_default.IsValueCreated)
            return;

        // IMPORTANT FOR CONTRIBUTORS:
        // Do not change the return type of this method from void to PythonExecutor.
        //
        // Reasons:
        // 1. Reference Count Mismatch: Disposing the returned instance would decrement _referenceCount,
        //    but the Lazy<T> in _default.Value would still reference the same instance.
        //    Subsequent calls to Python.Instance would return a disposed object, causing an ObjectDisposedException.
        //
        // 2. Lazy<T> Initialization Timing Issues: Since Initialize() does not initialize _default.Value,
        //    the first call to Python.Instance would initialize the Lazy, potentially ignoring the libraryPath.
        //
        // 3. Design Intent: Initialize() is intended for global singleton initialization only.
        //    If you need to manage the instance directly, you should use PythonExecutor.GetInstance().
        //
        // Correct Usage Pattern:
        //   DotNetPy.Initialize(customPath);  // Global initialization
        //   var executor = DotNetPy.Instance; // Access the singleton
        var _ = DotNetPyExecutor.GetInstance(libraryPath);
    }

    /// <summary>
    /// Executes Python code.
    /// </summary>
    public static void Execute(string code)
        => GetInstance().Execute(code);

    /// <summary>
    /// Executes Python code with injected variables.
    /// </summary>
    public static void Execute(string code, Dictionary<string, object?> variables)
        => GetInstance().Execute(code, variables);

    /// <summary>
    /// Executes Python code and returns the result.
    /// </summary>
    public static DotNetPyValue? ExecuteAndCapture(
        string code,
        string resultVariable = "result")
        => GetInstance().ExecuteAndCapture(code, resultVariable);

    /// <summary>
    /// Executes Python code with injected variables and returns the result.
    /// </summary>
    public static DotNetPyValue? ExecuteAndCapture(
        string code,
        Dictionary<string, object?> variables,
        string resultVariable = "result")
        => GetInstance().ExecuteAndCapture(code, variables, resultVariable);

    /// <summary>
    /// Evaluates a Python expression.
    /// </summary>
    public static DotNetPyValue? Evaluate(string expression)
        => GetInstance().Evaluate(expression);

    /// <summary>
    /// Checks if a specific global variable exists.
    /// </summary>
    public static bool VariableExists(string variableName)
        => GetInstance().VariableExists(variableName);

    /// <summary>
    /// Returns a list of variables that actually exist from a given list of variable names.
    /// </summary>
    public static IReadOnlyList<string> GetExistingVariables(params string[] variableNames)
        => GetInstance().GetExistingVariables(variableNames);

    /// <summary>
    /// Gets the value of a specific global variable from previously executed code.
    /// </summary>
    public static DotNetPyValue? CaptureVariable(string variableName)
        => GetInstance().CaptureVariable(variableName);

    /// <summary>
    /// Gets the values of multiple global variables at once.
    /// </summary>
    public static DotNetPyDictionary CaptureVariables(params string[] variableNames)
        => GetInstance().CaptureVariables(variableNames);

    /// <summary>
    /// Deletes a specific global variable.
    /// </summary>
    public static bool DeleteVariable(string variableName)
        => GetInstance().DeleteVariable(variableName);

    /// <summary>
    /// Deletes multiple global variables at once.
    /// </summary>
    public static IReadOnlyList<string> DeleteVariables(params string[] variableNames)
        => GetInstance().DeleteVariables(variableNames);

    /// <summary>
    /// Clears global variables.
    /// </summary>
    public static void ClearGlobals()
        => GetInstance().ClearGlobals();
}
