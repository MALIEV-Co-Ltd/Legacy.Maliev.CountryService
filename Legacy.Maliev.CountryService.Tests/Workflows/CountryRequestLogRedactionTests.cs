using Maliev.Aspire.ServiceDefaults.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CountryService.Tests.Workflows;

public sealed class CountryRequestLogRedactionTests
{
    [Theory]
    [InlineData(true, "/countries/42", "/countries/{id:int}")]
    [InlineData(false, "/countries/private-customer-token", "/")]
    public async Task CountryRequestLogs_NeverExposeLiteralPathQueryOrHeader(
        bool matched, string requestPath, string expectedLoggedPath)
    {
        var logger = new CaptureLogger();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = requestPath;
        context.Request.QueryString = new QueryString("?token=private-query");
        context.Request.Headers.Authorization = "Bearer private-header";
        if (matched)
        {
            context.SetEndpoint(new RouteEndpointBuilder(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/countries/{id:int}"),
                0).Build());
        }

        var middleware = new RequestLoggingMiddleware(current =>
        {
            current.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Equal(expectedLoggedPath, entry.Values["Path"]);
            Assert.DoesNotContain(requestPath, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-", entry.Message, StringComparison.Ordinal);
        });
        Assert.Equal(StatusCodes.Status204NoContent, logger.Entries[1].Values["StatusCode"]);
    }

    private sealed class CaptureLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<Entry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)state!).Where(pair => pair.Key != "{OriginalFormat}").ToDictionary()));
    }

    private sealed record Entry(string Message, Dictionary<string, object?> Values);
}
