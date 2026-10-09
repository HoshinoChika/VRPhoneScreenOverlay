using Microsoft.AspNetCore.HttpOverrides;
using VRPhoneScreenOverlay.Protocols;
using VRPhoneScreenOverlay.Service;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
// Deployment supplies its listener; an unconfigured source build is local only.
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("VRPSO_SERVICE_URL") ?? "http://127.0.0.1:8080");
builder.Services.AddSingleton(ServiceOptions.FromEnvironment());
builder.Services.AddSingleton<DiagnosticUploadStore>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddUsageHeartbeat();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(CloudStorageOptions.FromEnvironment());
builder.Services.AddSingleton<ICloudStorage, OpenListCloudStorage>();
builder.Services.AddSingleton<CloudReleaseCache>();
builder.Services.AddHostedService<CloudReleaseWorker>();
builder.Services.AddSingleton<MonthlyDiagnosticArchiver>();
builder.Services.AddHostedService<MonthlyDiagnosticWorker>();
WebApplication app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
});

app.MapGet("/vrphonescreen/api/v1/health", () => Results.Ok(new
{
    ok = true,
    service = "VRPhoneScreen Overlay Test Service",
    time = DateTimeOffset.UtcNow,
}));

app.MapGet("/vrphonescreen/api/v1/updates/manifest", async (
    string? channel, CloudStorageOptions cloudOptions, CloudReleaseCache cloud, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!string.Equals(channel, "beta", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "Only the beta channel is available" });
    }

    context.Response.Headers.CacheControl = "no-store";
    if (cloudOptions.Enabled && await cloud.ActiveVersionAsync(cancellationToken) is { } version)
    {
        CloudReleaseRecord? release = await cloud.ReadReleaseAsync(version, cancellationToken);
        UpdateDownloadLease? lease = await cloud.GetAsync(version, false, cancellationToken);
        if (release is null || lease is null) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        return Results.Json(release.Manifest with { Download = lease });
    }
    return Results.NotFound(new { error = "No release has been published" });
});

app.MapGet("/vrphonescreen/api/v1/updates/download/{version}", async (
    string version, bool? refresh, CloudStorageOptions options, CloudReleaseCache cloud, HttpContext context, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!options.Enabled || !CloudReleaseCache.ValidVersion(version)) { return Results.NotFound(); }
    UpdateDownloadLease? lease = await cloud.GetAsync(version, refresh == true, cancellationToken, context.Request.Headers.UserAgent.ToString());
    if (lease is null) { context.Response.Headers.RetryAfter = "60"; return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    return Results.Redirect(lease.Url, permanent: false, preserveMethod: true);
});

app.MapPost("/vrphonescreen/api/v1/diagnostics/init", async (
    DiagnosticUploadInitRequest request,
    HttpContext context,
    DiagnosticUploadStore store,
    CancellationToken cancellationToken) =>
{
    DiagnosticUploadInitResponse? response = await store.CreateAsync(
        request,
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        context.Request.Scheme,
        context.Request.Host.HasValue ? context.Request.Host.Value : "localhost",
        cancellationToken);
    return response is null
        ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
        : Results.Ok(response);
});

app.MapPut("/vrphonescreen/api/v1/diagnostics/upload/{id}", async (
    string id,
    HttpRequest request,
    DiagnosticUploadStore store,
    CancellationToken cancellationToken) =>
{
    string authorization = request.Headers.Authorization.ToString();
    string token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authorization[7..]
        : string.Empty;
    DiagnosticStoreResult result = await store.UploadAsync(
        id,
        token,
        request.Body,
        request.ContentLength,
        cancellationToken);
    return Results.Json(new { accepted = result.Succeeded, error = result.Error },
        statusCode: result.StatusCode);
});

app.MapPost("/vrphonescreen/api/v1/diagnostics/complete", async (
    DiagnosticUploadCompleteRequest request,
    DiagnosticUploadStore store,
    CancellationToken cancellationToken) =>
{
    DiagnosticUploadCompleteResponse? completed = await store.CompleteAsync(
        request,
        cancellationToken);
    return completed is null ? Results.BadRequest() : Results.Ok(completed);
});

app.MapUsageHeartbeat();
app.Run();
