using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : BaseApiController
{
    private readonly IDeviceService _deviceService;
    public DevicesController(ResponseHandler response, IDeviceService deviceService) : base(response) => _deviceService = deviceService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> GetAll(CancellationToken ct) =>
        Ok(Response.Success(await _deviceService.GetAllAsync(ct)));

    [HttpPost]
    public async Task<ActionResult<DeviceDto>> Register(RegisterDeviceRequest request, CancellationToken ct) =>
        Ok(Response.Success(await _deviceService.RegisterDeviceAsync(request, ct)));

    // Anonymous on purpose: this is called by the physical sensor hardware/gateway,
    // not a logged-in user — it needs its own auth scheme (e.g. a per-device API key)
    // rather than the Identity bearer token used by the Angular app. Tracked as a
    // follow-up; not implemented yet.
    [HttpPost("{id:guid}/heartbeat")]
    [AllowAnonymous]
    public async Task<ActionResult<DeviceDto>> Heartbeat(Guid id, DeviceHeartbeatRequest request, CancellationToken ct) =>
        Ok(Response.Success(await _deviceService.RecordHeartbeatAsync(id, request, ct)));

    [HttpPost("{id:guid}/assign/{batchId:guid}")]
    public async Task<ActionResult<DeviceDto>> AssignToBatch(Guid id, Guid batchId, CancellationToken ct) =>
        Ok(Response.Success(await _deviceService.AssignToBatchAsync(id, batchId, ct)));

    [HttpPost("{id:guid}/unassign")]
    public async Task<ActionResult<DeviceDto>> Unassign(Guid id, CancellationToken ct) =>
        Ok(Response.Success(await _deviceService.UnassignAsync(id, ct)));
}