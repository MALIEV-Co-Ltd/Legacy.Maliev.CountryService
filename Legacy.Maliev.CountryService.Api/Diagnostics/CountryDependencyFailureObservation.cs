using System.Text.RegularExpressions;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CountryService.Api.Diagnostics;

/// <summary>Selects one outer source terminal observer for the two Country-owned HTTP clients.</summary>
public sealed class CountryDependencyFailureObservation : IHttpMessageHandlerBuilderFilter
{
    /// <summary>Preserves all configured handlers before placing observation outside retry and the existing guard.</summary>
    /// <param name="next">The existing factory configuration pipeline.</param>
    /// <returns>The unchanged pipeline with selected terminal observation.</returns>
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        var name = builder.Name;
        if (name is not ("IAMService" or LegacyServiceAccessTokenProvider.HttpClientName)) return;
        builder.AdditionalHandlers.Insert(0, new CountryDependencyFailureHandler(
            builder.Services.GetRequiredService<ILogger<CountryDependencyFailureHandler>>(), name));
    };
}

/// <summary>Records only fixed terminal source metadata without changing a dependency outcome.</summary>
/// <param name="logger">The existing host logger; caught exception objects are never supplied.</param>
/// <param name="dependency">The bounded code-owned dependency name.</param>
public sealed class CountryDependencyFailureHandler(ILogger<CountryDependencyFailureHandler> logger, string dependency)
    : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<bool> CallerFailureLogging = new("Maliev.DependencyFailureOwnedByCaller");
    private readonly string _dependency = Regex.IsMatch(dependency, "^[A-Za-z][A-Za-z0-9_.-]{0,95}$", RegexOptions.CultureInvariant)
        ? dependency : "ConfiguredHttpClient";

    /// <summary>Returns or rethrows the same terminal outcome after source-compatible observation.</summary>
    /// <param name="request">The unchanged outbound request.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The unchanged downstream response.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode >= 500 && !CallerOwnsFailure(request)) Record((int)response.StatusCode);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            if (!CallerOwnsFailure(request)) Record(exception is OperationCanceledException or TimeoutException ? 504 : 503);
            throw;
        }
    }

    private static bool CallerOwnsFailure(HttpRequestMessage request) =>
        request.Options.TryGetValue(CallerFailureLogging, out bool owned) && owned;

    private void Record(int status)
    {
        try
        {
            // Source event fields only. Never export exceptions, request/response contents or arbitrary state.
            logger.LogError(new EventId(5101, "DependencyRequestFailure"),
                "{EventName} Dependency={Dependency} Operation={Operation} StatusCode={StatusCode}",
                "DependencyRequestFailure", _dependency, "HttpRequest", status);
        }
        catch (Exception)
        {
            // Logging failure must not replace the original response/exception or recursively disclose it.
        }
    }
}
