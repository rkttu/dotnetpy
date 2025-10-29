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

    private static PyDecRefDelegate? _pyDecRef;
    private static PyIncRefDelegate? _pyIncRef;

    /// <summary>
    /// Initializes the reference counting functions from the Python library.
    /// </summary>
    /// <param name="libraryHandle">The handle to the loaded Python library.</param>
    internal static void Initialize(IntPtr libraryHandle)
    {
        _pyDecRef = NativeMethods.LoadFunction<PyDecRefDelegate>(libraryHandle, "Py_DecRef");
        _pyIncRef = NativeMethods.LoadFunction<PyIncRefDelegate>(libraryHandle, "Py_IncRef");
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
    /// <returns>true if the handle is released successfully; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
            _pyDecRef!(handle);

        return true;
    }
}
