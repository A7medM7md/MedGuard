using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Domain.Enums;

namespace MedGuard.Application.Interfaces;

public interface IBatchService
{
    Task<BatchDto> CreateBatchAsync(CreateBatchRequest request, CancellationToken ct = default);
    Task<BatchDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BatchDetailDto> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BatchDto>> GetAllAsync(CancellationToken ct = default);
}

public interface IColdChainMonitoringService
{
    Task<SensorReadingDto> RecordReadingAsync(RecordReadingRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SensorReadingDto>> GetHistoryAsync(Guid batchId, int take = 100, CancellationToken ct = default);
}

public interface IAlertService
{
    Task<IReadOnlyList<AlertDto>> GetUnresolvedAsync(CancellationToken ct = default);
    Task ResolveAsync(Guid alertId, CancellationToken ct = default);

    /// <summary>Backs the full Alerts log page (GET /api/alerts) — filterable, paginated.</summary>
    Task<PagedResult<AlertDto>> GetPagedAsync(int page, int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default);
}

public interface IShipmentService
{
    Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default);
    Task<ShipmentDto> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ShipmentDto>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);
}

public interface IDeviceService
{
    Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);
    Task<DeviceDto> RecordHeartbeatAsync(Guid deviceId, DeviceHeartbeatRequest request, CancellationToken ct = default);
    Task<DeviceDto> AssignToBatchAsync(Guid deviceId, Guid batchId, CancellationToken ct = default);
    Task<DeviceDto> UnassignAsync(Guid deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceDto>> GetAllAsync(CancellationToken ct = default);
}
