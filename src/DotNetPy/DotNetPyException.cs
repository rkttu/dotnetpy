namespace DotNetPy;

/// <summary>
/// Exception that occurred during Python execution.
/// </summary>
public sealed class DotNetPyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetPyException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public DotNetPyException(string message)
        : base(message)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetPyException"/> class with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DotNetPyException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
