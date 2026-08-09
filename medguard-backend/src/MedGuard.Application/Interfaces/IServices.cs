using MedGuard.Application.DTOs;

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
}

public interface IShipmentService
{
    Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default);
    Task<ShipmentDto> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ShipmentDto>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default);
}
