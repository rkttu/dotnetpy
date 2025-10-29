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
    private static readonly object _instanceLock = new object();
    private static volatile DotNetPyExecutor? _instance = null;
    private static int _referenceCount = 0;

    private static readonly object _initLock = new object();
    private static volatile bool _initialized = false;
    private static string? _initializedLibraryPath = null;
    private static IntPtr _libraryHandle = IntPtr.Zero;
    private volatile bool _disposed = false;

    // Python C API 함수 포인터 델리게이트
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

    // 함수 포인터 인스턴스
    private static PyInitializeDelegate? _pyInitialize;
    private static PyFinalizeDelegate? _pyFinalize;
    private static PyIsInitializedDelegate? _pyIsInitialized;
    private static PyGILStateEnsureDelegate? _pyGILStateEnsure;
    private static PyGILStateReleaseDelegate? _pyGILStateRelease;
    private static PyRunSimpleStringDelegate? _pyRunSimpleString;
    private static PyRunStringDelegate? _pyRunString;
    private static PyImportAddModuleDelegate? _pyImportAddModule;
    private static PyModuleGetDictDelegate? _pyModuleGetDict;
    private static PyDictNewDelegate? _pyDictNew;
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
    /// Private constructor - instance can only be created through GetInstance().
    /// </summary>
    private DotNetPyExecutor(string? libraryPath)
    {
        EnsureInitialized(libraryPath);
    }

    /// <summary>
    /// Gets the singleton instance of the DotNetPyExecutor.
    /// Only one instance exists per process.
    /// </summary>
    /// <param name="libraryPath">The path to the Python library (only used on the first call).</param>
    /// <returns>The DotNetPyExecutor instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if already initialized with a different path.</exception>
    public static DotNetPyExecutor GetInstance(string? libraryPath = null)
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
                _instance = new DotNetPyExecutor(libraryPath);
                _referenceCount = 1;
                return _instance;
            }

            // Validate if already initialized with a different path.
            if (libraryPath != null && _initializedLibraryPath != null &&
                !string.Equals(libraryPath, _initializedLibraryPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
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
    /// Initializes the Python interpreter (once per process).
    /// </summary>
    private static void EnsureInitialized(string? libraryPath)
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

            // Initialize Python
            _pyInitialize!();
            _initialized = true;
        }
    }

    /// <summary>
    /// Loads the Python shared library and initializes function pointers.
    /// </summary>
    private static void LoadPythonLibrary(string? libraryPath)
    {
        if (_libraryHandle != IntPtr.Zero)
            return;

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

        // Load function pointers
        try
        {
            _pyInitialize = NativeMethods.LoadFunction<PyInitializeDelegate>(_libraryHandle, "Py_Initialize");
            _pyFinalize = NativeMethods.LoadFunction<PyFinalizeDelegate>(_libraryHandle, "Py_Finalize");
            _pyIsInitialized = NativeMethods.LoadFunction<PyIsInitializedDelegate>(_libraryHandle, "Py_IsInitialized");
            _pyGILStateEnsure = NativeMethods.LoadFunction<PyGILStateEnsureDelegate>(_libraryHandle, "PyGILState_Ensure");
            _pyGILStateRelease = NativeMethods.LoadFunction<PyGILStateReleaseDelegate>(_libraryHandle, "PyGILState_Release");
            _pyRunSimpleString = NativeMethods.LoadFunction<PyRunSimpleStringDelegate>(_libraryHandle, "PyRun_SimpleString");
            _pyRunString = NativeMethods.LoadFunction<PyRunStringDelegate>(_libraryHandle, "PyRun_String");
            _pyImportAddModule = NativeMethods.LoadFunction<PyImportAddModuleDelegate>(_libraryHandle, "PyImport_AddModule");
            _pyModuleGetDict = NativeMethods.LoadFunction<PyModuleGetDictDelegate>(_libraryHandle, "PyModule_GetDict");
            _pyDictNew = NativeMethods.LoadFunction<PyDictNewDelegate>(_libraryHandle, "PyDict_New");
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

        // Use Python's str.isidentifier() and keyword.iskeyword()
        string validationCode = $@"
import keyword
_is_valid = '{EscapePythonString(name)}'.isidentifier() and not keyword.iskeyword('{EscapePythonString(name)}')
";

        try
        {
            int result = _pyRunSimpleString!(validationCode);
            if (result != 0)
            {
                _pyErrClear!();
                return false;
            }

            // Get the globals dictionary of the __main__ module
            using var mainModule = DotNetPyObject.FromBorrowedReference(_pyImportAddModule!("__main__"));
            if (mainModule == null || mainModule.IsInvalid)
                return false;

            using var globals = DotNetPyObject.FromBorrowedReference(_pyModuleGetDict!(mainModule.DangerousGetHandle()));
            if (globals == null || globals.IsInvalid)
                return false;

            // Get the _is_valid variable (borrowed reference)
            using var isValidObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals.DangerousGetHandle(), "_is_valid"));
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
            // Clean up the temporary variable
            try
            {
                _pyRunSimpleString!("del _is_valid");
            }
            catch
            {
                // Ignore cleanup failure
            }
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
    public void Execute(string code)
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

        int result = _pyRunSimpleString!(code);

        if (result != 0)
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

        int result = _pyRunSimpleString!(fullCode);

        if (result != 0)
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

            case byte or sbyte or short or ushort or int or uint or long or ulong:
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

            case System.Collections.IDictionary dict:
                writer.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    writer.WritePropertyName(entry.Key.ToString() ?? string.Empty);
                    WriteJsonValue(writer, entry.Value);
                }
                writer.WriteEndObject();
                break;

            case System.Collections.IEnumerable enumerable when value is not string:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    WriteJsonValue(writer, item);
                }
                writer.WriteEndArray();
                break;

            default:
                // 익명 타입 및 일반 객체를 리플렉션으로 직렬화
                var type = value.GetType();
                if (type.Namespace == null || type.Name.Contains("AnonymousType"))
                {
                    // 익명 타입 처리
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
                    // 일반 객체를 리플렉션으로 직렬화
                    writer.WriteStartObject();
                    foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
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
    public DotNetPyValue? ExecuteAndCapture(string code, string resultVariable = "result")
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

        // Extract result via JSON serialization
        string wrapperCode = $@"
import json

# Execute user code
{code}

# Serialize the result to JSON
if '{resultVariable}' in locals() or '{resultVariable}' in globals():
    _json_result = json.dumps({resultVariable}, ensure_ascii=False, default=str)
else:
    _json_result = 'null'
";

        // Get the globals dictionary of the __main__ module
        using var mainModule = DotNetPyObject.FromBorrowedReference(_pyImportAddModule!("__main__"));
        if (mainModule == null || mainModule.IsInvalid)
        {
            throw new DotNetPyException("Could not get the __main__ module.");
        }

        IntPtr globals = _pyModuleGetDict!(mainModule.DangerousGetHandle()); // borrowed reference
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
            // Extract the JSON string from the _json_result variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, "_json_result"));
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
            CleanupTemporaryVariable("_json_result");
        }
    }

    /// <summary>
    /// Serializes .NET data to JSON, injects it as Python variables, and returns the result.
    /// </summary>
    /// <param name="code">The Python code to execute.</param>
    /// <param name="variables">The variables to inject into Python (name: value).</param>
    /// <param name="resultVariable">The name of the Python variable containing the result (default: "result").</param>
    /// <returns>A PyValue parsing the result of the Python script.</returns>
    public DotNetPyValue? ExecuteAndCapture(
        string code,
        Dictionary<string, object?> variables,
        string resultVariable = "result")
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        // Normalize indentation
        code = NormalizePythonCode(code);

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
    _json_result = json.dumps({resultVariable}, ensure_ascii=False, default=str)
