using MedGuard.Domain.Enums;

namespace MedGuard.Application.DTOs;

public record CreateShipmentRequest(
    Guid BatchId,
    string OriginLocation,
    string DestinationLocation,
    string? CourierName
);

public record ShipmentDto(
    Guid Id,
    Guid BatchId,
    string OriginLocation,
    string DestinationLocation,
    string? CourierName,
    ShipmentStatus Status,
    DateTime? DepartedAtUtc,
    DateTime? ArrivedAtUtc
);
