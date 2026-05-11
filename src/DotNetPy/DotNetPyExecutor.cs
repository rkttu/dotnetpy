using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetPy;

/// <summary>
/// An executor that runs Python scripts and returns the results.
/// It is guaranteed that only one instance exists per process.
/// </summary>
public sealed partial class DotNetPyExecutor : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.Default,
    };

    private static readonly Encoding _utf8Encoding = new UTF8Encoding(false);
    private static readonly object _instanceLock = new();
    private static volatile DotNetPyExecutor? _instance = null;
    private static int _referenceCount = 0;

    private static readonly object _initLock = new();
    private static volatile bool _initialized = false;
    private static string? _initializedLibraryPath = null;
    private static PythonInfo? _currentPythonInfo = null;
    private static IntPtr _libraryHandle = IntPtr.Zero;
    private volatile bool _disposed = false;

    // Isolated-namespace state.
    //
    // For the shared (singleton) executor this stays IntPtr.Zero and all code
    // runs against __main__'s globals dict (the historical behaviour). For an
    // executor produced by <see cref="CreateIsolated"/>, this owns a strong
    // reference to a fresh dict that serves as both globals and locals for
    // every execution. The dict is freed via Py_DecRef in <see cref="Dispose"/>.
    //
    // A non-zero value is sufficient to distinguish the two modes; the bool
    // exists only to make the intent explicit at call sites.
    private readonly IntPtr _isolatedNamespace = IntPtr.Zero;
    private readonly bool _isIsolated = false;

    // Cached resolved namespace pointer for the shared mode. __main__ is created
    // once per process and never unloaded, so its globals dict pointer is stable
    // for the lifetime of the interpreter. Resolving it lazily (on first use,
    // when a GIL is held) avoids needing the GIL at construction time and is
    // safe because every read happens inside a GilLock.
    private IntPtr _sharedNamespaceCache = IntPtr.Zero;

    // Monotonic counter used to mint unique names for internal temporary variables
    // injected into Python's __main__ globals. With free-threaded Python (PEP 703)
    // the GIL no longer serializes interpreter operations, so two concurrent
    // executor calls would otherwise race on shared, fixed names like _json_result.
    private static long _tempVarCounter = 0;

    /// <summary>
    /// Mints a unique Python identifier for an internal temporary variable so that
    /// concurrent calls cannot collide on the same name in __main__ globals.
    /// </summary>
    private static string MakeInternalName(string baseName)
        => $"_dotnetpy_{baseName}_{Interlocked.Increment(ref _tempVarCounter):x}";

    // Python C API function pointer delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyInitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyFinalizeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyIsInitializedDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyGILStateEnsureDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyGILStateReleaseDelegate(IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyRunSimpleStringDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string command);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyRunStringDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str,
        int start,
        IntPtr globals,
        IntPtr locals);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyImportAddModuleDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyModuleGetDictDelegate(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyDictNewDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PyDictSetItemStringDelegate(
        IntPtr dict,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyDictGetItemStringDelegate(
        IntPtr dict,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyUnicodeAsUTF8StringDelegate(IntPtr unicode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyBytesAsStringDelegate(IntPtr bytes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyErrOccurredDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyErrPrintDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyErrClearDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyErrFetchDelegate(out IntPtr pType, out IntPtr pValue, out IntPtr pTraceback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyErrNormalizeExceptionDelegate(ref IntPtr pType, ref IntPtr pValue, ref IntPtr pTraceback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyObjectStrDelegate(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyObjectReprDelegate(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyImportImportModuleDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyObjectGetAttrStringDelegate(
        IntPtr obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyObjectCallFunctionObjArgsDelegate(
        IntPtr callable,
        IntPtr arg1,
        IntPtr sentinel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyEvalSaveThreadDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyEvalRestoreThreadDelegate(IntPtr tstate);

    // Function pointer instances
    private static PyInitializeDelegate? _pyInitialize;
    private static PyFinalizeDelegate? _pyFinalize;
    private static PyIsInitializedDelegate? _pyIsInitialized;
    private static PyGILStateEnsureDelegate? _pyGILStateEnsure;
    private static PyGILStateReleaseDelegate? _pyGILStateRelease;
    private static PyEvalSaveThreadDelegate? _pyEvalSaveThread;
    private static PyEvalRestoreThreadDelegate? _pyEvalRestoreThread;
    private static PyRunSimpleStringDelegate? _pyRunSimpleString;
    private static PyRunStringDelegate? _pyRunString;
    private static PyImportAddModuleDelegate? _pyImportAddModule;
    private static PyModuleGetDictDelegate? _pyModuleGetDict;
    private static PyDictNewDelegate? _pyDictNew;
    private static PyDictSetItemStringDelegate? _pyDictSetItemString;
    private static PyDictGetItemStringDelegate? _pyDictGetItemString;
    private static PyUnicodeAsUTF8StringDelegate? _pyUnicodeAsUTF8String;
    private static PyBytesAsStringDelegate? _pyBytesAsString;
    private static PyErrOccurredDelegate? _pyErrOccurred;
    private static PyErrPrintDelegate? _pyErrPrint;
    private static PyErrClearDelegate? _pyErrClear;
    private static PyErrFetchDelegate? _pyErrFetch;
    private static PyErrNormalizeExceptionDelegate? _pyErrNormalizeException;
    private static PyObjectStrDelegate? _pyObjectStr;
    private static PyObjectReprDelegate? _pyObjectRepr;
    private static PyImportImportModuleDelegate? _pyImportImportModule;
    private static PyObjectGetAttrStringDelegate? _pyObjectGetAttrString;
    private static PyObjectCallFunctionObjArgsDelegate? _pyObjectCallFunctionObjArgs;

    // Py_eval_input = 258
    private const int Py_eval_input = 258;
    // Py_file_input = 257
    private const int Py_file_input = 257;

    /// <summary>
    /// Private constructor for the process-wide shared executor.
    /// Created through <see cref="GetInstance(string?, PythonInfo?)"/>.
    /// </summary>
    private DotNetPyExecutor(string? libraryPath, PythonInfo? pythonInfo)
    {
        EnsureInitialized(libraryPath, pythonInfo);
    }

    /// <summary>
    /// Private constructor for an isolated executor that owns a fresh namespace.
    /// Created through <see cref="CreateIsolated"/>; the Python runtime must
    /// already be initialized.
    /// </summary>
    private DotNetPyExecutor(bool isolated)
    {
        if (!isolated)
            throw new ArgumentException("This constructor only creates isolated executors.", nameof(isolated));
        if (!_initialized)
            throw new InvalidOperationException(
                "Python runtime must be initialized before creating an isolated executor. " +
                "Call Python.Initialize() (or DotNetPyExecutor.GetInstance) first.");

        _isIsolated = true;

        using var gil = new GilLock();

        IntPtr ns = _pyDictNew!();
        if (ns == IntPtr.Zero)
            throw new DotNetPyException("Failed to allocate isolated namespace dict.");

        try
        {
            // Without __builtins__ user code cannot do `import json`, `print`, `len`, etc.
            IntPtr builtins = _pyImportAddModule!("builtins"); // borrowed
            if (builtins == IntPtr.Zero)
                throw new DotNetPyException("Failed to resolve builtins module for isolated namespace.");

            int rc = _pyDictSetItemString!(ns, "__builtins__", builtins);
            if (rc != 0)
                throw new DotNetPyException("Failed to inject __builtins__ into isolated namespace.");

            _isolatedNamespace = ns;
        }
        catch
        {
            // We own the +1 reference returned by PyDict_New; release it on failure.
            DotNetPyObject.FromNewReference(ns)?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a new executor with its own private execution namespace. Multiple
    /// isolated executors can coexist with the shared singleton and with each
    /// other; user variables defined on one executor are invisible to the rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each isolated executor owns a fresh Python dict that serves as both the
    /// globals and locals for every <c>Execute</c> / <c>ExecuteAndCapture</c> /
    /// <c>Evaluate</c> call on it. The dict is initialised with a reference to
    /// the standard <c>builtins</c> module so user code can call <c>print</c>,
    /// <c>len</c>, and use <c>import</c> as usual.
    /// </para>
    /// <para>
    /// <b>When to prefer this over the shared singleton:</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><description>You want concurrent callers (especially on free-threaded
    ///   Python) to not race on shared user variable names in <c>__main__</c>.</description></item>
    ///   <item><description>You want to keep one task's Python state out of another
    ///   task's reach without manually clearing globals between runs.</description></item>
    /// </list>
    /// <para>
    /// <b>What you give up:</b> there is no cross-call persistence across
    /// different executors — variables defined on executor A are not visible
    /// from executor B. (Within a single isolated executor, variables still
    /// persist across calls, just like the shared executor.)
    /// </para>
    /// <para>
    /// The Python runtime must be initialised before calling this method
    /// (typically via <see cref="Python.Initialize(PythonDiscoveryOptions?)"/>
    /// or <see cref="Python.Initialize(string)"/>). Dispose the executor
    /// when finished to release the namespace dict.
    /// </para>
    /// </remarks>
    /// <returns>A new isolated executor instance.</returns>
    /// <exception cref="InvalidOperationException">The Python runtime has not been initialized.</exception>
    /// <exception cref="DotNetPyException">Underlying CPython API failed (dict creation or builtins injection).</exception>
    public static DotNetPyExecutor CreateIsolated()
        => new DotNetPyExecutor(isolated: true);

    /// <summary>
    /// Gets the IntPtr to the dict that this executor uses as the execution
    /// namespace. MUST be called with the GIL held.
    /// </summary>
    private IntPtr GetExecutionNamespacePtr()
    {
        if (_isIsolated)
            return _isolatedNamespace;

        // Shared mode: resolve __main__.globals() once and cache. The __main__
        // module and its globals dict are created at interpreter startup and
        // remain valid for the lifetime of the interpreter, so caching the
        // borrowed pointer is safe.
        if (_sharedNamespaceCache != IntPtr.Zero)
            return _sharedNamespaceCache;

        IntPtr mainModule = _pyImportAddModule!("__main__"); // borrowed
        if (mainModule == IntPtr.Zero)
            throw new DotNetPyException("Could not get the __main__ module.");
        IntPtr globals = _pyModuleGetDict!(mainModule); // borrowed from __main__
        if (globals == IntPtr.Zero)
            throw new DotNetPyException("Could not get the __main__ module's globals.");

        _sharedNamespaceCache = globals;
        return globals;
    }

    /// <summary>
    /// Gets the singleton instance of the DotNetPyExecutor.
    /// Only one instance exists per process.
    /// </summary>
    /// <param name="libraryPath">The path to the Python library (only used on the first call).</param>
    /// <param name="pythonInfo">Optional PythonInfo to store metadata about the Python installation.</param>
    /// <returns>The DotNetPyExecutor instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if already initialized with a different path.</exception>
    public static DotNetPyExecutor GetInstance(string? libraryPath = null, PythonInfo? pythonInfo = null)
    {
        // Fast path: If already created and no libraryPath is specified, perform a quick check.
        var currentInstance = _instance;
        if (currentInstance != null && libraryPath == null && !currentInstance._disposed)
        {
            lock (_instanceLock)
            {
                currentInstance = _instance;
                if (currentInstance != null && !currentInstance._disposed)
                {
                    Interlocked.Increment(ref _referenceCount);
                    return currentInstance;
                }
            }
        }

        lock (_instanceLock)
        {
            // If it's disposed or no instance exists, create a new one.
            if (_instance == null || _instance._disposed)
            {
                _instance = new DotNetPyExecutor(libraryPath, pythonInfo);
                _referenceCount = 1;
                return _instance;
            }

            // Validate library path first (before checking if already initialized with different path)
            // This ensures invalid paths throw DotNetPyException instead of InvalidOperationException
            if (libraryPath != null)
            {
                ValidateLibraryPath(libraryPath);
            }

            // Validate if already initialized with a different path.
            if (libraryPath != null && _initializedLibraryPath != null &&
         !string.Equals(libraryPath, _initializedLibraryPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new DotNetPyException(
           $"The Python runtime has already been initialized with a different path. " +
                   $"Initialized path: {_initializedLibraryPath}, Requested path: {libraryPath}");
            }

            Interlocked.Increment(ref _referenceCount);
            return _instance;
        }
    }

    /// <summary>
    /// Returns the current number of active references (for debugging/testing).
    /// </summary>
    public static int ReferenceCount
    {
        get
        {
            lock (_instanceLock)
            {
                return _referenceCount;
            }
        }
    }

    /// <summary>
    /// Gets information about the currently initialized Python installation.
    /// Returns null if Python has not been initialized yet.
    /// </summary>
    public static PythonInfo? CurrentPythonInfo
    {
        get
        {
            lock (_initLock)
            {
                return _currentPythonInfo;
            }
        }
    }

    /// <summary>
    /// Validates the library path without actually loading it.
    /// </summary>
    private static void ValidateLibraryPath(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath, nameof(libraryPath));
        if (!File.Exists(libraryPath))
            throw new DotNetPyException($"The specified Python library does not exist: {libraryPath}", new FileNotFoundException(libraryPath));
    }

    /// <summary>
    /// Initializes the Python interpreter (once per process).
    /// </summary>
    private static void EnsureInitialized(string? libraryPath, PythonInfo? pythonInfo)
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            // Load the library
            LoadPythonLibrary(libraryPath);
            _initializedLibraryPath = libraryPath;
            _currentPythonInfo = pythonInfo;

            // Initialize Python (this acquires the GIL)
            _pyInitialize!();

            // Configure virtual environment paths if applicable (while we still hold the GIL)
            ConfigureVirtualEnvironment(pythonInfo);

            // Release the GIL so that GilLock can properly acquire it later
            // Py_Initialize leaves the GIL held, so we must release it for PyGILState_Ensure to work correctly
            _pyEvalSaveThread!();

            _initialized = true;
        }
    }

    /// <summary>
    /// Configures sys.path for virtual environments.
    /// When Python is embedded, the virtual environment's site-packages is not automatically added.
    /// This method ensures packages installed in the venv are accessible.
    /// </summary>
    private static void ConfigureVirtualEnvironment(PythonInfo? pythonInfo)
    {
        if (pythonInfo == null)
            return;

        // Always configure if we have a site-packages path that differs from the embedded Python's default
        var sitePackagesPath = pythonInfo.SitePackagesPath;
        if (string.IsNullOrEmpty(sitePackagesPath) || !Directory.Exists(sitePackagesPath))
            return;

        try
        {
            // Escape backslashes for Python string literal
            var escapedPath = sitePackagesPath.Replace("\\", "\\\\");

            // Add site-packages to sys.path if not already present
            // Insert at position 0 to give venv packages priority over system packages
            var setupCode = $@"
import sys
_venv_site = '{escapedPath}'
if _venv_site not in sys.path:
    sys.path.insert(0, _venv_site)
del _venv_site
";
            _pyRunSimpleString!(setupCode);
        }
        catch
        {
            // Silently ignore errors during path configuration
            // The worst case is packages won't be found, which will be reported later
        }
    }

    /// <summary>
    /// Loads the Python shared library and initializes function pointers.
    /// </summary>
    private static void LoadPythonLibrary(string? libraryPath)
    {
        if (_libraryHandle != IntPtr.Zero)
            return;

        // Validation is already done in ValidateLibraryPath() called from GetInstance()
        // But we still need to check here for safety
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath, nameof(libraryPath));
        if (!File.Exists(libraryPath))
            throw new DotNetPyException($"The specified Python library does not exist: {libraryPath}", new FileNotFoundException(libraryPath));

        try
        {
            _libraryHandle = NativeLibrary.Load(libraryPath);
        }
        catch (DllNotFoundException ex)
        {
            throw new DotNetPyException(
       $"Could not find the Python library: {libraryPath}", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new DotNetPyException(
                   $"The specified file is not a valid Python library: {libraryPath}", ex);
        }

        // Load function pointers
        try
        {
            _pyInitialize = NativeMethods.LoadFunction<PyInitializeDelegate>(_libraryHandle, "Py_Initialize");
            _pyFinalize = NativeMethods.LoadFunction<PyFinalizeDelegate>(_libraryHandle, "Py_Finalize");
            _pyIsInitialized = NativeMethods.LoadFunction<PyIsInitializedDelegate>(_libraryHandle, "Py_IsInitialized");
            _pyGILStateEnsure = NativeMethods.LoadFunction<PyGILStateEnsureDelegate>(_libraryHandle, "PyGILState_Ensure");
            _pyGILStateRelease = NativeMethods.LoadFunction<PyGILStateReleaseDelegate>(_libraryHandle, "PyGILState_Release");
            _pyEvalSaveThread = NativeMethods.LoadFunction<PyEvalSaveThreadDelegate>(_libraryHandle, "PyEval_SaveThread");
            _pyEvalRestoreThread = NativeMethods.LoadFunction<PyEvalRestoreThreadDelegate>(_libraryHandle, "PyEval_RestoreThread");
            _pyRunSimpleString = NativeMethods.LoadFunction<PyRunSimpleStringDelegate>(_libraryHandle, "PyRun_SimpleString");
            _pyRunString = NativeMethods.LoadFunction<PyRunStringDelegate>(_libraryHandle, "PyRun_String");
            _pyImportAddModule = NativeMethods.LoadFunction<PyImportAddModuleDelegate>(_libraryHandle, "PyImport_AddModule");
            _pyModuleGetDict = NativeMethods.LoadFunction<PyModuleGetDictDelegate>(_libraryHandle, "PyModule_GetDict");
            _pyDictNew = NativeMethods.LoadFunction<PyDictNewDelegate>(_libraryHandle, "PyDict_New");
            _pyDictSetItemString = NativeMethods.LoadFunction<PyDictSetItemStringDelegate>(_libraryHandle, "PyDict_SetItemString");
            _pyDictGetItemString = NativeMethods.LoadFunction<PyDictGetItemStringDelegate>(_libraryHandle, "PyDict_GetItemString");
            _pyUnicodeAsUTF8String = NativeMethods.LoadFunction<PyUnicodeAsUTF8StringDelegate>(_libraryHandle, "PyUnicode_AsUTF8String");
            _pyBytesAsString = NativeMethods.LoadFunction<PyBytesAsStringDelegate>(_libraryHandle, "PyBytes_AsString");
            _pyErrOccurred = NativeMethods.LoadFunction<PyErrOccurredDelegate>(_libraryHandle, "PyErr_Occurred");
            _pyErrPrint = NativeMethods.LoadFunction<PyErrPrintDelegate>(_libraryHandle, "PyErr_Print");
            _pyErrClear = NativeMethods.LoadFunction<PyErrClearDelegate>(_libraryHandle, "PyErr_Clear");
            _pyErrFetch = NativeMethods.LoadFunction<PyErrFetchDelegate>(_libraryHandle, "PyErr_Fetch");
            _pyErrNormalizeException = NativeMethods.LoadFunction<PyErrNormalizeExceptionDelegate>(_libraryHandle, "PyErr_NormalizeException");
            _pyObjectStr = NativeMethods.LoadFunction<PyObjectStrDelegate>(_libraryHandle, "PyObject_Str");
            _pyObjectRepr = NativeMethods.LoadFunction<PyObjectReprDelegate>(_libraryHandle, "PyObject_Repr");
            _pyImportImportModule = NativeMethods.LoadFunction<PyImportImportModuleDelegate>(_libraryHandle, "PyImport_ImportModule");
            _pyObjectGetAttrString = NativeMethods.LoadFunction<PyObjectGetAttrStringDelegate>(_libraryHandle, "PyObject_GetAttrString");
            _pyObjectCallFunctionObjArgs = NativeMethods.LoadFunction<PyObjectCallFunctionObjArgsDelegate>(_libraryHandle, "PyObject_CallFunctionObjArgs");

            // Initialize PythonObject's reference counting functions
            DotNetPyObject.Initialize(_libraryHandle);
        }
        catch (Exception ex)
        {
            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
            throw new DotNetPyException("Could not load Python C API functions.", ex);
        }
    }

    /// <summary>
    /// Validates if a variable name is a valid Python identifier using Python itself.
    /// </summary>
    private bool IsValidPythonIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        ThrowIfDisposed();

        using var gil = new GilLock();

        // Use a per-call unique name so concurrent free-threaded callers don't
        // collide on the same slot, and so isolated executors' scratch lives
        // in their own namespace rather than leaking into __main__.
        string flagVar = MakeInternalName("is_valid");
        string escaped = EscapePythonString(name);

        // Use Python's str.isidentifier() and keyword.iskeyword().
        // PyRun_String honours the (globals, locals) we pass; PyRun_SimpleString
        // would always execute against __main__ and break isolated mode.
        string validationCode = $@"
import keyword
{flagVar} = '{escaped}'.isidentifier() and not keyword.iskeyword('{escaped}')
";

        IntPtr ns = GetExecutionNamespacePtr();

        try
        {
            using var result = DotNetPyObject.FromNewReference(_pyRunString!(validationCode, Py_file_input, ns, ns));
            if (result == null || result.IsInvalid)
            {
                _pyErrClear!();
                return false;
            }

            // Get the validation flag variable (borrowed reference)
            using var isValidObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(ns, flagVar));
            if (isValidObj == null || isValidObj.IsInvalid)
                return false;

            // Convert Python bool to C# bool
            using var strObj = DotNetPyObject.FromNewReference(_pyObjectStr!(isValidObj.DangerousGetHandle()));
            if (strObj == null || strObj.IsInvalid)
                return false;

            string? value = PyObjectToString(strObj);
            return value == "True";
        }
        finally
        {
            // Clean up the temporary variable in the same namespace it landed in.
            CleanupNamespaceVariable(ns, flagVar);
        }
    }

    /// <summary>
    /// Escapes special characters in a Python string literal.
    /// </summary>
    private static string EscapePythonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Executes a Python script.
    /// </summary>
    /// <param name="code">The Python code to execute.</param>
    /// <exception cref="DotNetPyException">Thrown if an error occurs during Python execution.</exception>
    /// <remarks>
    /// <para>
    /// <b>⚠️ SECURITY WARNING:</b> This method executes arbitrary Python code with the same privileges 
    /// as the host .NET process. Never pass untrusted or user-provided input directly to this method.
    /// Doing so may result in remote code execution (RCE) vulnerabilities.
    /// </para>
    /// <para>
    /// Python code has unrestricted access to the file system, network, environment variables, 
    /// and can execute system commands. Always ensure the code parameter contains only 
    /// developer-controlled, trusted content.
    /// </para>
    /// </remarks>
    public void Execute(string code)
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code using PyRun_String to preserve error information
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(code, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while executing the Python code.");
        }
    }

    /// <summary>
    /// Serializes .NET data to JSON, injects it as Python variables, and executes the code.
    /// </summary>
    /// <param name="code">The Python code to execute.</param>
    /// <param name="variables">The variables to inject into Python (name: value).</param>
    /// <remarks>
    /// <para>
    /// <b>⚠️ SECURITY WARNING:</b> This method executes arbitrary Python code with the same privileges 
    /// as the host .NET process. Never pass untrusted or user-provided input as the <paramref name="code"/> parameter.
    /// </para>
    /// <para>
    /// The <paramref name="variables"/> parameter is safe for user-provided data as values are serialized 
    /// to JSON and injected as data, not code. However, the <paramref name="code"/> parameter must contain 
    /// only developer-controlled, trusted content.
    /// </para>
    /// </remarks>
    public void Execute(string code, Dictionary<string, object?> variables)
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Generate Python code by serializing variables to JSON
        var variableCode = new StringBuilder(variables.Count * 100);
        variableCode.AppendLine("import json");
        variableCode.AppendLine("import base64");

        foreach (var kvp in variables)
        {
            if (!IsValidPythonIdentifier(kvp.Key))
                throw new ArgumentException($"'{kvp.Key}' is not a valid Python variable name.", nameof(variables));

            string jsonValue = SerializeToJson(kvp.Value, _jsonOptions);
            string base64 = Convert.ToBase64String(_utf8Encoding.GetBytes(jsonValue));
            variableCode.AppendLine($"{kvp.Key} = json.loads(base64.b64decode('{base64}').decode('utf-8'))");
        }

        // Combine with user code
        string fullCode = variableCode.ToString() + "\n" + NormalizePythonCode(code);

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code using PyRun_String to preserve error information
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(fullCode, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while executing the Python code.");
        }
    }

    /// <summary>
    /// Serializes an object to a JSON string (AOT compatible).
    /// </summary>
    private static string SerializeToJson(object? value, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = options.WriteIndented,
            Encoder = options.Encoder
        });

        WriteJsonValue(writer, value);
        writer.Flush();

        return _utf8Encoding.GetString(stream.ToArray());
    }

    /// <summary>
    /// Writes a value to the Utf8JsonWriter (handles recursively).
    /// </summary>
    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                break;

            case bool b:
                writer.WriteBooleanValue(b);
                break;

            case ulong ul:
                writer.WriteNumberValue(ul);
                break;

            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value));
                break;

            case float f:
                writer.WriteNumberValue(f);
                break;

            case double d:
                writer.WriteNumberValue(d);
                break;

            case decimal m:
                writer.WriteNumberValue(m);
                break;

            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O"));
                break;

            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O"));
                break;

            case Guid guid:
                writer.WriteStringValue(guid.ToString());
                break;

            case IDictionary dict:
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dict)
                {
                    writer.WritePropertyName(entry.Key.ToString() ?? string.Empty);
                    WriteJsonValue(writer, entry.Value);
                }
                writer.WriteEndObject();
                break;

            case IEnumerable enumerable when value is not string:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    WriteJsonValue(writer, item);
                }
                writer.WriteEndArray();
                break;

            default:
                // Serialize anonymous types and general objects using reflection
                var type = value.GetType();
                if (type.Namespace == null || type.Name.Contains("AnonymousType"))
                {
                    // Handle anonymous types
                    writer.WriteStartObject();
                    foreach (var prop in type.GetProperties())
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteJsonValue(writer, prop.GetValue(value));
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    // Serialize general objects using reflection
                    writer.WriteStartObject();
                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteJsonValue(writer, prop.GetValue(value));
                    }
                    writer.WriteEndObject();
                }
                break;
        }
    }

    /// <summary>
    /// Executes a Python script and returns the result as a PyValue (AOT compatible).
    /// </summary>
    /// <param name="code">The Python code to execute.</param>
    /// <param name="resultVariable">The name of the Python variable containing the result (default: "result").</param>
    /// <returns>A PyValue parsing the result of the Python script.</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠️ SECURITY WARNING:</b> This method executes arbitrary Python code with the same privileges 
    /// as the host .NET process. Never pass untrusted or user-provided input as the <paramref name="code"/> parameter.
    /// </para>
    /// </remarks>
    public DotNetPyValue? ExecuteAndCapture(string code, string resultVariable = "result")
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

        // Per-call unique sink name so free-threaded concurrent calls don't race.
        string sinkVar = MakeInternalName("json_result");

        // Extract result via JSON serialization
        string wrapperCode = $@"