else:
    _json_result = 'null'
";

        // Get the globals dictionary of the __main__ module
        using var mainModule = DotNetPyObject.FromBorrowedReference(_pyImportAddModule!("__main__"));
        if (mainModule == null || mainModule.IsInvalid)
        {
            throw new DotNetPyException("Could not get the __main__ module.");
        }

        IntPtr globals = _pyModuleGetDict!(mainModule.DangerousGetHandle()); // borrowed reference
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
            // Extract the JSON string from the _json_result variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, "_json_result"));
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
            CleanupTemporaryVariable("_json_result");
        }
    }

    /// <summary>
    /// Evaluates a Python expression and returns the result as a PyValue.
    /// </summary>
    /// <param name="expression">The Python expression to evaluate (e.g., "1+1", "[1,2,3]").</param>
    /// <returns>A PyValue parsing the result of the expression.</returns>
    public DotNetPyValue? Evaluate(string expression)
    {
        // Assign the expression to a result variable and execute
        return ExecuteAndCapture($"result = {expression}");
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

        string checkCode = $@"
_var_exists_check = '{EscapePythonString(variableName)}' in globals()
";

        try
        {
            Execute(checkCode);

            using var exists = CaptureVariable("_var_exists_check");
            return exists?.GetBoolean() ?? false;
        }
        finally
        {
            CleanupTemporaryVariable("_var_exists_check");
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
            return Array.Empty<string>();

        // Validate variable names
        foreach (var varName in variableNames)
        {
            if (!IsValidPythonIdentifier(varName))
                throw new ArgumentException($"'{varName}' is not a valid Python variable name.");
        }

        using var gil = new GilLock();

        // Check all variables at once
        var checkList = string.Join(",", variableNames.Select(v => $"'{EscapePythonString(v)}'"));
        string checkCode = $@"
_existing_vars = [v for v in [{checkList}] if v in globals()]
";

        try
        {
            Execute(checkCode);
            using var doc = CaptureVariableInternal("_existing_vars");

            if (doc == null)
                return Array.Empty<string>();

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
            CleanupTemporaryVariable("_existing_vars");
        }
    }

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

        // Extract variable via JSON serialization
        string captureCode = $@"
import json

if '{EscapePythonString(variableName)}' in locals() or '{EscapePythonString(variableName)}' in globals():
    _json_result = json.dumps({variableName}, ensure_ascii=False, default=str)
else:
    _json_result = '__VARIABLE_NOT_FOUND__'
";

        // Get the globals dictionary of the __main__ module
        using var mainModule = DotNetPyObject.FromBorrowedReference(_pyImportAddModule!("__main__"));
        if (mainModule == null || mainModule.IsInvalid)
        {
            throw new DotNetPyException("Could not get the __main__ module.");
        }

        IntPtr globals = _pyModuleGetDict!(mainModule.DangerousGetHandle()); // borrowed reference
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
            // Extract the JSON string from the _json_result variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, "_json_result"));
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
            CleanupTemporaryVariable("_json_result");
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

        // Capture all variables into a dictionary at once
        var varList = string.Join(", ", variableNames.Select(v =>
            $"'{EscapePythonString(v)}': globals().get('{EscapePythonString(v)}')"));
        string captureCode = $@"
import json
_captured_dict = {{{varList}}}
_json_result = json.dumps(_captured_dict, ensure_ascii=False, default=str)
";

        // Get the globals dictionary of the __main__ module
        using var mainModule = DotNetPyObject.FromBorrowedReference(_pyImportAddModule!("__main__"));
        if (mainModule == null || mainModule.IsInvalid)
        {
            throw new DotNetPyException("Could not get the __main__ module.");
        }

        IntPtr globals = _pyModuleGetDict!(mainModule.DangerousGetHandle()); // borrowed reference
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
            // Extract the JSON string directly from the _json_result variable (borrowed reference)
            using var jsonResultObj = DotNetPyObject.FromBorrowedReference(_pyDictGetItemString!(globals, "_json_result"));
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
            CleanupTemporaryVariables("_captured_dict", "_json_result");
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
                    result.AppendLine(lines[i].Substring(minIndent));
                }
                else
                {
                    result.AppendLine();
                }
            }
        }

        // Remove the last empty line
        if (result.Length > 0 && result[result.Length - 1] == '\n')
        {
            result.Length--;
            if (result.Length > 0 && result[result.Length - 1] == '\r')
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
    private string? PyObjectToString(IntPtr obj)
    {
        if (obj == IntPtr.Zero)
            return null;

        using var bytesObj = DotNetPyObject.FromNewReference(_pyUnicodeAsUTF8String!(obj));
        if (bytesObj == null || bytesObj.IsInvalid)
            return null;

        IntPtr strPtr = _pyBytesAsString!(bytesObj.DangerousGetHandle());
        if (strPtr == IntPtr.Zero)
            return null;

        return Marshal.PtrToStringUTF8(strPtr);
    }

    /// <summary>
    /// Converts a Python object to a string.
    /// </summary>
    /// <param name="obj">The Python object to convert (allows borrowed reference).</param>
    /// <remarks>
    /// This method safely handles borrowed references.
    /// PyUnicodeAsUTF8String returns a new reference even if the input is a borrowed reference.
    /// </remarks>
    private string? PyObjectToString(DotNetPyObject obj)
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
    private string? GetPythonError()
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
    private string? FormatTraceback(DotNetPyObject traceback)
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

        string deleteCode = $@"
_var_delete_existed = '{EscapePythonString(variableName)}' in globals()
if _var_delete_existed:
    del {variableName}
";

        try
        {
            Execute(deleteCode);

            // Check the _var_delete_existed variable
            using var existed = CaptureVariable("_var_delete_existed");
            return existed?.GetBoolean() ?? false;
        }
        finally
        {
            CleanupTemporaryVariable("_var_delete_existed");
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
            return Array.Empty<string>();

        // Validate variable names
        foreach (var varName in variableNames)
        {
            if (!IsValidPythonIdentifier(varName))
                throw new ArgumentException($"'{varName}' is not a valid Python variable name.");
        }

        using var gil = new GilLock();

        var checkList = string.Join(",", variableNames.Select(v => $"'{EscapePythonString(v)}'"));
        string deleteCode = $@"
_deleted_vars = []
for v in [{checkList}]:
    if v in globals():
        _deleted_vars.append(v)
        del globals()[v]
";

        try
        {
            Execute(deleteCode);
            using var doc = CaptureVariableInternal("_deleted_vars");

            if (doc == null)
                return Array.Empty<string>();

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
            CleanupTemporaryVariable("_deleted_vars");
        }
    }

    /// <summary>
    /// Clears global variables in the __main__ module (not a complete isolation).
    /// </summary>
    public void ClearGlobals()
    {
        ThrowIfDisposed();

        using var gil = new GilLock();

        Execute(@"
# Delete only user-defined variables (keep built-in objects and modules)
_to_delete = [k for k in list(globals().keys()) 
              if not k.startswith('_') 
              and k not in dir(__builtins__)]
for k in _to_delete:
    del globals()[k]
del _to_delete
");
    }

    /// <summary>
    /// Cleans up a temporary variable (logs an error on failure).
    /// </summary>
    private void CleanupTemporaryVariable(string variableName)
    {
        try
        {
            _pyRunSimpleString!($"del {variableName}");
        }
        catch (Exception ex)
        {
            // Logging (using ILogger is recommended in actual production)
            System.Diagnostics.Debug.WriteLine($"Failed to clean up temporary variable '{variableName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up multiple temporary variables.
    /// </summary>
    private void CleanupTemporaryVariables(params string[] variableNames)
    {
        foreach (var varName in variableNames)
        {
            CleanupTemporaryVariable(varName);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DotNetPyExecutor));
    }

    /// <summary>
    /// Releases the reference.
    /// This method only performs reference counting, and cleans up global variables when the last reference is released.
    /// The Python runtime itself is maintained until the process terminates.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_instanceLock)
        {
            if (_disposed)
                return;

            Interlocked.Decrement(ref _referenceCount);

            // 마지막 참조가 해제되면 전역 변수 정리
            if (_referenceCount == 0)
            {
                try
                {
                    ClearGlobals();
                }
                catch (Exception ex)
                {
                    // Logging (using ILogger is recommended in actual production)
                    System.Diagnostics.Debug.WriteLine($"Failed to clear global variables: {ex.Message}");
                }
            }

            _disposed = true;

            // Note: Py_Finalize() is only safe to call on process exit.
            // The Python runtime is maintained for the lifetime of the process.
        }
    }

    /// <summary>
    /// RAII-style struct to manage GIL acquisition/release.
    /// </summary>
    private readonly struct GilLock : IDisposable
    {
        private readonly IntPtr _state;

        public GilLock()
        {
            _state = _pyGILStateEnsure!();
        }

        public void Dispose()
        {
            _pyGILStateRelease!(_state);
        }
    }
}
