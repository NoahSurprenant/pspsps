using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pspsps.Domain.Rooms;

namespace Pspsps.Infrastructure.Persistence;

/// <summary>How a room's layout, and an overlay's props, are written into a <c>jsonb</c> column.</summary>
internal static class RoomJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ValueConverter<T?, string> NullableConverter<T>() where T : class =>
        new(value => JsonSerializer.Serialize(value, Options), json => JsonSerializer.Deserialize<T>(json, Options));

    public static ValueComparer<T?> NullableComparer<T>() where T : class =>
        new(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
            value => value);

    public static ValueConverter<T, string> Converter<T>() where T : class =>
        new(value => JsonSerializer.Serialize(value, Options), json => JsonSerializer.Deserialize<T>(json, Options)!);

    /// <summary>Equal when written the same: a value is replaced, never changed in place.</summary>
    public static ValueComparer<T> Comparer<T>() where T : class =>
        new(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
            value => value);
}
