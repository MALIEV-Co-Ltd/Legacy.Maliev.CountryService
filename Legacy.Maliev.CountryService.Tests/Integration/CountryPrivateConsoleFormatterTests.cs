using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryPrivateConsoleFormatterTests
{
    private const string Private = "country65-private-marker";
    private const string IncidentCategory = "Maliev.Aspire.ServiceDefaults.Middleware.ExceptionHandlingMiddleware";
    private const string FixedMessage = "Application diagnostic; see event, exception and trace metadata";

    [Theory]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRITICAL")]
    public void Write_FailureLevels_EmitsFixedPrivateNativeAndSourceFields(LogLevel level, string severity)
    {
        var fields = new Dictionary<string, object?>
        {
            ["EventName"] = "DependencyRequestFailure",
            ["Dependency"] = "IAMService",
            ["Operation"] = "HttpRequest",
            ["StatusCode"] = 503,
            ["ElapsedMs"] = 123L,
            ["AttemptCount"] = 3,
            ["Body"] = Private,
            ["Email"] = Private,
            ["Path"] = "/" + Private,
            ["{OriginalFormat}"] = Private
        };
        using var scopes = new ScopeFixture(new Dictionary<string, object?> { ["Header"] = Private });
        var output = Format(level, fields, new InvalidOperationException(Private), scopes.Provider);
        Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var json = JsonDocument.Parse(output);
        var entry = json.RootElement;
        Assert.Equal(severity, entry.GetProperty("severity").GetString());
        Assert.Equal(level.ToString(), entry.GetProperty("LogLevel").GetString());
        Assert.Equal(5101, entry.GetProperty("eventId").GetInt32());
        Assert.Equal(5101, entry.GetProperty("EventId").GetInt32());
        Assert.Equal("CountryService", entry.GetProperty("Category").GetString());
        Assert.Equal("CountryService", entry.GetProperty("logger").GetString());
        Assert.Equal(FixedMessage, entry.GetProperty("message").GetString());
        Assert.Equal(FixedMessage, entry.GetProperty("Message").GetString());
        Assert.Equal(entry.GetProperty("Timestamp").GetString(), entry.GetProperty("occurredAtUtc").GetString());
        Assert.True(DateTimeOffset.TryParse(entry.GetProperty("occurredAtUtc").GetString(),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurred));
        Assert.Equal(TimeSpan.Zero, occurred.Offset);
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("service").GetString()));
        Assert.True(entry.TryGetProperty("deploymentVersion", out _));
        Assert.Equal("InvalidOperationException", entry.GetProperty("Exception").GetString());
        Assert.Equal("System.InvalidOperationException", entry.GetProperty("exceptionType").GetString());
        Assert.Equal(503, entry.GetProperty("StatusCode").GetInt32());
        Assert.Equal(123L, entry.GetProperty("ElapsedMs").GetInt64());
        Assert.Equal(3, entry.GetProperty("AttemptCount").GetInt32());
        Assert.Equal(new[] { "AttemptCount", "Dependency", "ElapsedMs", "EventName", "Operation", "StatusCode" },
            entry.GetProperty("State").EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Private, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.None)]
    public void Write_BelowWarningOrNone_EmitsNothingWithoutCallingFormatter(LogLevel level)
    {
        Assert.Equal(string.Empty, Format(level, Private, new Exception(Private)));
    }

    [Fact]
    public void Write_AllowedScopeFields_ValidatesAndNormalizesWithoutRawScopeText()
    {
        using var scopes = new ScopeFixture(
            Private,
            new Dictionary<string, object?>
            {
                ["Operation"] = "HttpRequest",
                ["CorrelationId"] = "11111111-2222-3333-4444-555555555555",
                ["TraceId"] = "0123456789abcdef0123456789abcdef",
                ["SpanId"] = "0123456789abcdef",
                ["Synthetic"] = true,
                ["DiagnosticId"] = "AAAAAAAA222233334444555555555555",
                ["ExceptionType"] = "ScopeType",
                ["RequestPath"] = "/" + Private,
                ["Header"] = Private
            });
        var output = Format(LogLevel.Warning, new Dictionary<string, object?> { ["EventName"] = "Probe" },
            scopes: scopes.Provider);
        using var json = JsonDocument.Parse(output);
        var entry = json.RootElement;
        Assert.Equal("11111111222233334444555555555555", entry.GetProperty("CorrelationId").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", entry.GetProperty("TraceId").GetString());
        Assert.Equal("0123456789abcdef", entry.GetProperty("SpanId").GetString());
        Assert.True(entry.GetProperty("Synthetic").GetBoolean());
        Assert.Equal("aaaaaaaa222233334444555555555555", entry.GetProperty("DiagnosticId").GetString());
        Assert.Equal("HttpRequest", entry.GetProperty("Operation").GetString());
        Assert.False(entry.TryGetProperty("exceptionType", out _));
        Assert.Single(entry.GetProperty("Scopes").EnumerateArray());
        Assert.DoesNotContain(Private, output, StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeType", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_MalformedFieldsAndWrongPrimitiveTypes_DropsThem()
    {
        var fields = new Dictionary<string, object?>
        {
            ["StatusCode"] = "503",
            ["ElapsedMs"] = 1.5,
            ["AttemptCount"] = 1U,
            ["EventName"] = "Invalid Name",
            ["Dependency"] = "https://private.invalid",
            ["Operation"] = new string('A', 97),
            ["Method"] = "GET " + Private,
            ["TraceId"] = "ABCDEF0123456789ABCDEF0123456789",
            ["SpanId"] = "short",
            ["CorrelationId"] = Private,
            ["DiagnosticId"] = "not-a-guid",
            ["Synthetic"] = "true",
            ["ExceptionType"] = "Type With Spaces",
            ["Unknown"] = new ThrowingText()
        };
        var output = Format(LogLevel.Error, fields);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("State").ValueKind);
        foreach (var name in fields.Keys)
            Assert.False(json.RootElement.TryGetProperty(name, out _));
        Assert.DoesNotContain(Private, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_DuplicateStateAndScopes_CannotOverrideEnvelopeOrFirstStateValue()
    {
        var fields = new[]
        {
            KeyValuePair.Create<string, object?>("EventName", "First"),
            KeyValuePair.Create<string, object?>("EventName", "Second"),
            KeyValuePair.Create<string, object?>("Message", Private),
            KeyValuePair.Create<string, object?>("severity", Private)
        };
        using var scopes = new ScopeFixture(new Dictionary<string, object?> { ["EventName"] = "Scope" });
        using var json = JsonDocument.Parse(Format(LogLevel.Error, fields, scopes: scopes.Provider));
        var entry = json.RootElement;
        Assert.Equal("First", entry.GetProperty("EventName").GetString());
        Assert.Equal("First", entry.GetProperty("State").GetProperty("EventName").GetString());
        Assert.Equal(FixedMessage, entry.GetProperty("Message").GetString());
        Assert.Equal("ERROR", entry.GetProperty("severity").GetString());
        Assert.Equal(entry.EnumerateObject().Count(), entry.EnumerateObject().Select(field => field.Name).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_ExceptionType_ActualExceptionWinsAndScopesNeverSupplyType(bool actualException)
    {
        using var scopes = new ScopeFixture(new Dictionary<string, object?> { ["ExceptionType"] = "ScopeType" });
        var fields = new Dictionary<string, object?> { ["ExceptionType"] = "PostgresException" };
        using var json = JsonDocument.Parse(Format(LogLevel.Critical, fields,
            actualException ? new InvalidOperationException(Private, new IOException(Private)) : null,
            scopes.Provider));
        var entry = json.RootElement;
        Assert.Equal(actualException ? "System.InvalidOperationException" : "PostgresException",
            entry.GetProperty("exceptionType").GetString());
        if (actualException)
        {
            Assert.Equal("System.IO.IOException", entry.GetProperty("innerExceptionType").GetString());
            Assert.False(entry.GetProperty("State").ValueKind == JsonValueKind.Object
                && entry.GetProperty("State").TryGetProperty("ExceptionType", out _));
        }
        Assert.DoesNotContain("ScopeType", entry.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Private, entry.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CurrentIncident_OnlyPreservesIndependentlyValidatedCompatibilityFields()
    {
        var context = IncidentContext();
        using var json = JsonDocument.Parse(Format(LogLevel.Critical, IncidentFields(),
            context: context, category: IncidentCategory));
        var state = json.RootElement.GetProperty("State");
        Assert.Equal("Legacy.Maliev.CountryService.Api", state.GetProperty("Service").GetString());
        Assert.Equal("Countries", state.GetProperty("Path").GetString());
        Assert.Equal("GET", state.GetProperty("Method").GetString());
        Assert.Equal(500, state.GetProperty("StatusCode").GetInt32());
        Assert.Equal("PostgresException", state.GetProperty("ExceptionType").GetString());
        Assert.Equal("0HN1234567890:00000001", state.GetProperty("IncidentId").GetString());
        Assert.Equal("2026-10-17T01:02:03.0000000+00:00", state.GetProperty("OccurredAtUtc").GetString());
    }

    [Theory]
    [InlineData("category")]
    [InlineData("event")]
    [InlineData("context")]
    [InlineData("mismatch")]
    public void Write_UntrustedIncidentProducer_DropsCompatibilityOnlyFields(string invalid)
    {
        var fields = IncidentFields();
        if (invalid == "event") fields["EventName"] = "OtherEvent";
        var context = invalid == "context" ? null : IncidentContext();
        if (invalid == "mismatch") context!.TraceIdentifier = "DIFFERENT:00000001";
        var output = Format(LogLevel.Critical, fields, context: context,
            category: invalid == "category" ? "OtherCategory" : IncidentCategory);
        using var json = JsonDocument.Parse(output);
        foreach (var key in new[] { "Service", "Path", "IncidentId", "OccurredAtUtc" })
            Assert.False(json.RootElement.GetProperty("State").TryGetProperty(key, out _));
    }

    [Fact]
    public void Write_InjectedIncidentValues_DoesNotCopyPrimitiveState()
    {
        var context = IncidentContext();
        context.Request.Path = "/" + Private;
        var fields = IncidentFields();
        fields["Service"] = Private;
        fields["Path"] = "/" + Private;
        fields["OccurredAtUtc"] = Private;
        var output = Format(LogLevel.Critical, fields, context: context, category: IncidentCategory);
        using var json = JsonDocument.Parse(output);
        foreach (var key in new[] { "Service", "OccurredAtUtc" })
            Assert.False(json.RootElement.GetProperty("State").TryGetProperty(key, out _));
        Assert.Equal("/", json.RootElement.GetProperty("State").GetProperty("Path").GetString());
        Assert.DoesNotContain(Private, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_UnmatchedRoute_UsesFixedFallbackNotLiteralRequestedPath()
    {
        var context = IncidentContext();
        context.SetEndpoint(null);
        context.Request.Path = "/" + Private;
        var fields = IncidentFields();
        fields["Path"] = "/";
        using var json = JsonDocument.Parse(Format(LogLevel.Critical, fields,
            context: context, category: IncidentCategory));
        Assert.Equal("/", json.RootElement.GetProperty("State").GetProperty("Path").GetString());
        Assert.DoesNotContain(Private, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ActiveTrace_UsesRuntimeTraceAndSpanWithoutArbitraryState()
    {
        using var activity = new Activity("Country65").SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
        using var json = JsonDocument.Parse(Format(LogLevel.Error));
        Assert.Equal("11111111111111111111111111111111", json.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), json.RootElement.GetProperty("spanId").GetString());
    }

    [Fact]
    public void Write_NonStructuredStateAndInvalidCategory_UsesFixedMessageAndCategory()
    {
        var output = Format(LogLevel.Warning, Private, category: "Invalid category/" + Private);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("ConfiguredLogger", json.RootElement.GetProperty("Category").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("State").ValueKind);
        Assert.DoesNotContain(Private, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_FieldTraversal_IsBoundedWithoutArbitraryObjectFormatting()
    {
        var fields = Enumerable.Range(0, 64).Select(index => KeyValuePair.Create<string, object?>("Unknown" + index, new ThrowingText()))
            .Append(KeyValuePair.Create<string, object?>("EventName", "OutsideBudget"));
        using var json = JsonDocument.Parse(Format(LogLevel.Warning, fields));
        Assert.False(json.RootElement.TryGetProperty("EventName", out _));
    }

    [Fact]
    public void Write_ScopeTraversal_StopsAtOwnBudgetOnly()
    {
        var provider = new CountingScopes();
        Format(LogLevel.Warning, scopes: provider);
        Assert.Equal(16, provider.Visited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_FailingWriterOrScopeProvider_PropagatesWithoutFallback(bool failingScope)
    {
        var expected = new IOException(Private);
        var formatter = new CountryPrivateConsoleFormatter(new HttpContextAccessor());
        var entry = new LogEntry<string>(LogLevel.Error, "CountryService", default, Private, null,
            (_, _) => throw new InvalidOperationException("Formatter must never execute."));
        using var writer = failingScope ? new StringWriter() : new FailingWriter(expected);
        var escaped = Record.Exception(() => formatter.Write(entry,
            failingScope ? new FailingScopes(expected) : null, writer));
        Assert.Same(expected, escaped);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public async Task ActualHost_SelectedConsoleKeepsOtherProvidersAndGlobalInformationBehavior()
    {
        await using var factory = new ConsoleFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/countries/liveness");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = factory.Services.GetServices<ILoggerProvider>().ToArray();
        Assert.Single(providers.OfType<ConsoleLoggerProvider>());
        Assert.Contains(providers, provider => provider.GetType().Name == "OpenTelemetryLoggerProvider");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        Assert.Equal("country-private-console-json", options.FormatterName);
        var selected = Assert.IsType<CountryPrivateConsoleFormatter>(
            factory.Services.GetServices<ConsoleFormatter>().Single(formatter => formatter.Name == options.FormatterName));
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Country65.Acceptance");
        logger.LogInformation("Other providers retain {Marker}", Private);
        Assert.Contains(LogLevel.Information, factory.Other.Levels);
        using var writer = new StringWriter();
        var entry = new LogEntry<string>(LogLevel.Information, "Country65.Acceptance", default, Private, null, (_, _) => Private);
        selected.Write(entry, null, writer);
        Assert.Equal(string.Empty, writer.ToString());
        using var synthetic = await client.GetAsync("/internal/diagnostics/observability");
        Assert.Equal(HttpStatusCode.NotFound, synthetic.StatusCode);
    }

    private static string Format(LogLevel level, object? state = null, Exception? exception = null,
        IExternalScopeProvider? scopes = null, HttpContext? context = null, string category = "CountryService")
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        try
        {
            var formatter = new CountryPrivateConsoleFormatter(accessor);
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            formatter.Write(new LogEntry<object?>(level, category, new EventId(5101), state, exception,
                (_, _) => throw new InvalidOperationException("Arbitrary formatter must never execute.")), scopes, writer);
            return writer.ToString();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static DefaultHttpContext IncidentContext()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "0HN1234567890:00000001" };
        context.Request.Method = "GET";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("Countries"),
            0, EndpointMetadataCollection.Empty, "Countries"));
        return context;
    }

    private static Dictionary<string, object?> IncidentFields() => new()
    {
        ["EventName"] = "UnhandledRequestFailure",
        ["Service"] = "Legacy.Maliev.CountryService.Api",
        ["Method"] = "GET",
        ["Path"] = "Countries",
        ["StatusCode"] = 500,
        ["ExceptionType"] = "PostgresException",
        ["IncidentId"] = "0HN1234567890:00000001",
        ["OccurredAtUtc"] = "2026-10-17T01:02:03.0000000+00:00"
    };

    private sealed class ScopeFixture : IDisposable
    {
        private readonly List<IDisposable> _scopes = [];
        internal LoggerExternalScopeProvider Provider { get; } = new();
        internal ScopeFixture(params object[] scopes)
        {
            foreach (var scope in scopes) _scopes.Add(Provider.Push(scope));
        }
        public void Dispose()
        {
            foreach (var scope in _scopes.AsEnumerable().Reverse()) scope.Dispose();
        }
    }

    private sealed class ThrowingText
    {
        public override string ToString() => throw new InvalidOperationException("Untrusted ToString must never execute.");
    }

    private sealed class FailingWriter(Exception failure) : StringWriter
    {
        public override void WriteLine(string? value) => throw failure;
    }

    private sealed class FailingScopes(Exception failure) : IExternalScopeProvider
    {
        public IDisposable Push(object? state) => throw failure;
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => throw failure;
    }

    private sealed class CountingScopes : IExternalScopeProvider
    {
        internal int Visited { get; private set; }
        public IDisposable Push(object? state) => throw new NotSupportedException();
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
            for (int index = 0; index < 100; index++)
            {
                Visited++;
                callback(new Dictionary<string, object?> { ["Operation"] = "HttpRequest" }, state);
            }
        }
    }

    private sealed class ConsoleFactory : WebApplicationFactory<Program>
    {
        private readonly RSA _key = RSA.Create(2048);
        internal OtherProvider Other { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var pair in new Dictionary<string, string?>
            {
                ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=country65;Username=fixture",
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://country65.invalid",
                ["Logging:LogLevel:Country65.Acceptance"] = "Information",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "country65",
                ["Jwt:Audience"] = "country65",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:TracingEnabled"] = "false",
                ["Observability:RuntimeMetricsEnabled"] = "false"
            }) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureTestServices(services =>
            {
                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
                services.AddSingleton<ILoggerProvider>(Other);
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _key.Dispose();
        }
    }

    private sealed class OtherProvider : ILoggerProvider
    {
        internal ConcurrentQueue<LogLevel> Levels { get; } = new();
        public ILogger CreateLogger(string categoryName) => new OtherLogger(this);
        public void Dispose() { }
        private sealed class OtherLogger(OtherProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => provider.Levels.Enqueue(logLevel);
        }
    }
}