import json

# Execute user code
{code}

# Serialize the result to JSON
if '{resultVariable}' in locals() or '{resultVariable}' in globals():
    {sinkVar} = json.dumps({resultVariable}, ensure_ascii=False, default=str)
else:
    {sinkVar} = 'null'
";

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(wrapperCode, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while executing the Python code.");
        }

        try
        {
            // Extract the JSON string from the sink variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, sinkVar));
            if (jsonResultObj == null || jsonResultObj.IsInvalid)
            {
                return null;
            }

            string? jsonString = PyObjectToString(jsonResultObj);
            if (string.IsNullOrEmpty(jsonString))
            {
                return null;
            }

            // Parse JSON to JsonDocument (AOT compatible)
            try
            {
                return new DotNetPyValue(JsonDocument.Parse(jsonString));
            }
            catch (JsonException ex)
            {
                throw new DotNetPyException($"Could not parse Python result as JSON: {ex.Message}", ex);
            }
        }
        finally
        {
            // Clean up the temporary variable
            CleanupTemporaryVariable(sinkVar);
        }
    }

    /// <summary>
    /// Serializes .NET data to JSON, injects it as Python variables, and returns the result.
    /// </summary>
    /// <param name="code">The Python code to execute.</param>
    /// <param name="variables">The variables to inject into Python (name: value).</param>
    /// <param name="resultVariable">The name of the Python variable containing the result (default: "result").</param>
    /// <returns>A PyValue parsing the result of the Python script.</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠️ SECURITY WARNING:</b> This method executes arbitrary Python code with the same privileges 
    /// as the host .NET process. Never pass untrusted or user-provided input as the <paramref name="code"/> parameter.
    /// </para>
    /// <para>
    /// The <paramref name="variables"/> parameter is safe for user-provided data as values are serialized 
    /// to JSON and injected as data, not code.
    /// </para>
    /// </remarks>
    public DotNetPyValue? ExecuteAndCapture(
        string code,
        Dictionary<string, object?> variables,
        string resultVariable = "result")
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

        // Per-call unique sink name so free-threaded concurrent calls don't race.
        string sinkVar = MakeInternalName("json_result");

        // Generate Python code by serializing variables to JSON (using Base64 encoding)
        var variableCode = new StringBuilder(variables.Count * 100);

        foreach (var kvp in variables)
        {
            string jsonValue = SerializeToJson(kvp.Value, _jsonOptions);
            string base64 = Convert.ToBase64String(_utf8Encoding.GetBytes(jsonValue));
            variableCode.AppendLine($"{kvp.Key} = json.loads(base64.b64decode('{base64}').decode('utf-8'))");
        }

        // Extract result via JSON serialization
        string wrapperCode = $@"
