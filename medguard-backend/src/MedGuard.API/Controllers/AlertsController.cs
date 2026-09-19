using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : BaseApiController
{
    private readonly IAlertService _alertService;
    public AlertsController(ResponseHandler response, IAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet("unresolved")]
    public async Task<ActionResult<Response<List<AlertDto>>>> GetUnresolved(CancellationToken ct) =>
        NewResult(await _alertService.GetUnresolvedAsync(ct));

    /// <summary>Backs the full Alerts log page — filterable, paginated.</summary>
    [HttpGet]
    public async Task<ActionResult<Response<PagedResult<AlertDto>>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AlertSeverity? severity = null,
        [FromQuery] bool? isResolved = null,
        CancellationToken ct = default) =>
        NewResult(await _alertService.GetPagedAsync(page, pageSize, severity, isResolved, ct));

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        await _alertService.ResolveAsync(id, ct);
        return NoContent();
    }
}
