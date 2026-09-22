using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Domain.Enums;

namespace MedGuard.Application.Interfaces;

public interface IBatchService
{
    Task<Response<BatchDto>> CreateBatchAsync(CreateBatchRequest request, CancellationToken ct = default);
    Task<Response<BatchDto?>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Response<BatchDetailDto>> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<Response<List<BatchDto>>> GetAllAsync(CancellationToken ct = default);
    Task<Response<BatchDto>> ClearQuarantineAsync(Guid id, CancellationToken ct = default);
    Task<Response<BatchDto>> RecallAsync(Guid id, CancellationToken ct = default);
}

public interface IColdChainMonitoringService
{
    Task<Response<SensorReadingDto>> RecordReadingAsync(RecordReadingRequest request, CancellationToken ct = default);

    Task<Response<List<SensorReadingDto>>> GetHistoryAsync(Guid batchId, int take = 100, CancellationToken ct = default);
}

public interface IAlertService
{
    Task<Response<List<AlertDto>>> GetUnresolvedAsync(CancellationToken ct = default);
    Task ResolveAsync(Guid alertId, CancellationToken ct = default);

    /// <summary>Backs the full Alerts log page (GET /api/alerts) — filterable, paginated.</summary>
    Task<Response<PagedResult<AlertDto>>> GetPagedAsync(int page, int pageSize,
        AlertSeverity? severity = null,
        bool? isResolved = null,
        CancellationToken ct = default);
}

public interface IShipmentService
{
    Task<Response<ShipmentDto>> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default);
    Task<Response<ShipmentDto>> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default);
    Task<Response<List<ShipmentDto>>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);
    Task<Response<List<ShipmentDto>>> GetAllAsync(CancellationToken ct = default);
}

public interface IDeviceService
{
    Task<Response<DeviceDto>> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);
    Task<Response<DeviceDto>> RecordHeartbeatAsync(Guid deviceId, DeviceHeartbeatRequest request, CancellationToken ct = default);
    Task<Response<DeviceDto>> AssignToBatchAsync(Guid deviceId, Guid batchId, CancellationToken ct = default);
    Task<Response<DeviceDto>> UnassignAsync(Guid deviceId, CancellationToken ct = default);
    Task<Response<List<DeviceDto>>> GetAllAsync(CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<Response<SettingsDto>> GetAsync(CancellationToken ct = default);
    Task<Response<SettingsDto>> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default);
}