import json
import base64

# Inject variables
{variableCode}

# Execute user code
{code}

# Serialize the result to JSON
if '{resultVariable}' in locals() or '{resultVariable}' in globals():
    {sinkVar} = json.dumps({resultVariable}, ensure_ascii=False, default=str)
else:
    {sinkVar} = 'null'
";

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(wrapperCode, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while executing the Python code.");
        }

        try
        {
            // Extract the JSON string from the sink variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, sinkVar));
            if (jsonResultObj == null || jsonResultObj.IsInvalid)
            {
                return null;
            }

            string? jsonString = PyObjectToString(jsonResultObj);
            if (string.IsNullOrEmpty(jsonString))
            {
                return null;
            }

            // Parse JSON to JsonDocument (AOT compatible)
            try
            {
                return new DotNetPyValue(JsonDocument.Parse(jsonString));
            }
            catch (JsonException ex)
            {
                throw new DotNetPyException($"Could not parse Python result as JSON: {ex.Message}", ex);
            }
        }
        finally
        {
            // Clean up the temporary variable
            CleanupTemporaryVariable(sinkVar);
        }
    }

    /// <summary>
    /// Evaluates a Python expression and returns the result as a PyValue.
    /// </summary>
    /// <param name="expression">The Python expression to evaluate (e.g., "1+1", "[1,2,3]").</param>
    /// <returns>A PyValue parsing the result of the expression.</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠️ SECURITY WARNING:</b> This method evaluates arbitrary Python expressions with the same privileges 
    /// as the host .NET process. Never pass untrusted or user-provided input as the <paramref name="expression"/> parameter.
    /// </para>
    /// </remarks>
    public DotNetPyValue? Evaluate(string expression)
    {
        // Use a per-call unique sink so concurrent Evaluate calls under free-threaded
        // Python (no GIL serialization) don't race on a shared 'result' slot in
        // __main__ globals. Side-effect: Evaluate no longer leaves a 'result' user
        // variable behind for callers that relied on chaining Evaluate -> CaptureVariable("result").
        // For that pattern, callers should use Execute + CaptureVariable explicitly.
        string resultVar = MakeInternalName("eval_result");
        try
        {
            return ExecuteAndCapture($"{resultVar} = {expression}", resultVar);
        }
        finally
        {
            // ExecuteAndCapture cleans up its own JSON sink but leaves the named
            // resultVariable in __main__ globals. For Evaluate that's a per-call
            // unique name, so it would accumulate forever; clean it up explicitly.
            using var gil = new GilLock();
            CleanupTemporaryVariable(resultVar);
        }
    }

    /// <summary>
    /// Checks if a specific global variable exists.
    /// </summary>
    /// <param name="variableName">The name of the variable to check.</param>
    /// <returns>True if the variable exists, false otherwise.</returns>
    public bool VariableExists(string variableName)
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        string flagVar = MakeInternalName("var_exists");
        string checkCode = $@"
{flagVar} = '{EscapePythonString(variableName)}' in globals()
";

        try
        {
            Execute(checkCode);

            using var exists = CaptureVariable(flagVar);
            return exists?.GetBoolean() ?? false;
        }
        finally
        {
            CleanupTemporaryVariable(flagVar);
        }
    }

    /// <summary>
    /// Returns a list of variables that actually exist from a given list of variable names.
    /// </summary>
    /// <param name="variableNames">The variable names to check.</param>
    /// <returns>A list of variable names that actually exist.</returns>
    public IReadOnlyList<string> GetExistingVariables(params string[] variableNames)
    {
        ThrowIfDisposed();

        if (variableNames.Length == 0)
            return [];

        // Validate variable names
        foreach (var varName in variableNames)
        {
            if (!IsValidPythonIdentifier(varName))
                throw new ArgumentException($"'{varName}' is not a valid Python variable name.");
        }

        using var gil = new GilLock();

        // Check all variables at once
        string listVar = MakeInternalName("existing_vars");
        var checkList = string.Join(",", variableNames.Select(v => $"'{EscapePythonString(v)}'"));
        string checkCode = $@"
{listVar} = [v for v in [{checkList}] if v in globals()]
";

        try
        {
            Execute(checkCode);
            using var doc = CaptureVariableInternal(listVar);

            if (doc == null)
                return [];

            var existing = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var value = element.GetString();
                if (value != null)
                {
                    existing.Add(value);
                }
            }

            return existing;
        }
        finally
        {
            CleanupTemporaryVariable(listVar);
        }
    }

    /// <summary>
    /// Captures the value of a specific global variable.
    /// </summary>
    /// <param name="variableName">The name of the variable to capture.</param>
    /// <returns>A <see cref="DotNetPyValue"/> containing the variable's value, or null if the variable does not exist.</returns>
    public DotNetPyValue? CaptureVariable(string variableName)
    {
        var doc = CaptureVariableInternal(variableName);
        if (doc == null)
            return null;

        return new DotNetPyValue(doc);
    }

    /// <summary>
    /// Gets the value of a specific global variable from previously executed code.
    /// </summary>
    /// <param name="variableName">The name of the variable to capture.</param>
    /// <returns>A JsonDocument parsing the variable's value (null if the variable does not exist).</returns>
    /// <exception cref="DotNetPyException">Thrown if an error occurs during variable capture.</exception>
    private JsonDocument? CaptureVariableInternal(string variableName)
    {
        ThrowIfDisposed();

        if (!IsValidPythonIdentifier(variableName))
            throw new ArgumentException($"'{variableName}' is not a valid Python variable name.", nameof(variableName));

        using var gil = new GilLock();

        string sinkVar = MakeInternalName("json_result");

        // Extract variable via JSON serialization
        string captureCode = $@"
import json

if '{EscapePythonString(variableName)}' in locals() or '{EscapePythonString(variableName)}' in globals():
    {sinkVar} = json.dumps({variableName}, ensure_ascii=False, default=str)
else:
    {sinkVar} = '__VARIABLE_NOT_FOUND__'
";

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(captureCode, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while capturing the variable.");
        }

        try
        {
            // Extract the JSON string from the sink variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, sinkVar));
            if (jsonResultObj == null || jsonResultObj.IsInvalid)
            {
                return null;
            }

            string? jsonString = PyObjectToString(jsonResultObj);
            if (string.IsNullOrEmpty(jsonString) || jsonString == "__VARIABLE_NOT_FOUND__")
            {
                return null;
            }

            // Parse JSON to JsonDocument
            try
            {
                return JsonDocument.Parse(jsonString);
            }
            catch (JsonException ex)
            {
                throw new DotNetPyException(
                    $"Could not parse variable '{variableName}' as JSON: {ex.Message}", ex);
            }
        }
        finally
        {
            CleanupTemporaryVariable(sinkVar);
        }
    }

    /// <summary>
    /// Gets the values of multiple global variables at once.
    /// </summary>
    /// <param name="variableNames">The names of the variables to capture.</param>
    /// <returns>A disposable collection of variable names and their values (non-existent variables are null).</returns>
    public DotNetPyDictionary CaptureVariables(params string[] variableNames)
    {
        ThrowIfDisposed();

        if (variableNames.Length == 0)
            return new DotNetPyDictionary(new Dictionary<string, DotNetPyValue?>());

        // Validate variable names
        foreach (var varName in variableNames)
        {
            if (!IsValidPythonIdentifier(varName))
                throw new ArgumentException($"'{varName}' is not a valid Python variable name.");
        }

        using var gil = new GilLock();

        // Per-call unique names so concurrent free-threaded calls don't race.
        string dictVar = MakeInternalName("captured_dict");
        string sinkVar = MakeInternalName("json_result");

        // Capture all variables into a dictionary at once
        var varList = string.Join(", ", variableNames.Select(v =>
            $"'{EscapePythonString(v)}': globals().get('{EscapePythonString(v)}')"));
        string captureCode = $@"
import json
{dictVar} = {{{varList}}}
{sinkVar} = json.dumps({dictVar}, ensure_ascii=False, default=str)
";

        // Resolve this executor's namespace (shared __main__ or an isolated dict).
        IntPtr globals = GetExecutionNamespacePtr();
        IntPtr locals = globals;

        // Execute the code
        using var result = DotNetPyObject.FromNewReference(_pyRunString!(captureCode, Py_file_input, globals, locals));

        if (result == null || result.IsInvalid)
        {
            string? errorMessage = GetPythonError();
            throw new DotNetPyException(
                errorMessage ?? "An error occurred while capturing variables.");
        }

        try
        {
            // Extract the JSON string directly from the sink variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, sinkVar));
            if (jsonResultObj == null || jsonResultObj.IsInvalid)
            {
                return new DotNetPyDictionary(new Dictionary<string, DotNetPyValue?>());
            }

            string? jsonString = PyObjectToString(jsonResultObj);
            if (string.IsNullOrEmpty(jsonString))
            {
                return new DotNetPyDictionary(new Dictionary<string, DotNetPyValue?>());
            }

            // Parse the JSON string into a JsonDocument
            using var jsonDoc = JsonDocument.Parse(jsonString);
            var capturedDict = new Dictionary<string, DotNetPyValue?>();

            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    capturedDict[property.Name] = null;
                }
                else
                {
                    // Parse each value into an individual JsonDocument
                    var valueJson = property.Value.GetRawText();
                    capturedDict[property.Name] = new DotNetPyValue(JsonDocument.Parse(valueJson));
                }
            }

            return new DotNetPyDictionary(capturedDict);
        }
        finally
        {
            CleanupTemporaryVariables(dictVar, sinkVar);
        }
    }

    /// <summary>
    /// Normalizes a C# string into Python code by removing common leading indentation.
    /// </summary>
    private static string NormalizePythonCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        var lines = code.Split('\n');

        // Find the first and last non-empty lines
        int firstNonEmpty = 0;
        int lastNonEmpty = lines.Length - 1;

        while (firstNonEmpty < lines.Length && string.IsNullOrWhiteSpace(lines[firstNonEmpty]))
            firstNonEmpty++;

        while (lastNonEmpty >= 0 && string.IsNullOrWhiteSpace(lines[lastNonEmpty]))
            lastNonEmpty--;

        if (firstNonEmpty > lastNonEmpty)
            return string.Empty;

        // Find the minimum common indentation
        int minIndent = int.MaxValue;
        for (int i = firstNonEmpty; i <= lastNonEmpty; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            int indent = 0;
            foreach (char c in lines[i])
            {
                if (c == ' ' || c == '\t')
                    indent++;
                else
                    break;
            }
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent == int.MaxValue)
            minIndent = 0;

        // Reconstruct efficiently using StringBuilder
        var result = new StringBuilder(code.Length);
        for (int i = firstNonEmpty; i <= lastNonEmpty; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                result.AppendLine();
            }
            else
            {
                // Remove minIndent
                if (lines[i].Length > minIndent)
                {
                    result.AppendLine(lines[i][minIndent..]);
                }
                else
                {
                    result.AppendLine();
                }
            }
        }

        // Remove the last empty line
        if (result.Length > 0 && result[^1] == '\n')
        {
            result.Length--;
            if (result.Length > 0 && result[^1] == '\r')
                result.Length--;
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts a Python object to a string.
    /// </summary>
    /// <param name="obj">The Python object to convert (allows borrowed reference).</param>
    /// <remarks>
    /// This method safely handles borrowed references.
    /// PyUnicodeAsUTF8String returns a new reference even if the input is a borrowed reference.
    /// </remarks>
    private static string? PyObjectToString(DotNetPyObject obj)
    {
        if (obj == null || obj.IsInvalid)
            return null;

        using var bytesObj = DotNetPyObject.FromNewReference(_pyUnicodeAsUTF8String!(obj.DangerousGetHandle()));
        if (bytesObj == null || bytesObj.IsInvalid)
            return null;

        IntPtr strPtr = _pyBytesAsString!(bytesObj.DangerousGetHandle());
        if (strPtr == IntPtr.Zero)
            return null;

        return Marshal.PtrToStringUTF8(strPtr);
    }

    /// <summary>
    /// Captures Python exception information and returns it as a string.
    /// </summary>
    private static string? GetPythonError()
    {
        if (_pyErrOccurred!() == IntPtr.Zero)
            return null;

        _pyErrFetch!(out IntPtr pTypeRaw, out IntPtr pValueRaw, out IntPtr pTracebackRaw); // all new references

        using var pType = DotNetPyObject.FromNewReference(pTypeRaw);
        using var pValue = DotNetPyObject.FromNewReference(pValueRaw);
        using var pTraceback = DotNetPyObject.FromNewReference(pTracebackRaw);

        if ((pType == null || pType.IsInvalid) && (pValue == null || pValue.IsInvalid))
            return "Unknown Python error";

        try
        {
            IntPtr pTypeHandle = pType?.DangerousGetHandle() ?? IntPtr.Zero;
            IntPtr pValueHandle = pValue?.DangerousGetHandle() ?? IntPtr.Zero;
            IntPtr pTracebackHandle = pTraceback?.DangerousGetHandle() ?? IntPtr.Zero;

            _pyErrNormalizeException!(ref pTypeHandle, ref pValueHandle, ref pTracebackHandle);

            using var normalizedPType = DotNetPyObject.FromNewReference(pTypeHandle);
            using var normalizedPValue = DotNetPyObject.FromNewReference(pValueHandle);
            using var normalizedPTraceback = DotNetPyObject.FromNewReference(pTracebackHandle);

            var errorParts = new List<string>();

            if (normalizedPType != null && !normalizedPType.IsInvalid)
            {
                using var typeNameObj = DotNetPyObject.FromNewReference(_pyObjectGetAttrString!(normalizedPType.DangerousGetHandle(), "__name__"));
                if (typeNameObj != null && !typeNameObj.IsInvalid)
                {
                    string? typeName = PyObjectToString(typeNameObj);
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        errorParts.Add($"[{typeName}]");
                    }
                }
            }

            if (normalizedPValue != null && !normalizedPValue.IsInvalid)
            {
                using var valueStrObj = DotNetPyObject.FromNewReference(_pyObjectStr!(normalizedPValue.DangerousGetHandle()));
                if (valueStrObj != null && !valueStrObj.IsInvalid)
                {
                    string? message = PyObjectToString(valueStrObj);
                    if (!string.IsNullOrEmpty(message))
                    {
                        errorParts.Add(message);
                    }
                }
            }

            if (normalizedPTraceback != null && !normalizedPTraceback.IsInvalid)
            {
                try
                {
                    string? tracebackStr = FormatTraceback(normalizedPTraceback);
                    if (!string.IsNullOrEmpty(tracebackStr))
                    {
                        errorParts.Add($"\n{tracebackStr}");
                    }
                }
                catch
                {
                    // Ignore traceback formatting failure
                }
            }

            return errorParts.Count > 0
                ? string.Join(" ", errorParts)
                : "Python error (no details)";
        }
        finally
        {
            // SafeHandle will handle this automatically
        }
    }

    /// <summary>
    /// Formats a traceback object into a string.
    /// </summary>
    private static string? FormatTraceback(DotNetPyObject traceback)
    {
        try
        {
            using var tracebackModule = DotNetPyObject.FromNewReference(_pyImportImportModule!("traceback"));
            if (tracebackModule == null || tracebackModule.IsInvalid)
                return null;

            using var formatTbFunc = DotNetPyObject.FromNewReference(_pyObjectGetAttrString!(tracebackModule.DangerousGetHandle(), "format_tb"));
            if (formatTbFunc == null || formatTbFunc.IsInvalid)
                return null;

            using var resultList = DotNetPyObject.FromNewReference(_pyObjectCallFunctionObjArgs!(formatTbFunc.DangerousGetHandle(), traceback.DangerousGetHandle(), IntPtr.Zero));
            if (resultList == null || resultList.IsInvalid)
                return null;

            using var resultStr = DotNetPyObject.FromNewReference(_pyObjectStr!(resultList.DangerousGetHandle()));
            if (resultStr == null || resultStr.IsInvalid)
                return null;

            return PyObjectToString(resultStr);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a specific global variable.
    /// </summary>
    /// <param name="variableName">The name of the variable to delete.</param>
    /// <returns>True if the variable existed and was deleted, false if it did not exist.</returns>
    public bool DeleteVariable(string variableName)
    {
        ThrowIfDisposed();

        if (!IsValidPythonIdentifier(variableName))
            throw new ArgumentException($"'{variableName}' is not a valid Python variable name.", nameof(variableName));

        using var gil = new GilLock();

        string flagVar = MakeInternalName("var_delete_existed");
        string deleteCode = $@"
{flagVar} = '{EscapePythonString(variableName)}' in globals()
if {flagVar}:
    del {variableName}
";

        try
        {
            Execute(deleteCode);

            // Check the existence flag captured before deletion
            using var existed = CaptureVariable(flagVar);
            return existed?.GetBoolean() ?? false;
        }
        finally
        {
            CleanupTemporaryVariable(flagVar);
        }
    }

    /// <summary>
    /// Deletes multiple global variables at once.
    /// </summary>
    /// <param name="variableNames">The names of the variables to delete.</param>
    /// <returns>A list of variable names that were actually deleted.</returns>
    public IReadOnlyList<string> DeleteVariables(params string[] variableNames)
    {
        ThrowIfDisposed();

        if (variableNames.Length == 0)
            return [];

        // Validate variable names
        foreach (var varName in variableNames)
        {
            if (!IsValidPythonIdentifier(varName))
                throw new ArgumentException($"'{varName}' is not a valid Python variable name.");
        }

        using var gil = new GilLock();

        string listVar = MakeInternalName("deleted_vars");
        var checkList = string.Join(",", variableNames.Select(v => $"'{EscapePythonString(v)}'"));
        string deleteCode = $@"
{listVar} = []
for v in [{checkList}]:
    if v in globals():
        {listVar}.append(v)
        del globals()[v]
";

        try
        {
            Execute(deleteCode);

            using var doc = CaptureVariableInternal(listVar);

            if (doc == null)
                return [];

            var deleted = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var value = element.GetString();
                if (value != null)
                {
                    deleted.Add(value);
                }
            }

            return deleted;
        }
        finally
        {
            CleanupTemporaryVariable(listVar);
        }
    }

    /// <summary>
    /// Clears global variables in the __main__ module (not a complete isolation).
    /// </summary>
    public void ClearGlobals()
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Per-call unique scratch name. The leading underscore in the resulting
        // _dotnetpy_* identifier ensures the comprehension's startswith('_')
        // filter still excludes our scratch list from the deletion set.
        string listVar = MakeInternalName("to_delete");
        Execute($@"
# Delete only user-defined variables (keep built-in objects and modules)
{listVar} = [k for k in list(globals().keys())
              if not k.startswith('_')
              and k not in dir(__builtins__)]
for k in {listVar}:
    del globals()[k]
del {listVar}
");
    }

    /// <summary>
    /// Cleans up a temporary variable in this executor's namespace
    /// (logs an error on failure). MUST be called with the GIL held.
    /// </summary>
    private void CleanupTemporaryVariable(string variableName)
        => CleanupNamespaceVariable(GetExecutionNamespacePtr(), variableName);

    /// <summary>
    /// Cleans up multiple temporary variables in this executor's namespace.
    /// MUST be called with the GIL held.
    /// </summary>
    private void CleanupTemporaryVariables(params string[] variableNames)
    {
        IntPtr ns = GetExecutionNamespacePtr();
        foreach (var varName in variableNames)
        {
            CleanupNamespaceVariable(ns, varName);
        }
    }

    /// <summary>
    /// Deletes a name from the given namespace dict. Errors are swallowed
    /// because cleanup paths run from finally blocks where a leaked scratch
    /// name is preferable to a thrown exception masking the real failure.
    /// </summary>
    private static void CleanupNamespaceVariable(IntPtr ns, string variableName)
    {
        try
        {
            string code = $"del {variableName}";
            IntPtr r = _pyRunString!(code, Py_file_input, ns, ns);
            if (r == IntPtr.Zero)
            {
                _pyErrClear!();
            }
            else
            {
                // Release the +1 reference PyRun_String returns on success.
                DotNetPyObject.FromNewReference(r)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clean up temporary variable '{variableName}': {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DotNetPyExecutor));
    }

    /// <summary>
    /// Releases the executor. For the shared singleton this only decrements
    /// the process-wide reference count and clears globals when the last
    /// reference goes away; the Python runtime itself stays loaded. For an
    /// isolated executor this additionally releases the owned namespace dict.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_isIsolated)
        {
            // Isolated executors don't participate in the shared reference
            // count: each one owns its namespace and releases it independently.
            lock (_instanceLock)
            {
                if (_disposed)
                    return;

                if (_isolatedNamespace != IntPtr.Zero)
                {
                    // Releasing the namespace dict triggers Py_DecRef on every
                    // value it contains, which can run arbitrary __del__ code.
                    // We need to hold the GIL for that.
                    try
                    {
                        using var gil = new GilLock();
                        DotNetPyObject.FromNewReference(_isolatedNamespace)?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to release isolated namespace: {ex.Message}");
                    }
                }

                _disposed = true;
            }
            return;
        }

        lock (_instanceLock)
        {
            if (_disposed)
                return;

            Interlocked.Decrement(ref _referenceCount);

            // Clean up global variables when the last reference is released
            if (_referenceCount == 0)
            {
                try
                {
                    ClearGlobals();
                }
                catch (Exception ex)
                {
                    // Logging (using ILogger is recommended in actual production)
                    Debug.WriteLine($"Failed to clear global variables: {ex.Message}");
                }
            }

            _disposed = true;

            // Note: Py_Finalize() is only safe to call on process exit.
            // The Python runtime is maintained for the lifetime of the process.
        }
    }

    // Thread-local counter for reentrant GIL acquisition
    [ThreadStatic]
    private static int _gilLockCount;
    [ThreadStatic]
    private static IntPtr _gilState;

    /// <summary>
    /// RAII-style struct to manage GIL acquisition/release.
    /// Supports reentrant acquisition on the same thread.
    /// </summary>
    private readonly struct GilLock : IDisposable
    {
        private readonly bool _ownsLock;

        public GilLock()
        {
            if (_gilLockCount == 0)
            {
                _gilState = _pyGILStateEnsure!();
                _ownsLock = true;
            }
            else
            {
                _ownsLock = false;
            }
            _gilLockCount++;
        }

        public void Dispose()
        {
            _gilLockCount--;
            if (_ownsLock && _gilLockCount == 0)
            {
                _pyGILStateRelease!(_gilState);
                _gilState = IntPtr.Zero;
            }
        }
    }
}
