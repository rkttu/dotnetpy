namespace DotNetPy;

/// <summary>
/// Exception that occurred during Python execution.
/// </summary>
public sealed class DotNetPyException : Exception
{
    public DotNetPyException(string message)
        : base(message)
    { }

    public DotNetPyException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
