using System.Net;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Source terminal outcomes and private event contract through the actual dependency handler.</summary>
public sealed class CountryDependencyFailureObservationTests
{
    private const string Private = "country63-private-marker";

    [Theory]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(599, true)]
    [InlineData(600, true)]
    public async Task ReturnedStatus_PreservesSameResponseAndSourceAtLeast500Rule(int status, bool observed)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(Private) };
        response.Headers.Add("X-Private", Private);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => Task.FromResult(response));
        using var request = Request();
        var actual = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Same(response, actual);
        Assert.Equal(Private, await actual.Content.ReadAsStringAsync());
        Assert.Equal(Private, Assert.Single(actual.Headers.GetValues("X-Private")));
        if (observed) AssertPrivateEvent(Assert.Single(logger.Events), status);
        else Assert.Empty(logger.Events);
    }

    [Theory]
    [InlineData("network", 503)]
    [InlineData("network-with-404", 503)]
    [InlineData("timeout", 504)]
    [InlineData("noncaller-cancellation", 504)]
    public async Task ThrownTransport_PreservesSameExceptionAndOriginalSourceStatus(string mode, int status)
    {
        var failure = Failure(mode);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var request = Request();
        var escaped = await Record.ExceptionAsync(() => invoker.SendAsync(request, CancellationToken.None));
        Assert.Same(failure, escaped);
        AssertPrivateEvent(Assert.Single(logger.Events), status);
    }

    [Fact]
    public async Task CallerCancellation_RethrowsSameExceptionWithoutObservation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var failure = new OperationCanceledException(Private, cancelled.Token);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var request = Request();
        var escaped = await Record.ExceptionAsync(() => invoker.SendAsync(request, cancelled.Token));
        Assert.Same(failure, escaped);
        Assert.Empty(logger.Events);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("noncaller-cancellation")]
    public async Task CallerOwnedFlag_SuppressesBothReturnedAndThrownSourceObservations(string mode)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var failure = mode == "http" ? null : Failure(mode);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => failure is null
            ? Task.FromResult(response) : Task.FromException<HttpResponseMessage>(failure));
        using var request = Request();
        request.Options.Set(new HttpRequestOptionsKey<bool>("Maliev.DependencyFailureOwnedByCaller"), true);
        if (failure is null) Assert.Same(response, await invoker.SendAsync(request, CancellationToken.None));
        else Assert.Same(failure, await Record.ExceptionAsync(() => invoker.SendAsync(request, CancellationToken.None)));
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task CallerOwnedFalseFlag_RetainsSourceObservation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => Task.FromResult(response));
        using var request = Request();
        request.Options.Set(new HttpRequestOptionsKey<bool>("Maliev.DependencyFailureOwnedByCaller"), false);
        Assert.Same(response, await invoker.SendAsync(request, CancellationToken.None));
        AssertPrivateEvent(Assert.Single(logger.Events), 502);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingLogSink_CannotChangeResponseOrReplaceOriginalException(bool throws)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var failure = new HttpRequestException(Private);
        var logger = new CountryDependencyEventCapture { Fail = true };
        using var invoker = Invoker(logger, (_, _) => throws
            ? Task.FromException<HttpResponseMessage>(failure) : Task.FromResult(response));
        using var request = Request();
        if (throws) Assert.Same(failure, await Record.ExceptionAsync(() => invoker.SendAsync(request, CancellationToken.None)));
        else Assert.Same(response, await invoker.SendAsync(request, CancellationToken.None));
        Assert.Equal(1, logger.Attempts);
    }

    [Theory]
    [InlineData("unhandled")]
    [InlineData("argument")]
    public async Task OtherException_RemainsUnobservedAndUnchanged(string mode)
    {
        Exception failure = mode == "argument" ? new ArgumentException(Private) : new InvalidOperationException(Private);
        var logger = new CountryDependencyEventCapture();
        using var invoker = Invoker(logger, (_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var request = Request();
        Assert.Same(failure, await Record.ExceptionAsync(() => invoker.SendAsync(request, CancellationToken.None)));
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task SourceDependencyName_InvalidArbitraryValueIsNotLogged()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var logger = new CountryDependencyEventCapture();
        using var handler = new CountryDependencyFailureHandler(logger, "https://private.invalid/" + Private)
        { InnerHandler = new CountryDependencyTransport((_, _) => Task.FromResult(response)) };
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = Request();
        Assert.Same(response, await invoker.SendAsync(request, CancellationToken.None));
        var entry = Assert.Single(logger.Events);
        AssertPrivateEvent(entry, 503, "ConfiguredHttpClient");
    }

    private static HttpMessageInvoker Invoker(CountryDependencyEventCapture logger,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new CountryDependencyFailureHandler(logger, "IAMService")
        { InnerHandler = new CountryDependencyTransport(send) });

    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://private.invalid/" + Private + "?private=" + Private)
        { Content = new StringContent(Private) };
        request.Headers.Add("X-Private", Private);
        return request;
    }

    private static Exception Failure(string mode) => mode switch
    {
        "timeout" => new TimeoutException(Private),
        "noncaller-cancellation" => new OperationCanceledException(Private),
        "network-with-404" => new HttpRequestException(Private, null, HttpStatusCode.NotFound),
        _ => new HttpRequestException(Private)
    };

    internal static void AssertPrivateEvent(CountryDependencyEvent entry, int status, string dependency = "IAMService")
    {
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(5101, entry.Id.Id);
        Assert.Equal("DependencyRequestFailure", entry.Id.Name);
        Assert.Null(entry.Exception);
        Assert.Equal("DependencyRequestFailure", entry.Fields["EventName"]);
        Assert.Equal(dependency, entry.Fields["Dependency"]);
        Assert.Equal("HttpRequest", entry.Fields["Operation"]);
        Assert.Equal(status, entry.Fields["StatusCode"]);
        Assert.Equal(new[] { "Dependency", "EventName", "Operation", "StatusCode", "{OriginalFormat}" },
            entry.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Private, entry.Message, StringComparison.Ordinal);
        Assert.All(entry.Fields.Values, value => Assert.DoesNotContain(Private, value?.ToString() ?? "", StringComparison.Ordinal));
    }
}

internal sealed record CountryDependencyEvent(LogLevel Level, EventId Id, Exception? Exception,
    string Message, Dictionary<string, object?> Fields);

internal sealed class CountryDependencyEventCapture : ILogger<CountryDependencyFailureHandler>, ILoggerProvider
{
    private readonly object _gate = new();
    internal List<CountryDependencyEvent> Events { get; } = [];
    internal int Attempts;
    internal bool Fail { get; init; }
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (id.Id != 5101) return;
        lock (_gate)
        {
            Attempts++;
            if (Fail) throw new IOException("country63-private-sink-marker");
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state).ToDictionary(pair => pair.Key, pair => pair.Value);
            Events.Add(new(level, id, exception, formatter(state, exception), fields));
        }
    }
    public void Dispose() { }
}

internal sealed class CountryDependencyTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
