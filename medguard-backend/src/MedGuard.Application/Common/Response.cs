using System.Net;

namespace MedGuard.Application.Common
{
    public class Response<T>
    {
        public bool Succeeded { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public string Message { get; set; } = string.Empty;

        public List<string> Errors { get; set; }

        public object Meta { get; set; } = null!;

        public T Data { get; set; } = default!;

        // Private constructor to force using factory methods
        public Response() { }

        // Success factory
        public static Response<T> Success(
            T data,
            string message = "Request completed successfully",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            object meta = null)
        {
            return new Response<T>
            {
                Succeeded = true,
                StatusCode = statusCode,
                Message = message,
                Data = data,
                Errors = null,
                Meta = meta
            };
        }

        // Fail factory (with list of errors)
        public static Response<T> Fail(
            string message = "Request failed",
            HttpStatusCode statusCode = HttpStatusCode.BadRequest,
            List<string> errors = null,
            object meta = null)
        {
            return new Response<T>
            {
                Succeeded = false,
                StatusCode = statusCode,
                Message = message,
                Errors = errors ?? new List<string>(),
                Data = default,
                Meta = meta
            };
        }

        // Fail factory (single error)
        public static Response<T> Fail(
            string error,
            string message = "Request failed",
            HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            return Fail(message, statusCode, new List<string> { error });
        }
    }
}
