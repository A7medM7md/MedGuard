using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Exceptions;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Entities;
using MedGuard.Domain.Interfaces;

namespace MedGuard.Application.Services;

public class ShipmentService : ResponseHandler, IShipmentService
{
    private readonly IUnitOfWork _uow;
    public ShipmentService(IUnitOfWork uow) => _uow = uow;

    public async Task<Response<ShipmentDto>> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default)
    {
        var batch = await _uow.Batches.GetByIdAsync(request.BatchId, ct)
            ?? throw new NotFoundException(nameof(Batch), request.BatchId);

        // Batch.Ship() enforces the "no shipping while quarantined/recalled" rule itself
        // and throws InvalidBatchStateTransitionException — no status check needed here.
        var shipment = batch.Ship(request.OriginLocation, request.DestinationLocation, request.CourierName);

        await _uow.Shipments.AddAsync(shipment, ct);
        await _uow.SaveChangesAsync(ct);

        var result = ToDto(shipment);
        return Success(result);
    }

    public async Task<Response<ShipmentDto>> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default)
    {
        var shipment = await _uow.Shipments.GetByIdAsync(shipmentId, ct)
            ?? throw new NotFoundException(nameof(Shipment), shipmentId);

        shipment.MarkDelivered();
        _uow.Shipments.Update(shipment);

        var batch = await _uow.Batches.GetByIdAsync(shipment.BatchId, ct);
        if (batch is not null)
        {
            batch.MarkDelivered();
            _uow.Batches.Update(batch);
        }

        await _uow.SaveChangesAsync(ct);
        var result = ToDto(shipment);
        return Success(result);
    }

    public async Task<Response<List<ShipmentDto>>> GetByBatchIdAsync(Guid batchId, CancellationToken ct = default)
    {
        var shipments = await _uow.Shipments.GetByBatchIdAsync(batchId, ct);
        var result = shipments.Select(ToDto).ToList();
        return Success(result);
    }

    private static ShipmentDto ToDto(Shipment s) => new(
        s.Id, s.BatchId, s.OriginLocation, s.DestinationLocation, s.CourierName, s.Status, s.DepartedAtUtc, s.ArrivedAtUtc);
}
