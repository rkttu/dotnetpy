using System.Runtime.InteropServices;

namespace DotNetPy;

/// <summary>
/// Provides helper methods for native library interoperability.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Loads a function from a native library.
    /// </summary>
    /// <typeparam name="TDelegate">The type of the delegate representing the function's signature.</typeparam>
    /// <param name="libraryHandle">The handle to the native library.</param>
    /// <param name="functionName">The name of the function to load.</param>
    /// <returns>A delegate that can be used to invoke the native function.</returns>
    /// <exception cref="DotNetPyException">Thrown when the function is not found in the native library.</exception>
    public static TDelegate LoadFunction<TDelegate>(IntPtr libraryHandle, string functionName) where TDelegate : Delegate
    {
        if (!NativeLibrary.TryGetExport(libraryHandle, functionName, out IntPtr funcPtr))
            throw new DotNetPyException($"Function not found: {functionName}");

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(funcPtr);
    }
}
