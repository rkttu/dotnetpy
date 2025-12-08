using System.Runtime.InteropServices;
using DotNetPy;

namespace DotNetPy.Native.Shared;

/// <summary>
/// C-compatible exports for native interoperability.
/// These functions can be called from C/C++ or other native languages.
/// </summary>
public static unsafe class NativeExports
{
    #region Initialization

    /// <summary>
    /// Initializes the Python runtime with the specified library path.
    /// </summary>
    /// <param name="libraryPath">Path to the Python shared library (UTF-8 encoded, null-terminated).</param>
    /// <returns>0 on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_initialize")]
    public static int Initialize(byte* libraryPath)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)libraryPath);
            if (string.IsNullOrEmpty(path))
                return -1; // Invalid path

            Python.Initialize(path);
            return 0;
        }
        catch
        {
            return -2; // Initialization failed
        }
    }

    /// <summary>
    /// Initializes the Python runtime using automatic discovery.
    /// </summary>
    /// <returns>0 on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_initialize_auto")]
    public static int InitializeAuto()
    {
        try
        {
            Python.Initialize();
            return 0;
        }
        catch
        {
            return -1; // Discovery or initialization failed
        }
    }

    #endregion

    #region Execution

    /// <summary>
    /// Executes Python code.
    /// </summary>
    /// <param name="code">Python code to execute (UTF-8 encoded, null-terminated).</param>
    /// <returns>0 on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_execute")]
    public static int Execute(byte* code)
    {
        try
        {
            var codeString = Marshal.PtrToStringUTF8((IntPtr)code);
            if (string.IsNullOrEmpty(codeString))
                return -1; // Invalid code

            Python.Execute(codeString);
            return 0;
        }
        catch
        {
            return -2; // Execution failed
        }
    }

    /// <summary>
    /// Evaluates a Python expression and returns the result as a JSON string.
    /// </summary>
    /// <param name="expression">Python expression to evaluate (UTF-8 encoded, null-terminated).</param>
    /// <param name="resultBuffer">Buffer to write the JSON result.</param>
    /// <param name="bufferSize">Size of the result buffer.</param>
    /// <returns>Length of result on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_evaluate")]
    public static int Evaluate(byte* expression, byte* resultBuffer, int bufferSize)
    {
        try
        {
            var expr = Marshal.PtrToStringUTF8((IntPtr)expression);
            if (string.IsNullOrEmpty(expr))
                return -1; // Invalid expression

            using var result = Python.Evaluate(expr);
            if (result == null)
            {
                // Write "null" to buffer
                var nullBytes = "null"u8;
                if (bufferSize < nullBytes.Length + 1)
                    return -3; // Buffer too small

                nullBytes.CopyTo(new Span<byte>(resultBuffer, nullBytes.Length));
                resultBuffer[nullBytes.Length] = 0;
                return nullBytes.Length;
            }

            var json = result.ToJsonString();
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            if (bufferSize < jsonBytes.Length + 1)
                return -3; // Buffer too small

            Marshal.Copy(jsonBytes, 0, (IntPtr)resultBuffer, jsonBytes.Length);
            resultBuffer[jsonBytes.Length] = 0;
            return jsonBytes.Length;
        }
        catch
        {
            return -2; // Evaluation failed
        }
    }

    /// <summary>
    /// Executes Python code and captures the result variable as JSON.
    /// </summary>
    /// <param name="code">Python code to execute (UTF-8 encoded, null-terminated).</param>
    /// <param name="resultBuffer">Buffer to write the JSON result.</param>
    /// <param name="bufferSize">Size of the result buffer.</param>
    /// <returns>Length of result on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_execute_and_capture")]
    public static int ExecuteAndCapture(byte* code, byte* resultBuffer, int bufferSize)
    {
        try
        {
            var codeString = Marshal.PtrToStringUTF8((IntPtr)code);
            if (string.IsNullOrEmpty(codeString))
                return -1; // Invalid code

            using var result = Python.ExecuteAndCapture(codeString);
            if (result == null)
            {
                var nullBytes = "null"u8;
                if (bufferSize < nullBytes.Length + 1)
                    return -3; // Buffer too small

                nullBytes.CopyTo(new Span<byte>(resultBuffer, nullBytes.Length));
                resultBuffer[nullBytes.Length] = 0;
                return nullBytes.Length;
            }

            var json = result.ToJsonString();
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            if (bufferSize < jsonBytes.Length + 1)
                return -3; // Buffer too small

            Marshal.Copy(jsonBytes, 0, (IntPtr)resultBuffer, jsonBytes.Length);
            resultBuffer[jsonBytes.Length] = 0;
            return jsonBytes.Length;
        }
        catch
        {
            return -2; // Execution failed
        }
    }

    #endregion

    #region Variable Management

    /// <summary>
    /// Checks if a variable exists in the Python global scope.
    /// </summary>
    /// <param name="variableName">Variable name (UTF-8 encoded, null-terminated).</param>
    /// <returns>1 if exists, 0 if not exists, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_variable_exists")]
    public static int VariableExists(byte* variableName)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8((IntPtr)variableName);
            if (string.IsNullOrEmpty(name))
                return -1; // Invalid name

            return Python.VariableExists(name) ? 1 : 0;
        }
        catch
        {
            return -2; // Check failed
        }
    }

    /// <summary>
    /// Captures a variable from the Python global scope as JSON.
    /// </summary>
    /// <param name="variableName">Variable name (UTF-8 encoded, null-terminated).</param>
    /// <param name="resultBuffer">Buffer to write the JSON result.</param>
    /// <param name="bufferSize">Size of the result buffer.</param>
    /// <returns>Length of result on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_capture_variable")]
    public static int CaptureVariable(byte* variableName, byte* resultBuffer, int bufferSize)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8((IntPtr)variableName);
            if (string.IsNullOrEmpty(name))
                return -1; // Invalid name

            using var result = Python.CaptureVariable(name);
            if (result == null)
            {
                var nullBytes = "null"u8;
                if (bufferSize < nullBytes.Length + 1)
                    return -3; // Buffer too small

                nullBytes.CopyTo(new Span<byte>(resultBuffer, nullBytes.Length));
                resultBuffer[nullBytes.Length] = 0;
                return nullBytes.Length;
            }

            var json = result.ToJsonString();
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            if (bufferSize < jsonBytes.Length + 1)
                return -3; // Buffer too small

            Marshal.Copy(jsonBytes, 0, (IntPtr)resultBuffer, jsonBytes.Length);
            resultBuffer[jsonBytes.Length] = 0;
            return jsonBytes.Length;
        }
        catch
        {
            return -2; // Capture failed
        }
    }

    /// <summary>
    /// Deletes a variable from the Python global scope.
    /// </summary>
    /// <param name="variableName">Variable name (UTF-8 encoded, null-terminated).</param>
    /// <returns>1 if deleted, 0 if not found, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_delete_variable")]
    public static int DeleteVariable(byte* variableName)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8((IntPtr)variableName);
            if (string.IsNullOrEmpty(name))
                return -1; // Invalid name

            return Python.DeleteVariable(name) ? 1 : 0;
        }
        catch
        {
            return -2; // Delete failed
        }
    }

    /// <summary>
    /// Clears all global variables in the Python session.
    /// </summary>
    /// <returns>0 on success, negative error code on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dotnetpy_clear_globals")]
    public static int ClearGlobals()
    {
        try
        {
            Python.ClearGlobals();
            return 0;
        }
        catch
        {
            return -1; // Clear failed
        }
    }

    #endregion
}
