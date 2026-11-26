using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.Services;

/// <summary>
/// Backup timestamps are UTC, like everything the app stores. Written with a "Z" so the
/// file says so; read back as UTC whatever the file carries:
/// <list type="bullet">
/// <item>"…Z" — already UTC.</item>
/// <item>"…+03:00" (a hand-edited file or another tool) — converted to UTC. Left alone,
/// System.Text.Json would turn it into local time and the app would store a shifted value.</item>
/// <item>no marker (backups written by earlier versions) — taken as UTC, which is what those versions wrote.</item>
/// </list>
/// </summary>
internal sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc); // stored values carry no kind but are UTC
        writer.WriteStringValue(utc);
    }
}
