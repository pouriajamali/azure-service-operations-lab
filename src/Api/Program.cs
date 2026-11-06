using System.Diagnostics;
using System.Threading.RateLimiting;
using Operations;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = false);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddSingleton(new BoundedReadService(2));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("reads", _ => RateLimitPartition.GetConcurrencyLimiter("synthetic-read",
        _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    // Generate locally: no user identifiers, token values, headers or query strings are logged.
    var correlation = ActivityTraceId.CreateRandom().ToString();
    context.Response.Headers["X-Correlation-Id"] = correlation;
    var route = context.Request.Path.Value switch
    {
        "/health/live" => "/health/live", "/health/ready" => "/health/ready",
        "/version" => "/version", "/api/reference" => "/api/reference", _ => "unmatched"
    };
    try { await next(context); }
    finally
    {
        app.Logger.LogInformation("request_completed Route={Route} Status={Status} DurationMs={DurationMs} CorrelationId={CorrelationId} Aborted={Aborted}",
            route, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            correlation, context.RequestAborted.IsCancellationRequested);
    }
});
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", (IConfiguration config) =>
    !string.IsNullOrWhiteSpace(config["LAB_DEPENDENCY_TOKEN"]) && config["LAB_FAIL_READINESS"] != "true"
        ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/version", (IConfiguration config) => Results.Ok(new
{
    version = "0.1.0-preview.1", revision = config["CONTAINER_APP_REVISION"] ?? "local",
    sourceCommit = config["SOURCE_COMMIT"] ?? "unrecorded", mode = "synthetic-read-only"
}));
app.MapGet("/api/reference", async (int? delayMs, BoundedReadService reads, HttpContext context) =>
{
    var delay = delayMs ?? 0;
    if (delay < 0 || delay > 2000) return Results.BadRequest(new { error = "delayMs must be 0..2000" });
    try
    {
        var value = await reads.ReadAsync(async ct =>
        {
            await Task.Delay(delay, ct);
            return "SYNTHETIC-REFERENCE";
        }, TimeSpan.FromMilliseconds(100), context.RequestAborted);
        return Results.Ok(new { value, source = "synthetic", execution = "read-only" });
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (OperationCanceledException)
    {
        return Results.Json(new { error = "cooperative_read_budget_expired" }, statusCode: 504);
    }
}).RequireRateLimiting("reads");
app.Run();
