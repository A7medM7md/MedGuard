using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedGuard.API.Json;

/// <summary>
/// Every DateTime in MedGuard is UTC, but SQL Server's datetime2 stores no zone, so EF
/// materializes them as DateTimeKind.Unspecified and System.Text.Json then writes them
/// WITHOUT a "Z". Browsers read such a string as local time: a reading taken a second
/// ago showed as "2h ago" in Cairo. This writes every DateTime as UTC with a "Z", and
/// turns any incoming offset into UTC before it reaches the domain.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };
        writer.WriteStringValue(utc);
    }
}
