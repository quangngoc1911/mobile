using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ApiError error)
        {
            context.Response.StatusCode = error.Status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = error.Status, Title = error.Message }, cancellationToken);
            return true;
        }
        logger.LogError(exception, "Lỗi không xử lý được. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 500, Title = "Không thể xử lý yêu cầu.", Extensions = { ["traceId"] = context.TraceIdentifier } }, cancellationToken);
        return true;
    }
}

public sealed class ApiError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public static ApiError BadRequest(string message) => new(400, message);
    public static ApiError Forbidden() => new(403, "Không có quyền thực hiện thao tác này.");
    public static ApiError NotFound() => new(404, "Không tìm thấy dữ liệu.");
    public static ApiError Conflict(string message) => new(409, message);
}
