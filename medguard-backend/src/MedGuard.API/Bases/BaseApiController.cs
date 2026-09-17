using MedGuard.Application.Common;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace MedGuard.Api.Bases
{
    [ApiController]
    public class BaseApiController : ControllerBase
    {
        public ObjectResult NewResult<T>(Response<T> response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return new OkObjectResult(response);
                case HttpStatusCode.Created:
                    return new CreatedResult(string.Empty, response);
                case HttpStatusCode.Unauthorized:
                    return new UnauthorizedObjectResult(response);
                case HttpStatusCode.Forbidden:
                    return new ObjectResult(response) { StatusCode = StatusCodes.Status403Forbidden };
                case HttpStatusCode.BadRequest:
                    return new BadRequestObjectResult(response);
                case HttpStatusCode.NotFound:
                    return new NotFoundObjectResult(response);
                case HttpStatusCode.Accepted:
                    return new AcceptedResult(string.Empty, response);
                case HttpStatusCode.UnprocessableEntity:
                    return new UnprocessableEntityObjectResult(response);
                case HttpStatusCode.Locked:
                    return new ObjectResult(response) { StatusCode = StatusCodes.Status423Locked };
                case HttpStatusCode.NoContent:
                    return new ObjectResult(response) { StatusCode = StatusCodes.Status204NoContent };
                default:
                    return new BadRequestObjectResult(response);
            }
        }
    }
}
