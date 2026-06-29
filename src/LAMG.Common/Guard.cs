using System.Runtime.CompilerServices;

namespace LAMG.Common;

/// <summary>
/// Argument validation helpers. Each method returns the validated value
/// so it can be used inline in field assignments, e.g.
/// <c>_path = Guard.NotNullOrWhiteSpace(path);</c>.
/// </summary>
public static class Guard
{
    public static T NotNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        return value;
    }

    public static string NotNullOrEmpty(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        }

        return value;
    }

    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value;
    }

    public static int NotNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be >= 0.");
        }

        return value;
    }

    public static long NotNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be >= 0.");
        }

        return value;
    }

    public static int Positive(
        int value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be > 0.");
        }

        return value;
    }

    public static long Positive(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be > 0.");
        }

        return value;
    }

    public static int InRange(
        int value,
        int minInclusive,
        int maxInclusive,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < minInclusive || value > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be in [{minInclusive}, {maxInclusive}].");
        }

        return value;
    }
}
