using System.Data;

using Dapper;

namespace LAMG.Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper handler that stores enums as their member name in a
/// <c>TEXT</c> column. Makes the database self-describing at the cost
/// of a few bytes per row.
/// </summary>
public sealed class EnumStringTypeHandler<T> : SqlMapper.TypeHandler<T>
    where T : struct, Enum
{
    public override T Parse(object value)
    {
        string text = value switch
        {
            string s => s,
            null => throw new InvalidCastException(
                $"Cannot parse NULL into enum {typeof(T).Name}."),
            _ => value.ToString()
                 ?? throw new InvalidCastException(
                     $"Value of type {value.GetType().Name} could not be converted to string."),
        };

        return Enum.Parse<T>(text, ignoreCase: true);
    }

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString();
    }
}
