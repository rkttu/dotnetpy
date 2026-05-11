using System.Runtime.InteropServices;

namespace DotNetPy;

/// <summary>
/// Represents a wrapper for a Python object pointer (PyObject*).
/// This class automatically manages the reference counting of the Python object
/// by inheriting from SafeHandle.
/// </summary>
internal sealed class DotNetPyObject : SafeHandle
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyDecRefDelegate(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyIncRefDelegate(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PyGILStateEnsureDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PyGILStateReleaseDelegate(IntPtr state);

    private static PyDecRefDelegate? _pyDecRef;
    private static PyIncRefDelegate? _pyIncRef;
    private static PyGILStateEnsureDelegate? _pyGILStateEnsure;
    private static PyGILStateReleaseDelegate? _pyGILStateRelease;

    /// <summary>
    /// Initializes the reference counting functions from the Python library.
    /// </summary>
    /// <param name="libraryHandle">The handle to the loaded Python library.</param>
    internal static void Initialize(IntPtr libraryHandle)
    {
        _pyDecRef = NativeMethods.LoadFunction<PyDecRefDelegate>(libraryHandle, "Py_DecRef");
        _pyIncRef = NativeMethods.LoadFunction<PyIncRefDelegate>(libraryHandle, "Py_IncRef");
        _pyGILStateEnsure = NativeMethods.LoadFunction<PyGILStateEnsureDelegate>(libraryHandle, "PyGILState_Ensure");
        _pyGILStateRelease = NativeMethods.LoadFunction<PyGILStateReleaseDelegate>(libraryHandle, "PyGILState_Release");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetPyObject"/> class.
    /// The handle is considered invalid until SetHandle is called.
    /// </summary>
    private DotNetPyObject()
        : base(IntPtr.Zero, true)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid =>
        handle == IntPtr.Zero;

    /// <summary>
    /// Creates a new PythonObject that wraps the given handle.
    /// This is a factory method to ensure that the handle is valid.
    /// </summary>
    /// <param name="handle">The Python object pointer to wrap.</param>
    /// <returns>A new PythonObject instance, or null if the handle is invalid.</returns>
    public static DotNetPyObject? FromNewReference(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        var obj = new DotNetPyObject();
        obj.SetHandle(handle);
        return obj;
    }

    /// <summary>
    /// Creates a new PythonObject from a borrowed reference.
    /// The reference count of the handle is incremented.
    /// </summary>
    /// <param name="handle">The borrowed Python object pointer.</param>
    /// <returns>A new PythonObject instance, or null if the handle is invalid.</returns>
    public static DotNetPyObject? FromBorrowedReference(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        _pyIncRef!(handle);
        var obj = new DotNetPyObject();
        obj.SetHandle(handle);
        return obj;
    }

    /// <summary>
    /// Executes the code required to free the handle.
    /// This method is called by the runtime when the object is finalized.
    /// It decrements the Python object's reference count.
    /// </summary>
    /// <remarks>
    /// SafeHandle finalizers run on the .NET finalizer thread, which is NOT attached
    /// to the Python interpreter and does NOT hold the GIL. Calling Py_DecRef in that
    /// state is unsafe under classic GIL builds (a refcount drop to 0 fires Python
    /// __del__ code in an unattached thread) and remains unsafe under free-threaded
    /// builds (Py_DecRef itself is atomic in PEP 703 but __del__ still requires an
    /// attached thread state). Acquire the GIL via PyGILState_Ensure first; on
    /// free-threaded builds this still attaches a valid thread state cheaply.
    /// </remarks>
    /// <returns>true if the handle is released successfully; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (IsInvalid || _pyDecRef is null)
            return true;

        // If GIL helpers are unavailable (e.g. extremely early shutdown or a partial
        // initialization), fall back to a bare Py_DecRef — it is no worse than the
        // pre-fix behaviour and avoids leaking the handle entirely.
        var ensure = _pyGILStateEnsure;
        var release = _pyGILStateRelease;
        if (ensure is null || release is null)
        {
            _pyDecRef(handle);
            return true;
        }

        IntPtr gilState = ensure();
        try
        {
            _pyDecRef(handle);
        }
        finally
        {
            release(gilState);
        }
        return true;
    }
}
