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
);

public record BatchDetailDto(
    BatchDto Batch,
    IReadOnlyList<SensorReadingDto> RecentReadings,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<ShipmentDto> Shipments
);
