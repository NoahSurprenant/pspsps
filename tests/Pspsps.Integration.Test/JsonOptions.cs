using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pspsps.Integration.Test;

internal static class JsonOptions
{
    /// <summary>What the server sends: web defaults with enums as names.</summary>
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
