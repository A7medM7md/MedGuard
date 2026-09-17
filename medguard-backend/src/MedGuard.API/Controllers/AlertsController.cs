using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

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

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        await _alertService.ResolveAsync(id, ct);
        return NoContent();
    }
}
