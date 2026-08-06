using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using MedGuard.Application.Resources;
using MedGuard.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

public class AlertsController : BaseApiController
{
    private readonly IAlertService _alertService;
    public AlertsController(ResponseHandler response, IAlertService alertService) : base(response)
    {
        _alertService = alertService;
    }

    [HttpGet("unresolved")]
    public async Task<ActionResult<Response<IReadOnlyList<AlertDto>>>> GetUnresolved(CancellationToken ct) =>
        Ok(Response.Success(await _alertService.GetUnresolvedAsync(ct)));

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        await _alertService.ResolveAsync(id, ct);
        return Ok(Response.Success(SharedResourcesKeys.Success));
    }
}
