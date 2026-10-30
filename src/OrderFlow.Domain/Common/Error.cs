namespace OrderFlow.Domain.Common;

/// <summary>Classifies an expected failure so callers can react without parsing codes.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
}

/// <summary>
/// An expected, business-level failure. Exceptions are reserved for bugs and infrastructure
/// faults; anything a caller should handle is returned as an <see cref="Error"/> instead.
/// </summary>
/// <param name="Code">Stable, machine-readable identifier such as <c>order.not_found</c>.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Kind">How the failure should be treated.</param>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
}
