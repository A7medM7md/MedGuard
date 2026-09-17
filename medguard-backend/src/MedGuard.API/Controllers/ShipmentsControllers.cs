using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShipmentsController : BaseApiController
{
    private readonly IShipmentService _shipmentService;
    public ShipmentsController(ResponseHandler response, IShipmentService shipmentService) =>
        _shipmentService = shipmentService;

    // Batch.Ship() throws InvalidBatchStateTransitionException when quarantined/recalled —
    // ExceptionHandlingMiddleware turns that into a 400 automatically.
    [HttpPost]
    public async Task<ActionResult<Response<ShipmentDto>>> Create(CreateShipmentRequest request, CancellationToken ct) =>
        NewResult(await _shipmentService.CreateShipmentAsync(request, ct));

    [HttpPost("{id:guid}/deliver")]
    public async Task<ActionResult<Response<ShipmentDto>>> MarkDelivered(Guid id, CancellationToken ct) =>
        NewResult(await _shipmentService.MarkDeliveredAsync(id, ct));

    [HttpGet("batch/{batchId:guid}")]
    public async Task<ActionResult<Response<List<ShipmentDto>>>> GetByBatch(Guid batchId, CancellationToken ct) =>
        NewResult(await _shipmentService.GetByBatchIdAsync(batchId, ct));
}
