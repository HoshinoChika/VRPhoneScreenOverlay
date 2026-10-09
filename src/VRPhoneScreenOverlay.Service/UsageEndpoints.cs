using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Service;

public static class UsageEndpoints
{
    public static void AddUsageHeartbeat(this IServiceCollection services)
    {
        services.AddSingleton<UsageHeartbeatStore>();
        services.AddHostedService<UsageHeartbeatWorker>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // One bounded global bucket, no unbounded IP partitions. No queued requests.
            options.AddFixedWindowLimiter("usage", limiter =>
            {
                limiter.PermitLimit = 2000;
                limiter.Window = TimeSpan.FromSeconds(1);
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });
    }

    public static void MapUsageHeartbeat(this WebApplication app)
    {
        app.UseRateLimiter();
        app.MapPost("/vrphonescreen/api/v1/usage/heartbeat", async (
            HttpContext context, UsageHeartbeatStore store, CancellationToken cancellationToken) =>
        {
            IHttpMaxRequestBodySizeFeature? bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) { bodyLimit.MaxRequestBodySize = 1024; }
            if (context.Request.ContentLength > 1024)
            {
                context.Response.Headers.Connection = "close";
                return Results.StatusCode(413);
            }
            if (!context.Request.HasJsonContentType()) { return Results.StatusCode(415); }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                UsageHeartbeatRequest? request = await context.Request.ReadFromJsonAsync<UsageHeartbeatRequest>(timeout.Token);
                return request is null ? Results.BadRequest() : Results.StatusCode(store.Record(request, context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (BadHttpRequestException exception) { return Results.StatusCode(exception.StatusCode); }
            catch (OperationCanceledException) { return Results.StatusCode(408); }
        }).RequireRateLimiting("usage");

        // A server-only token, never bundled with the public client. Unconfigured means disabled.
        string? token = Environment.GetEnvironmentVariable("USAGE_STATS_TOKEN");
        app.MapGet("/vrphonescreen/api/v1/usage/stats", (HttpContext context, UsageHeartbeatStore store) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), token))
            { return Results.NotFound(); }
            return store.IsAvailable ? Results.Ok(store.GetStatistics())
                : Results.Json(new { reason = store.ReasonCode }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).RequireRateLimiting("usage");
    }

    internal static bool IsAuthorized(string authorization, string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 32 || authorization.Length > 512)
        { return false; }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(authorization)),
            SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token)));
    }
}
