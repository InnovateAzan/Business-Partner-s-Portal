using System.Text.Json;
namespace BusinessPartnerPortal.Api.Common;

public sealed class GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Handled API error {StatusCode} for {Path}. CorrelationId={CorrelationId}", ex.StatusCode, context.Request.Path, context.TraceIdentifier);
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = ex.Message, correlationId = context.TraceIdentifier }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Path}. CorrelationId={CorrelationId}", context.Request.Path, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "An unexpected server error occurred.", correlationId = context.TraceIdentifier }));
        }
    }
}
