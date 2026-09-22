using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : BaseApiController
{
    private readonly ISettingsService _settingsService;
    public SettingsController(ResponseHandler response, ISettingsService settingsService) =>
        _settingsService = settingsService;

    [HttpGet]
    public async Task<ActionResult<Response<SettingsDto>>> Get(CancellationToken ct) =>
        NewResult(await _settingsService.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<Response<SettingsDto>>> Update(UpdateSettingsRequest request, CancellationToken ct) =>
        NewResult(await _settingsService.UpdateAsync(request, ct));
}
