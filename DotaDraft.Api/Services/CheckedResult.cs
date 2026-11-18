using System.Diagnostics.CodeAnalysis;

namespace DotaDraft.Api.Services;

/// <summary>
/// The outcome of a service call whose input is checked against the database: either a value, or the problems with
/// the input keyed by query parameter, ready for a <c>ValidationProblem</c>.
/// </summary>
public sealed class CheckedResult<T>
{
    private CheckedResult(T? value, IReadOnlyDictionary<string, string[]> errors)
    {
        Value = value;
        Errors = errors;
    }

    /// <summary>The value; set only when <see cref="IsValid"/>.</summary>
    public T? Value { get; }

    /// <summary>The problems with the input by query parameter; empty when <see cref="IsValid"/>.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsValid => Errors.Count == 0;

    public static CheckedResult<T> Valid(T value) => new(value, new Dictionary<string, string[]>());

    public static CheckedResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        errors.Count > 0 ? new(default, errors) : throw new ArgumentException("An invalid result needs errors.", nameof(errors));
}
