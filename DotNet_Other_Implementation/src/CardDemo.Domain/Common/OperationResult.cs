namespace CardDemo.Domain.Common;

/// <summary>
/// Simple success/failure result carrying a human-readable message, mirroring the
/// single-error-message discipline of the BMS screens (one WS-MESSAGE at a time).
/// </summary>
public readonly record struct OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

/// <summary>Result carrying a value on success.</summary>
public readonly record struct OperationResult<T>(bool Success, string Message, T? Value)
{
    public static OperationResult<T> Ok(T value, string message = "") => new(true, message, value);
    public static OperationResult<T> Fail(string message) => new(false, message, default);
}
