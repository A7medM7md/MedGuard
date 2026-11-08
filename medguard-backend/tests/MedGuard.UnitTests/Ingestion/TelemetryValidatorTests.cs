using System.Text;
using System.Text.Json;
using MedGuard.Contracts.Devices;
using MedGuard.IngestionBridge;

namespace MedGuard.UnitTests.Ingestion;

/// <summary>The bridge's gate (ADR 0004): what's rejected here goes to the DLQ instead of Kafka.</summary>
public class TelemetryValidatorTests
{
    private static readonly string Topic = MqttTopics.Telemetry("DEV-1");

    private static byte[] Payload(object o) => JsonSerializer.SerializeToUtf8Bytes(o, ContractJson.Options);

    private static DeviceTelemetry Valid(string device = "DEV-1", decimal temp = 5m) =>
        new(Guid.NewGuid(), device, Guid.NewGuid(), temp, 50m, 30.0, 31.0, DateTime.UtcNow);

    [Fact]
    public void Valid_reading_passes()
    {
        var (telemetry, error) = TelemetryValidator.Validate(Topic, Payload(Valid()));

        Assert.NotNull(telemetry);
        Assert.Null(error);
    }

    [Fact]
    public void Device_cannot_publish_as_another_device()
    {
        var (telemetry, error) = TelemetryValidator.Validate(Topic, Payload(Valid(device: "DEV-2")));

        Assert.Null(telemetry);
        Assert.Contains("does not match", error);
    }

    [Theory]
    [InlineData(-150)]
    [InlineData(900)]
    public void Physically_impossible_temperature_is_a_sensor_fault(double temp)
    {
        var (telemetry, error) = TelemetryValidator.Validate(Topic, Payload(Valid(temp: (decimal)temp)));

        Assert.Null(telemetry);
        Assert.Contains("out of sensor range", error);
    }

    [Fact]
    public void Missing_ids_are_rejected()
    {
        var noId = Valid() with { MessageId = Guid.Empty };
        var noBatch = Valid() with { BatchId = Guid.Empty };

        Assert.Contains("messageId", TelemetryValidator.Validate(Topic, Payload(noId)).Error);
        Assert.Contains("batchId", TelemetryValidator.Validate(Topic, Payload(noBatch)).Error);
    }

    [Fact]
    public void Garbage_and_wrong_topics_are_rejected()
    {
        Assert.Contains("invalid JSON", TelemetryValidator.Validate(Topic, Encoding.UTF8.GetBytes("{not json")).Error);
        Assert.Contains("unexpected topic", TelemetryValidator.Validate("some/other/topic", Payload(Valid())).Error);
    }
}
