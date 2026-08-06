using MedGuard.Application.Bases;
using Microsoft.AspNetCore.Mvc;

namespace MedGuard.Api.Bases
{
    [ApiController]
    public class BaseApiController : ControllerBase
    {
        protected readonly ResponseHandler Response;

        protected BaseApiController(ResponseHandler response)
        {
            Response = response;
        }
    }
}
