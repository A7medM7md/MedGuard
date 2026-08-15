using MedGuard.Api.Bases;
using MedGuard.Application.Bases;
using MedGuard.Application.Common;
using MedGuard.Application.DTOs;
using MedGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BatchesController : BaseApiController
{
    private readonly IBatchService _batchService;

    public BatchesController(ResponseHandler response, IBatchService batchService) : base(response) => _batchService = batchService;

    [HttpGet]
    public async Task<ActionResult<Response<IReadOnlyList<BatchDto>>>> GetAll(CancellationToken ct) =>
        Ok(Response.Success(await _batchService.GetAllAsync(ct)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Response<BatchDto>>> GetById(Guid id, CancellationToken ct)
    {
        var batch = await _batchService.GetByIdAsync(id, ct);
        return batch is null ? NotFound(Response.NotFound<BatchDto>()) : Ok(Response.Success(batch));
    }

    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<Response<BatchDetailDto>>> GetDetail(Guid id, CancellationToken ct) =>
        Ok(Response.Success(await _batchService.GetDetailAsync(id, ct)));

    [HttpPost]
    public async Task<ActionResult<Response<BatchDto>>> Create(CreateBatchRequest request, CancellationToken ct)
    {
        var batch = await _batchService.CreateBatchAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = batch.Id }, Response.Success(batch));
    }
}
