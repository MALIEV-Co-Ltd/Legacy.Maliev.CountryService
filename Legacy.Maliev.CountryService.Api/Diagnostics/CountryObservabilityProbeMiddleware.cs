using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CountryService.Api.Diagnostics;

/// <summary>Exercises one controlled failure through an isolated SDK boundary without a business operation.</summary>
public sealed class CountryObservabilityProbeMiddleware
{
    private const string DiagnosticPath = "/internal/diagnostics/observability";
    private static readonly object NonceKey = new();
    private readonly string _healthInstance = Guid.NewGuid().ToString("N");
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ExceptionHandlerMiddleware _framework;
    private readonly object _gate = new();
    private DateTimeOffset _lastProbeAtUtc = DateTimeOffset.MinValue;

    /// <summary>Creates the per-application request-context loopback diagnostic boundary.</summary>
    /// <param name="next">The unchanged normal request pipeline.</param>
    /// <param name="loggerFactory">The existing host logger factory; no provider is changed.</param>
    /// <param name="diagnostics">The existing SDK diagnostic listener.</param>
    /// <param name="timeProvider">The existing application clock for the source minute gate.</param>
    public CountryObservabilityProbeMiddleware(RequestDelegate next, ILoggerFactory loggerFactory,
        DiagnosticListener diagnostics, TimeProvider timeProvider)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger<CountryObservabilityProbeMiddleware>();
        _timeProvider = timeProvider;
        // The public wrapper owns only this controlled branch and does not resolve host IExceptionHandler services.
        _framework = new ExceptionHandlerMiddleware(context =>
        {
            var nonce = (string)context.Items[NonceKey]!;
            _logger.LogWarning("{EventName} Synthetic={Synthetic} DiagnosticId={DiagnosticId}",
                "ObservabilityPipelineProbe", true, nonce);
            throw new CountryObservabilityDiagnosticException();
        }, loggerFactory, Options.Create(new ExceptionHandlerOptions
        {
            ExceptionHandler = context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                WriteHeaders(context, (string)context.Items[NonceKey]!);
                return Task.CompletedTask;
            },
            SuppressDiagnosticsCallback = _ => false
        }), diagnostics);
    }

    /// <summary>Admits only native GET/loopback/Guid-N requests, at most once per application minute.</summary>
    /// <param name="context">The current request; its peer identity may already reflect upstream hosting middleware.</param>
    /// <returns>The unchanged next operation, a quiet rejection, or the controlled failure response.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            && (context.Request.Path == "/countries/liveness" || context.Request.Path == "/countries/readiness"))
        {
            WriteHealthHeaders(context);
            context.Response.OnStarting(() =>
            {
                WriteHealthHeaders(context);
                return Task.CompletedTask;
            });
        }

        if (context.Request.Path != DiagnosticPath)
        {
            await _next(context);
            return;
        }

        WriteHeaders(context, null);
        var values = context.Request.Headers["X-Maliev-Diagnostic-Id"];
        if (!HttpMethods.IsGet(context.Request.Method)
            || context.Connection.RemoteIpAddress is null
            || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
            || values.Count != 1 || !Guid.TryParseExact(values[0], "N", out var id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now - _lastProbeAtUtc < TimeSpan.FromMinutes(1))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
            _lastProbeAtUtc = now;
        }

        var nonce = id.ToString("N");
        context.Items[NonceKey] = nonce;
        WriteHeaders(context, nonce);
        // Registered before the SDK's cache callback so the final branch headers survive Response.Clear.
        context.Response.OnStarting(() =>
        {
            WriteHeaders(context, nonce);
            return Task.CompletedTask;
        });
        try
        {
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["Synthetic"] = true,
                ["DiagnosticId"] = nonce
            }))
            {
                await _framework.Invoke(context);
                // .NET 10 emits its actual Error after the handler delegate returns.
                _logger.LogCritical("{EventName} Synthetic={Synthetic} DiagnosticId={DiagnosticId} StatusCode={StatusCode} ExceptionType={ExceptionType}",
                    "UnhandledRequestFailure", true, nonce, StatusCodes.Status500InternalServerError,
                    typeof(CountryObservabilityDiagnosticException).FullName);
            }
        }
        finally
        {
            context.Items.Remove(NonceKey);
        }
    }

    private void WriteHeaders(HttpContext context, string? nonce)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex";
        if (nonce is not null)
        {
            context.Response.Headers["X-Maliev-Diagnostic-Id"] = nonce;
            WriteHealthHeaders(context);
        }
    }

    private void WriteHealthHeaders(HttpContext context)
    {
        context.Response.Headers["X-Maliev-Health-Instance"] = _healthInstance;
        context.Response.Headers.CacheControl = "no-store";
    }
}

/// <summary>Identifies a fixed controlled exception rather than customer or provider data.</summary>
internal sealed class CountryObservabilityDiagnosticException()
    : Exception("Controlled loopback observability diagnostic; no business operation executed.");
