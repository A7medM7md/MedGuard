using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record CreateBatchRequest(
    string BatchNumber,
    string DrugName,
    string ManufacturerName,
    int QuantityUnits,
    DateTime ManufacturedDateUtc,
    DateTime ExpiryDateUtc,
    decimal MinSafeTemperatureC,
    decimal MaxSafeTemperatureC
);

public record BatchDto(
    Guid Id,
    string BatchNumber,
    string DrugName,
    string ManufacturerName,
    int QuantityUnits,
    DateTime ManufacturedDateUtc,
    DateTime ExpiryDateUtc,
    decimal MinSafeTemperatureC,
    decimal MaxSafeTemperatureC,
    BatchStatus Status
)
{
    /// <summary>
    /// The batch's most recent reading. Filled by the list endpoint only, so a
    /// table of batches can show each one's current temperature without a
    /// detail call per row. Null when the batch has no readings yet.
    /// </summary>
    public SensorReadingDto? LastReading { get; init; }
}

public record BatchDetailDto(
    BatchDto Batch,
    IReadOnlyList<SensorReadingDto> RecentReadings,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<ShipmentDto> Shipments
);
