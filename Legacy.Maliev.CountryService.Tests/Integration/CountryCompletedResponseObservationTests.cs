using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Source completed-response observation through the unchanged normal HTTP and native logging pipeline.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryCompletedResponseObservationTests
{
    private const string Private = "country61-private-marker";

    /// <summary>Every nonthrowing handled5xx is observed, without altering response data or emitting private inputs.</summary>
    [Fact]
    public async Task HandledResponses_NormalPipeline_RecordsEvery5xxWithoutChangingResponse()
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        foreach (var status in new[] { 503, 503, 200, 400, 502 })
        {
            factory.Control.Status = status;
            using var response = await client.GetAsync("/country61-acceptance/handled/" + Private + "?search=" + Private);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Equal("unchanged", Assert.Single(response.Headers.GetValues("X-Country61-Control")));
            Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
            Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        }
        var events = Events(factory, "HandledOperationFailure");
        Assert.Equal(new[] { 503, 503, 502 }, events.Select(Fields).Select(fields => fields.GetProperty("StatusCode").GetInt32()));
        Assert.All(events, entry =>
        {
            Assert.Equal("ERROR", entry.GetProperty("severity").GetString());
            Assert.Equal("HttpResponse", Fields(entry).GetProperty("Operation").GetString());
            AssertPrivateFields(entry);
        });
        Assert.Empty(Events(factory, "HealthProbeFailure"));
        Assert.Empty(Events(factory, "UnhandledRequestFailure"));
    }

    /// <summary>Readiness and liveness have independent status buckets, exact five-minute windows and recovery resets.</summary>
    [Fact]
    public async Task HealthSuffixes_NormalPipeline_PreservesIndependentBucketsAndExactWindow()
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        async Task Probe(string operation, int status)
        {
            factory.Control.Status = status;
            using var response = await client.GetAsync("/country61-acceptance/" + operation + "?private=" + Private);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        }
        await Probe("readiness", 503);
        await Probe("readiness", 503);
        await Probe("liveness", 503);
        Assert.Equal(2, Events(factory, "HealthProbeFailure").Length);
        factory.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await Probe("readiness", 503);
        await Probe("liveness", 503);
        Assert.Equal(2, Events(factory, "HealthProbeFailure").Length);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await Probe("readiness", 503);
        await Probe("liveness", 503);
        Assert.Equal(4, Events(factory, "HealthProbeFailure").Length);
        await Probe("readiness", 502);
        Assert.Equal(5, Events(factory, "HealthProbeFailure").Length);
        await Probe("readiness", 200);
        await Probe("readiness", 502);
        var events = Events(factory, "HealthProbeFailure");
        Assert.Equal(new[] { "Readiness", "Liveness", "Readiness", "Liveness", "Readiness", "Readiness" },
            events.Select(Fields).Select(fields => fields.GetProperty("Operation").GetString()));
        Assert.Equal(new[] { 503, 503, 503, 503, 502, 502 },
            events.Select(Fields).Select(fields => fields.GetProperty("StatusCode").GetInt32()));
        Assert.All(events, AssertPrivateFields);
        Assert.Empty(Events(factory, "HandledOperationFailure"));
    }

    /// <summary>Every non5xx completion, including a client error, resets only its source health bucket.</summary>
    [Theory]
    [InlineData("readiness")]
    [InlineData("liveness")]
    public async Task HealthSuffix_ClientErrorRecovery_ImmediatelyReadmitsSameFailure(string operation)
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        foreach (var status in new[] { 503, 503, 400, 503 })
        {
            factory.Control.Status = status;
            using var response = await client.GetAsync("/country61-acceptance/" + operation);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
        }
        Assert.Equal(2, Events(factory, "HealthProbeFailure").Length);
    }

    /// <summary>The registered readiness response/body and public health contract remain unchanged.</summary>
    [Fact]
    public async Task RegisteredReadiness_ControlledResults_ObservesFailureAndRecoveryWithoutNewHeaders()
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        foreach (var healthy in new[] { true, false, false, true, false })
        {
            factory.Control.Healthy = healthy;
            using var response = await client.GetAsync("/countries/readiness");
            Assert.Equal(healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(healthy ? "Healthy" : "Unhealthy", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
            Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        }
        var events = Events(factory, "HealthProbeFailure");
        Assert.Equal(2, events.Length);
        Assert.All(events, entry =>
        {
            Assert.Equal("Readiness", Fields(entry).GetProperty("Operation").GetString());
            AssertPrivateFields(entry);
        });
    }

    /// <summary>Thrown errors remain owned by the existing outer exception middleware, without completed-response duplicates.</summary>
    [Fact]
    public async Task ThrownFailure_NormalPipeline_PreservesExistingSafe500AndSingleOuterIncident()
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/country61-acceptance/throw/" + Private + "?search=" + Private);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal("An internal server error occurred", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(500, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("details").ValueKind);
        Assert.DoesNotContain(Private, text, StringComparison.Ordinal);
        Assert.Single(Events(factory, "UnhandledRequestFailure"));
        Assert.Empty(Events(factory, "HandledOperationFailure"));
        Assert.Empty(Events(factory, "HealthProbeFailure"));
    }

    /// <summary>The passive observer cannot change anonymous protection or add synthetic access.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ProtectedLegacyRoute_Anonymous_Preserves401WithoutObservation(string method)
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/Countries/not-an-integer");
        if (method == "PUT") request.Content = new StringContent("{\"name\":\"Unchanged\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        using var synthetic = await client.GetAsync("/internal/diagnostics/observability");
        Assert.Equal(HttpStatusCode.NotFound, synthetic.StatusCode);
        Assert.Empty(Events(factory, "HandledOperationFailure"));
        Assert.Empty(Events(factory, "HealthProbeFailure"));
    }

    /// <summary>Cancellation escaping downstream is not swallowed, converted or observed as a completed error.</summary>
    [Fact]
    public async Task CancelledDownstream_Observer_RethrowsSameCancellationWithoutLogging()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var context = new DefaultHttpContext();
        context.Request.Path = "/country61-acceptance/readiness";
        context.RequestAborted = cancelled.Token;
        context.Response.StatusCode = 503;
        var failure = new OperationCanceledException(cancelled.Token);
        var logger = new ObserverLogger();
        var observer = new CountryCompletedResponseObservationMiddleware(_ => Task.FromException(failure),
            logger, new FakeTimeProvider());
        var escaped = await Record.ExceptionAsync(() => observer.InvokeAsync(context));
        Assert.Same(failure, escaped);
        Assert.Empty(logger.Events);
        Assert.Equal(503, context.Response.StatusCode);
    }

    /// <summary>Concurrent same-status completions cannot defeat the per-host source health admission lock.</summary>
    [Fact]
    public async Task HealthSuffix_ConcurrentSameStatus_RecordsOneFailureInWindow()
    {
        await using var factory = new ObservationFactory();
        using var client = factory.CreateClient();
        factory.Control.Status = 503;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var response = await client.GetAsync("/country61-acceptance/readiness");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }));
        Assert.Single(Events(factory, "HealthProbeFailure"));
    }

    private static JsonElement Fields(JsonElement entry) => entry.GetProperty("State");

    private static JsonElement[] Events(ObservationFactory factory, string name) => factory.Logs.Lines.Select(line =>
    {
        using var json = JsonDocument.Parse(line);
        return json.RootElement.Clone();
    }).Where(entry => entry.TryGetProperty("State", out var fields) && fields.ValueKind == JsonValueKind.Object
        && fields.TryGetProperty("EventName", out var value) && value.GetString() == name).ToArray();

    private static void AssertPrivateFields(JsonElement entry)
    {
        var fields = Fields(entry);
        Assert.Equal(new[] { "EventName", "Operation", "StatusCode" },
            fields.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Private, entry.GetRawText(), StringComparison.Ordinal);
        Assert.False(fields.TryGetProperty("{OriginalFormat}", out _));
        Assert.False(fields.TryGetProperty("Path", out _));
        Assert.False(fields.TryGetProperty("Exception", out _));
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("Exception").ValueKind);
    }

    private sealed class ObservationFactory : WebApplicationFactory<Program>
    {
        private readonly RSA key = RSA.Create(2048);
        internal CountryCompletedObservationControl Control { get; } = new();
        internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
        internal NativeCaptureProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var pair in new Dictionary<string, string?>
            {
                ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=country61;Username=fixture",
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://country61.invalid",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "country61",
                ["Jwt:Audience"] = "country61",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:TracingEnabled"] = "false",
                ["Observability:RuntimeMetricsEnabled"] = "false"
            }) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(Control);
                services.AddControllers().AddApplicationPart(typeof(CountryCompletedObservationController).Assembly);
                // Controlled health results only; this is not proof of live dependency readiness.
                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
                services.AddHealthChecks().AddCheck("country61-controlled", () =>
                    Control.Healthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("controlled"));
                services.AddSingleton<ILoggerProvider>(provider =>
                {
                    var name = provider.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName;
                    Logs.Formatter = provider.GetServices<ConsoleFormatter>().Single(formatter => formatter.Name == name);
                    return Logs;
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) key.Dispose();
        }
    }

    private sealed class ObserverLogger : ILogger<CountryCompletedResponseObservationMiddleware>
    {
        internal List<LogLevel> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(level);
    }

    private sealed class NativeCaptureProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        internal ConcurrentQueue<string> Lines { get; } = new();
        internal ConsoleFormatter Formatter { get; set; } = null!;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(NativeCaptureProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                using var writer = new StringWriter();
                provider.Formatter.Write(new LogEntry<TState>(level, category, eventId, state, exception, formatter), provider.scopes, writer);
                if (writer.GetStringBuilder().Length != 0) provider.Lines.Enqueue(writer.ToString());
            }
        }
    }
}

/// <summary>Test-only completion controls; no business or permission implementation is replaced.</summary>
public sealed class CountryCompletedObservationControl
{
    /// <summary>Gets or sets the next controlled completion status.</summary>
    public int Status { get; set; } = 200;

    /// <summary>Gets or sets the controlled readiness result.</summary>
    public bool Healthy { get; set; } = true;
}

/// <summary>Test-only endpoints exercise completion and exception paths through normal startup.</summary>
[ApiController]
public sealed class CountryCompletedObservationController : ControllerBase
{
    /// <summary>Starts the controlled response unchanged before passive observation.</summary>
    [HttpGet("country61-acceptance/handled/{value}")]
    [HttpGet("country61-acceptance/readiness")]
    [HttpGet("country61-acceptance/liveness")]
    public async Task CompleteAsync([FromServices] CountryCompletedObservationControl control)
    {
        Response.StatusCode = control.Status;
        Response.Headers["X-Country61-Control"] = "unchanged";
        await Response.StartAsync();
    }

    /// <summary>Throws a synthetic business error for the existing outer exception boundary.</summary>
    [HttpGet("country61-acceptance/throw/{value}")]
    public Task ThrowAsync() => throw new Exception("country61-private-marker");
}
