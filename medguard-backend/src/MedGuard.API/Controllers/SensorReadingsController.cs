using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SensorReadingsController : BaseApiController
{
    private readonly IColdChainMonitoringService _monitoringService;

    public SensorReadingsController(ResponseHandler response, IColdChainMonitoringService monitoringService) : base(response) =>
        _monitoringService = monitoringService;

    /// <summary>Endpoint an IoT device (or its edge gateway) calls every time it records a temperature reading.</summary>
    [HttpPost]
    public async Task<ActionResult<Response<SensorReadingDto>>> Record(RecordReadingRequest request, CancellationToken ct) =>
        Ok(Response.Success(await _monitoringService.RecordReadingAsync(request, ct)));

    [HttpGet("batch/{batchId:guid}")]
    public async Task<ActionResult<Response<IReadOnlyList<SensorReadingDto>>>> GetHistory(
        Guid batchId, [FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(Response.Success(await _monitoringService.GetHistoryAsync(batchId, take, ct)));
}
