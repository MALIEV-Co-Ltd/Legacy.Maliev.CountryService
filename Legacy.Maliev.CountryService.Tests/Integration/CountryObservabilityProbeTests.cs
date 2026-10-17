using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryObservabilityProbeTests
{
    private const string Path = "/internal/diagnostics/observability";
    private const string Private = "country67-private-marker";
    private const string Nonce = "11111111222233334444555555555555";

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.2.3")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Probe_NativeLoopbackPeer_ReturnsControlledFailureWithoutAuthentication(string peer)
    {
        await using var factory = new ProbeFactory();
        var response = await SendAsync(factory, peer: peer);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.Response.StatusCode);
        AssertHeaders(response, Nonce);
        Assert.Equal("application/problem+json", response.Response.ContentType);
        using var reader = new StreamReader(response.Response.Body);
        Assert.Equal(string.Empty, await reader.ReadToEndAsync());
        Assert.Equal(0, factory.Stealer.Calls);
        Assert.Equal(3, Events(factory, Nonce).Length);
    }

    [Theory]
    [InlineData("POST", "127.0.0.1", "11111111222233334444555555555555")]
    [InlineData("HEAD", "127.0.0.1", "11111111222233334444555555555555")]
    [InlineData("GET", "203.0.113.5", "11111111222233334444555555555555")]
    [InlineData("GET", null, "11111111222233334444555555555555")]
    [InlineData("GET", "127.0.0.1", "not-a-guid")]
    [InlineData("GET", "127.0.0.1", "11111111-2222-3333-4444-555555555555")]
    [InlineData("GET", "127.0.0.1", "")]
    public async Task Probe_InvalidRequest_IsQuiet404AndDoesNotConsumeAdmission(string method, string? peer, string nonce)
    {
        await using var factory = new ProbeFactory();
        var denied = await SendAsync(factory, method: method, peer: peer, values: [nonce]);
        Assert.Equal(StatusCodes.Status404NotFound, denied.Response.StatusCode);
        AssertHeaders(denied);
        Assert.Empty(Events(factory));
        var admitted = await SendAsync(factory);
        Assert.Equal(StatusCodes.Status500InternalServerError, admitted.Response.StatusCode);
        Assert.Equal(3, Events(factory).Length);
    }

    [Fact]
    public async Task Probe_MultipleNonceValues_IsQuiet404WithoutConsumingAdmission()
    {
        await using var factory = new ProbeFactory();
        var denied = await SendAsync(factory, values: [Nonce, Nonce]);
        Assert.Equal(StatusCodes.Status404NotFound, denied.Response.StatusCode);
        AssertHeaders(denied);
        Assert.Empty(Events(factory));
        Assert.Equal(StatusCodes.Status500InternalServerError, (await SendAsync(factory)).Response.StatusCode);
    }

    [Theory]
    [InlineData("/INTERNAL/DIAGNOSTICS/OBSERVABILITY", "GET", "AAAAAAAA222233334444555555555555", "aaaaaaaa222233334444555555555555")]
    [InlineData("/internal/diagnostics/observability", "get", "11111111222233334444555555555555", "11111111222233334444555555555555")]
    [InlineData("/internal/diagnostics/observability", "GET", " 11111111222233334444555555555555 ", "11111111222233334444555555555555")]
    public async Task Probe_NativePathMethodAndGuidForms_PreserveNormalizedAdmission(string path, string method, string value, string normalized)
    {
        await using var factory = new ProbeFactory();
        var response = await SendAsync(factory, path: path, method: method, values: [value]);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.Response.StatusCode);
        AssertHeaders(response, normalized);
        Assert.Equal(3, Events(factory, normalized).Length);
    }

    [Theory]
    [InlineData("/internal/diagnostics/observability/")]
    [InlineData("/internal/diagnostics/observability/child")]
    public async Task Probe_NonMatchingPath_FallsThroughWithoutProbeHeadersOrEvents(string path)
    {
        await using var factory = new ProbeFactory();
        var response = await SendAsync(factory, path: path);
        Assert.Equal(StatusCodes.Status404NotFound, response.Response.StatusCode);
        Assert.False(response.Response.Headers.ContainsKey("X-Maliev-Diagnostic-Id"));
        Assert.Empty(Events(factory));
    }

    [Theory]
    [InlineData("203.0.113.5", "127.0.0.1", 404)]
    [InlineData("203.0.113.6", "127.0.0.1", 404)]
    [InlineData("127.0.0.1", "203.0.113.6", 500)]
    public async Task Probe_BeforeStandardForwarding_UsesCurrentPeerNotForwardedHeader(string peer, string forwarded, int status)
    {
        await using var factory = new ProbeFactory();
        var response = await SendAsync(factory, peer: peer, forwarded: forwarded);
        Assert.Equal(status, response.Response.StatusCode);
        Assert.Equal(status == 500 ? 3 : 0, Events(factory).Length);
    }

    [Fact]
    public async Task Probe_MinuteGate_RejectsUntilExact60SecondsAndDoesNotEchoRejectedNonce()
    {
        await using var factory = new ProbeFactory();
        var first = await SendAsync(factory);
        Assert.Equal(StatusCodes.Status500InternalServerError, first.Response.StatusCode);
        var repeated = await SendAsync(factory, values: ["aaaaaaaa222233334444555555555555"]);
        Assert.Equal(StatusCodes.Status429TooManyRequests, repeated.Response.StatusCode);
        AssertHeaders(repeated);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(59999));
        var early = await SendAsync(factory);
        Assert.Equal(StatusCodes.Status429TooManyRequests, early.Response.StatusCode);
        AssertHeaders(early);
        Assert.Equal(3, Events(factory).Length);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var next = await SendAsync(factory, values: ["bbbbbbbb222233334444555555555555"]);
        Assert.Equal(StatusCodes.Status500InternalServerError, next.Response.StatusCode);
        AssertHeaders(next, "bbbbbbbb222233334444555555555555");
        Assert.Equal(6, Events(factory).Length);
    }

    [Fact]
    public async Task Probe_ConcurrentValidRequests_AdmitsOnePerApplicationInstance()
    {
        await using var factory = new ProbeFactory();
        using var bootstrap = factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SendAsync(factory)));
        Assert.Single(responses, response => response.Response.StatusCode == 500);
        Assert.Equal(7, responses.Count(response => response.Response.StatusCode == 429));
        Assert.Equal(3, Events(factory).Length);
    }

    [Fact]
    public async Task Probe_SeparateApplicationInstance_DoesNotShareStaticRateState()
    {
        await using var first = new ProbeFactory();
        await using var second = new ProbeFactory();
        Assert.Equal(500, (await SendAsync(first)).Response.StatusCode);
        Assert.Equal(500, (await SendAsync(second)).Response.StatusCode);
        Assert.Equal(3, Events(first).Length);
        Assert.Equal(3, Events(second).Length);
    }

    [Fact]
    public async Task Probe_BackwardsUtcClock_RetainsOriginalWallClockAdmissionRule()
    {
        await using var factory = new ProbeFactory();
        var initial = factory.Clock.GetUtcNow();
        Assert.Equal(500, (await SendAsync(factory)).Response.StatusCode);
        factory.Clock.Now = initial - TimeSpan.FromSeconds(1);
        Assert.Equal(429, (await SendAsync(factory)).Response.StatusCode);
        factory.Clock.Now = initial + TimeSpan.FromSeconds(60);
        Assert.Equal(500, (await SendAsync(factory)).Response.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Probe_ActualRegisteredSdkHandler_EmitsWarningRealErrorCriticalWithPrivateConsole(string environment)
    {
        await using var factory = new ProbeFactory(environment);
        var response = await SendAsync(factory);
        Assert.Equal(500, response.Response.StatusCode);
        AssertHeaders(response, Nonce);
        Assert.Equal("application/problem+json", response.Response.ContentType);
        Assert.Equal(string.Empty, await new StreamReader(response.Response.Body).ReadToEndAsync());
        Assert.Equal(0, factory.Stealer.Calls);
        var events = Events(factory, Nonce);
        Assert.Equal(new[] { "WARNING", "ERROR", "CRITICAL" },
            events.Select(entry => entry.GetProperty("severity").GetString()));
        Assert.Equal("ObservabilityPipelineProbe", events[0].GetProperty("EventName").GetString());
        Assert.Equal("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", events[1].GetProperty("Category").GetString());
        Assert.EndsWith(".CountryObservabilityDiagnosticException", events[1].GetProperty("exceptionType").GetString());
        Assert.Equal("UnhandledRequestFailure", events[2].GetProperty("EventName").GetString());
        Assert.Equal(500, events[2].GetProperty("StatusCode").GetInt32());
        foreach (var entry in events)
        {
            Assert.True(entry.GetProperty("Synthetic").GetBoolean());
            Assert.Equal(Nonce, entry.GetProperty("DiagnosticId").GetString());
            Assert.Equal("Application diagnostic; see event, exception and trace metadata", entry.GetProperty("Message").GetString());
            Assert.DoesNotContain(Private, entry.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("Controlled loopback", entry.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("{OriginalFormat}", entry.GetRawText(), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("HandledOperationFailure", StringComparison.Ordinal)
            || line.Contains("HealthProbeFailure", StringComparison.Ordinal) || line.Contains("DependencyRequestFailure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonMatchingRequest_ConventionalMiddleware_PreservesNextContextAndResponse()
    {
        using var diagnostics = new DiagnosticListener("Country67.Unit");
        using var logs = LoggerFactory.Create(_ => { });
        var context = new DefaultHttpContext();
        context.Request.Path = "/Countries";
        context.Request.Method = "DELETE";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.6");
        context.Response.Headers["X-Control"] = "unchanged";
        int calls = 0;
        var middleware = new CountryObservabilityProbeMiddleware(current =>
        {
            Assert.Same(context, current);
            Assert.Equal("203.0.113.6", current.Connection.RemoteIpAddress!.ToString());
            Assert.Equal("DELETE", current.Request.Method);
            current.Response.StatusCode = 403;
            calls++;
            return Task.CompletedTask;
        }, logs, diagnostics, new ProbeClock());
        await middleware.InvokeAsync(context);
        Assert.Equal(1, calls);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("unchanged", context.Response.Headers["X-Control"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
        Assert.False(context.Response.Headers.ContainsKey("X-Maliev-Diagnostic-Id"));
    }

    private static async Task<HttpContext> SendAsync(ProbeFactory factory, string path = Path, string method = "GET",
        string? peer = "127.0.0.1", string[]? values = null, string? forwarded = null)
    {
        using var bootstrap = factory.CreateClient();
        return await factory.Server.SendAsync(context =>
        {
            context.Request.Path = path;
            context.Request.Method = method;
            context.Request.QueryString = new QueryString("?token=" + Private);
            context.Request.Headers["Authorization"] = "Bearer " + Private;
            context.Request.Headers["X-Private-Test"] = Private;
            context.Request.Headers["X-Maliev-Diagnostic-Id"] = new StringValues(values ?? [Nonce]);
            if (forwarded is not null) context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
            context.Response.Headers.CacheControl = "public";
            context.Response.Headers["X-Robots-Tag"] = "index";
        });
    }

    private static void AssertHeaders(HttpContext context, string? nonce = null)
    {
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("noindex", context.Response.Headers["X-Robots-Tag"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("X-Maliev-Health-Instance"));
        if (nonce is null) Assert.False(context.Response.Headers.ContainsKey("X-Maliev-Diagnostic-Id"));
        else Assert.Equal(nonce, context.Response.Headers["X-Maliev-Diagnostic-Id"].ToString());
    }

    private static JsonElement[] Events(ProbeFactory factory, string? nonce = null) => factory.Logs.Lines.Select(line =>
    {
        using var json = JsonDocument.Parse(line);
        return json.RootElement.Clone();
    }).Where(entry => entry.TryGetProperty("Synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True
        && (nonce is null || entry.GetProperty("DiagnosticId").GetString() == nonce)).ToArray();

    // Source diagnostics tests use a mutable UTC TimeProvider; no timers or new clock framework.
    private sealed class ProbeClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        internal void Advance(TimeSpan elapsed) => Now += elapsed;
    }

    private sealed class ProbeFactory(string environment = "Production") : WebApplicationFactory<Program>
    {
        private readonly RSA _key = RSA.Create(2048);
        internal ProbeClock Clock { get; } = new();
        internal CaptureProvider Logs { get; } = new();
        internal StealingHandler Stealer { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var pair in new Dictionary<string, string?>
            {
                ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=country67;Username=fixture",
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://country67.invalid",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "country67",
                ["Jwt:Audience"] = "country67",
                ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.5",
                // This test certifies placement before Country's own forwarding, not arbitrary upstream startup filters.
                ["ForwardedHeaders_Enabled"] = "false",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:TracingEnabled"] = "false",
                ["Observability:RuntimeMetricsEnabled"] = "false"
            }) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
                services.AddSingleton<IExceptionHandler>(Stealer);
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
            if (disposing) _key.Dispose();
        }
    }

    private sealed class StealingHandler : IExceptionHandler
    {
        internal int Calls;
        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            context.Response.StatusCode = 418;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class CaptureProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        internal ConcurrentQueue<string> Lines { get; } = new();
        internal ConsoleFormatter Formatter { get; set; } = null!;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(CaptureProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                using var writer = new StringWriter();
                provider.Formatter.Write(new LogEntry<TState>(level, category, eventId, state, exception, formatter), provider._scopes, writer);
                if (writer.GetStringBuilder().Length != 0) provider.Lines.Enqueue(writer.ToString());
            }
        }
    }
}
