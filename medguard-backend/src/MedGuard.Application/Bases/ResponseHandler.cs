using MedGuard.Application.Common;
using MedGuard.Application.Resources;
using System.Net;

namespace MedGuard.Application.Bases
{
    public class ResponseHandler
    {
        public Response<T> Success<T>(T data, string? message = null, object? meta = null) =>
            Response<T>.Success(data, message ?? SharedResourcesKeys.Success, HttpStatusCode.OK, meta);

        public Response<T> Created<T>(T data, string? message = null, object? meta = null) =>
            Response<T>.Success(data, message ?? SharedResourcesKeys.Created, HttpStatusCode.Created, meta);

        public Response<T> Updated<T>(T data, string? message = null, object? meta = null) =>
            Response<T>.Success(data, message ?? SharedResourcesKeys.Updated, HttpStatusCode.OK, meta);

        public Response<T> Deleted<T>(string? message = null, bool noContent = false) =>
            Response<T>.Success(default, message ?? SharedResourcesKeys.Deleted,
                noContent ? HttpStatusCode.NoContent : HttpStatusCode.OK);

        public Response<T> BadRequest<T>(string? message = null, List<string>? errors = null) =>
            Response<T>.Fail(message ?? SharedResourcesKeys.BadRequest, HttpStatusCode.BadRequest, errors);

        public Response<T> Unauthorized<T>(string? message = null) =>
            Response<T>.Fail(message ?? SharedResourcesKeys.UnAuthorized, HttpStatusCode.Unauthorized);

        public Response<T> NotFound<T>(string? message = null) =>
            Response<T>.Fail(message ?? SharedResourcesKeys.NotFound, HttpStatusCode.NotFound);

        public Response<T> UnprocessableEntity<T>(string? message = null, List<string>? errors = null) =>
            Response<T>.Fail(message ?? SharedResourcesKeys.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, errors);

        public Response<T> InternalServerError<T>(string? message = null, List<string>? errors = null) =>
            Response<T>.Fail(message ?? SharedResourcesKeys.InternalServerError, HttpStatusCode.InternalServerError, errors);
    }
}