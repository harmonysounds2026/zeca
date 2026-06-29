using System.Data;

using Dapper;

namespace LAMG.Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper type handler that maps <see cref="DateTimeOffset"/> to a
/// 64-bit Unix-millisecond integer column. All timestamps in the
/// database use this representation.
/// </summary>
public sealed class DateTimeOffsetUnixMsTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value)
    {
        return value switch
        {
            long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms),
            int ms => DateTimeOffset.FromUnixTimeMilliseconds(ms),
            string s when long.TryParse(s, out long parsed)
                => DateTimeOffset.FromUnixTimeMilliseconds(parsed),
            _ => throw new InvalidCastException(
                $"Cannot parse '{value}' ({value?.GetType().Name}) as a Unix-ms DateTimeOffset."),
        };
    }

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.DbType = DbType.Int64;
        parameter.Value = value.ToUnixTimeMilliseconds();
    }
}

/// <summary>
/// Same mapping, but for nullable <see cref="DateTimeOffset"/> columns.
/// </summary>
public sealed class NullableDateTimeOffsetUnixMsTypeHandler
    : SqlMapper.TypeHandler<DateTimeOffset?>
{
    public override DateTimeOffset? Parse(object value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms),
            int ms => DateTimeOffset.FromUnixTimeMilliseconds(ms),
            string s when long.TryParse(s, out long parsed)
                => DateTimeOffset.FromUnixTimeMilliseconds(parsed),
            _ => throw new InvalidCastException(
                $"Cannot parse '{value}' ({value.GetType().Name}) as a Unix-ms DateTimeOffset."),
        };
    }

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
