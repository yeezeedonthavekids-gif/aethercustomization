namespace Aether.Core.Diagnostics;

/// <summary>Outcome of a user-initiated operation. Never thrown away silently: the UI surfaces Message/Errors.</summary>
public sealed record OperationResult(bool Success, string Message, IReadOnlyList<string> Errors)
{
    public static OperationResult Ok(string message = "Done.") => new(true, message, []);
    public static OperationResult Fail(string message, params string[] errors) => new(false, message, errors);
    public static OperationResult Fail(string message, IEnumerable<string> errors) => new(false, message, errors.ToList());

    public string Describe() => Errors.Count == 0 ? Message : $"{Message}\n• {string.Join("\n• ", Errors)}";
}

public sealed record OperationResult<T>(bool Success, string Message, IReadOnlyList<string> Errors, T? Value)
{
    public static OperationResult<T> Ok(T value, string message = "Done.") => new(true, message, [], value);
    public static OperationResult<T> Fail(string message, params string[] errors) => new(false, message, errors, default);
    public OperationResult ToResult() => new(Success, Message, Errors);
}
