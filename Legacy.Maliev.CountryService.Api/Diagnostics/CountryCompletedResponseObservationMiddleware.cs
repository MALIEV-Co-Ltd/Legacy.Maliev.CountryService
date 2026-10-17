namespace Legacy.Maliev.CountryService.Api.Diagnostics;

/// <summary>Preserves source completed-response diagnostics without changing HTTP results or observing thrown failures.</summary>
/// <param name="next">The existing downstream request pipeline.</param>
/// <param name="logger">The existing native logger for fixed diagnostic metadata.</param>
/// <param name="clock">The host-owned clock used by the source health suppression window.</param>
public sealed class CountryCompletedResponseObservationMiddleware(
    RequestDelegate next, ILogger<CountryCompletedResponseObservationMiddleware> logger, TimeProvider clock)
{
    private readonly object gate = new();
    private DateTimeOffset lastReadiness = DateTimeOffset.MinValue;
    private DateTimeOffset lastLiveness = DateTimeOffset.MinValue;
    private int readinessStatus;
    private int livenessStatus;

    /// <summary>Records fixed source events only after downstream completes normally.</summary>
    /// <param name="context">The unchanged request and response context.</param>
    /// <returns>The downstream request completion.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // Keep this inside the existing outer exception handler. A thrown failure
        // escapes before observation, leaving the original handler its sole incident.
        await next(context);

        var path = context.Request.Path.Value ?? string.Empty;
        bool readiness = path.EndsWith("/readiness", StringComparison.OrdinalIgnoreCase);
        bool liveness = path.EndsWith("/liveness", StringComparison.OrdinalIgnoreCase);
        int status = context.Response.StatusCode;
        if (readiness || liveness)
        {
            lock (gate)
            {
                ref int previousStatus = ref (readiness ? ref readinessStatus : ref livenessStatus);
                ref DateTimeOffset previousAt = ref (readiness ? ref lastReadiness : ref lastLiveness);
                if (status < 500)
                {
                    previousStatus = 0;
                    previousAt = DateTimeOffset.MinValue;
                    return;
                }
                var now = clock.GetUtcNow();
                if (status == previousStatus && now - previousAt < TimeSpan.FromMinutes(5)) return;
                previousStatus = status;
                previousAt = now;
            }
        }
        if (status >= 500)
        {
            // Never emit paths, queries, headers, bodies, exception text or arbitrary state.
            logger.LogError("{EventName} Operation={Operation} StatusCode={StatusCode}",
                readiness || liveness ? "HealthProbeFailure" : "HandledOperationFailure",
                readiness ? "Readiness" : liveness ? "Liveness" : "HttpResponse", status);
        }
    }
}
