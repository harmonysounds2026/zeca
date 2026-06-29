using System.Data;
using System.Globalization;

using Dapper;

namespace LAMG.Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper type handler that maps <see cref="DateTimeOffset"/> to a
/// 64-bit Unix-millisecond integer column. All timestamps in the
/// database use this representation for writes.
/// </summary>
/// <remarks>
/// The <see cref="Parse(object)"/> path also tolerates ISO-format
/// strings produced by older builds and by Microsoft.Data.Sqlite's
/// default DateTimeOffset serialization, so a DB that pre-dates this
/// handler (or that was written through a code path where the handler
/// was bypassed) keeps loading instead of throwing.
/// </remarks>
public sealed class DateTimeOffsetUnixMsTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value)
    {
        return TryConvert(value) ?? throw new InvalidCastException(
            $"Cannot parse '{value}' ({value?.GetType().Name}) as a DateTimeOffset.");
    }

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.DbType = DbType.Int64;
        parameter.Value = value.ToUnixTimeMilliseconds();
    }

    internal static DateTimeOffset? TryConvert(object? value)
    {
        if (value is null or DBNull) return null;

        switch (value)
        {
            case long ms:
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);

            case int ms:
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);

            case DateTimeOffset dto:
                return dto;

            case DateTime dt:
                // Microsoft.Data.Sqlite hands us a DateTime when the
                // column is TEXT and the value matches an ISO format.
                return new DateTimeOffset(
                    DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                    TimeSpan.Zero);

            case string s:
                // Numeric string -> treat as Unix ms (legacy good path).
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(parsed);
                }

                // ISO-ish string -> Microsoft.Data.Sqlite's default
                // DateTimeOffset format is "yyyy-MM-dd HH:mm:ss.fffffffzzz".
                // Be permissive about exact form using a roundtrip-friendly
                // parser.
                if (DateTimeOffset.TryParse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset iso))
                {
                    return iso;
                }
                return null;

            default:
                return null;
        }
    }
}

/// <summary>
/// Same mapping, but for nullable <see cref="DateTimeOffset"/> columns.
/// </summary>
public sealed class NullableDateTimeOffsetUnixMsTypeHandler
    : SqlMapper.TypeHandler<DateTimeOffset?>
{
    public override DateTimeOffset? Parse(object value)
        => DateTimeOffsetUnixMsTypeHandler.TryConvert(value);

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
    {
        if (value is null)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = value.Value.ToUnixTimeMilliseconds();
        }
    }
}
