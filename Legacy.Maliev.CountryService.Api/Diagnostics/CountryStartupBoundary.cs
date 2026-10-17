using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Legacy.Maliev.CountryService.Api.Diagnostics;

/// <summary>Reports fatal Country host initialization using private source diagnostic metadata.</summary>
public static class CountryStartupBoundary
{
    /// <summary>Attempts one private event5102 emission and preserves a failing process exit code.</summary>
    /// <param name="exception">The host initialization failure; messages and stacks are never emitted.</param>
    public static void ReportFailure(Exception exception)
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var payload = new Dictionary<string, object?>
            {
                ["severity"] = "CRITICAL",
                ["occurredAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["logger"] = "HostStartup",
                ["eventId"] = 5102,
                ["message"] = "Application diagnostic; see event, exception and trace metadata",
                ["service"] = assembly?.GetName().Name,
                ["deploymentVersion"] = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                ["exceptionType"] = exception.GetType().FullName,
                ["innerExceptionType"] = exception.InnerException?.GetType().FullName,
                ["sourceLocation"] = exception.TargetSite?.DeclaringType?.FullName + "." + exception.TargetSite?.Name,
                ["EventName"] = "StartupFailure",
                ["Operation"] = "HostInitialization"
            };
            if (Activity.Current is { } activity && activity.TraceId != default)
            {
                payload["traceId"] = activity.TraceId.ToHexString();
                payload["spanId"] = activity.SpanId.ToHexString();
            }
            Console.Error.WriteLine(JsonSerializer.Serialize(payload));
            Console.Error.Flush();
        }
        catch (Exception)
        {
            // An unavailable diagnostic sink cannot replace the original failure with
            // plaintext fallback or a successful exit. No second sink or retry is used.
        }
        finally
        {
            Environment.ExitCode = 1;
        }
    }
}
