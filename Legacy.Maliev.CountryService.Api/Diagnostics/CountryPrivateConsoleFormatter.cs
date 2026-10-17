using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Legacy.Maliev.CountryService.Api.Diagnostics;

/// <summary>Writes the selected Country console using private source fields and safe native aliases.</summary>
/// <param name="accessor">The existing accessor; request context is read synchronously and never retained.</param>
public sealed class CountryPrivateConsoleFormatter(IHttpContextAccessor accessor) : ConsoleFormatter(FormatterName)
{
    /// <summary>Gets the application-local formatter name; other providers are unchanged.</summary>
    public const string FormatterName = "country-private-console-json";
    private const string FixedMessage = "Application diagnostic; see event, exception and trace metadata";
    private const string IncidentCategory = "Maliev.Aspire.ServiceDefaults.Middleware.ExceptionHandlingMiddleware";
    private const string ServiceName = "Legacy.Maliev.CountryService.Api";

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> entry,
        IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        if (entry.LogLevel is not (LogLevel.Warning or LogLevel.Error or LogLevel.Critical)) return;

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var category = IsIdentifier(entry.Category) ? entry.Category : "ConfiguredLogger";
        var assembly = Assembly.GetEntryAssembly();
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Timestamp"] = now,
            ["EventId"] = entry.EventId.Id,
            ["LogLevel"] = entry.LogLevel.ToString(),
            ["severity"] = entry.LogLevel == LogLevel.Critical ? "CRITICAL" : entry.LogLevel == LogLevel.Error ? "ERROR" : "WARNING",
            ["Category"] = category,
            ["Message"] = FixedMessage,
            ["Exception"] = entry.Exception?.GetType().Name,
            ["occurredAtUtc"] = now,
            ["logger"] = category,
            ["eventId"] = entry.EventId.Id,
            ["message"] = FixedMessage,
            ["service"] = assembly?.GetName().Name,
            ["deploymentVersion"] = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        };
        if (entry.Exception is { } exception)
        {
            payload["exceptionType"] = exception.GetType().FullName;
            payload["innerExceptionType"] = exception.InnerException?.GetType().FullName;
            payload["sourceLocation"] = exception.TargetSite?.DeclaringType?.FullName + "." + exception.TargetSite?.Name;
        }
        if (Activity.Current is { } activity && activity.TraceId != default)
        {
            payload["traceId"] = activity.TraceId.ToHexString();
            payload["spanId"] = activity.SpanId.ToHexString();
        }

        var fields = ReadFields(entry.State);
        var state = SafeFields(fields, entry.Exception is null);
        AddIncidentCompatibility(state, fields, entry.Category);
        Promote(payload, state);
        payload["State"] = state.Count == 0 ? null : state;

        var scopes = new List<Dictionary<string, object?>>();
        if (scopeProvider is not null)
        {
            var stop = new ScopeBudgetReachedException();
            int count = 0;
            try
            {
                scopeProvider.ForEachScope((scope, _) =>
                {
                    var safe = SafeFields(ReadFields(scope), false);
                    if (safe.Count != 0)
                    {
                        scopes.Add(safe);
                        Promote(payload, safe);
                    }
                    if (++count == 16) throw stop;
                }, 0);
            }
            catch (ScopeBudgetReachedException budgetException) when (ReferenceEquals(budgetException, stop))
            {
                // Stop only this traversal's own finite marker; unrelated provider failures propagate.
            }
        }
        payload["Scopes"] = scopes.Count == 0 ? null : scopes;
        textWriter.WriteLine(JsonSerializer.Serialize(payload));
    }

    private void AddIncidentCompatibility(Dictionary<string, object?> state,
        Dictionary<string, object?> fields, string category)
    {
        // ConsoleLogger formats synchronously before queueing text. Buffered/background records
        // without this exact runtime request identity fail closed instead of borrowing another request.
        var context = accessor.HttpContext;
        if (category != IncidentCategory || GetString(fields, "EventName") != "UnhandledRequestFailure"
            || context is null || GetString(fields, "IncidentId") is not { } incident
            || !IsIncidentId(incident) || incident != context.TraceIdentifier) return;

        if (GetString(fields, "Service") == ServiceName) state["Service"] = ServiceName;
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var expectedPath = string.IsNullOrEmpty(pattern) ? "/" : pattern;
        state["Path"] = GetString(fields, "Path") == expectedPath ? expectedPath : "/";
        state["IncidentId"] = incident;
        if (GetString(fields, "OccurredAtUtc") is { } timestamp
            && DateTimeOffset.TryParseExact(timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred)
            && occurred.Offset == TimeSpan.Zero)
        {
            state["OccurredAtUtc"] = occurred.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private static Dictionary<string, object?> ReadFields(object? value)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (value is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            foreach (var field in structured.Take(64)) fields.TryAdd(field.Key, field.Value);
        }
        return fields;
    }

    private static Dictionary<string, object?> SafeFields(Dictionary<string, object?> fields, bool includeExceptionType)
    {
        var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field.Key is "StatusCode" or "ElapsedMs" or "AttemptCount" && field.Value is int or long)
            {
                safe.Add(field.Key, field.Value);
            }
            else if (field.Key is "EventName" or "Dependency" or "Operation" or "Method"
                && field.Value is string identifier && IsIdentifier(identifier))
            {
                safe.Add(field.Key, identifier);
            }
            else if (includeExceptionType && field.Key == "ExceptionType"
                && field.Value is string exceptionType && IsIdentifier(exceptionType))
            {
                safe.Add(field.Key, exceptionType);
            }
            else if (field.Key is "TraceId" or "SpanId" && field.Value is string trace
                && trace.Length is >= 16 and <= 32 && trace.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9'))
            {
                safe.Add(field.Key, trace);
            }
            else if (field.Key == "Synthetic" && field.Value is bool synthetic)
            {
                safe.Add(field.Key, synthetic);
            }
            else if (field.Key == "DiagnosticId" && field.Value is string diagnostic
                && Guid.TryParseExact(diagnostic, "N", out var diagnosticId))
            {
                safe.Add(field.Key, diagnosticId.ToString("N"));
            }
            else if (field.Key == "CorrelationId" && field.Value is string correlation
                && Guid.TryParse(correlation, out var correlationId))
            {
                safe.Add(field.Key, correlationId.ToString("N"));
            }
        }
        return safe;
    }

    private static void Promote(Dictionary<string, object?> payload, Dictionary<string, object?> fields)
    {
        foreach (var field in fields)
        {
            // Compatibility-only native incident fields never become arbitrary source top-level keys.
            if (field.Key is "Service" or "Path" or "IncidentId" or "OccurredAtUtc") continue;
            payload.TryAdd(field.Key == "ExceptionType" ? "exceptionType" : field.Key, field.Value);
        }
    }

    private static string? GetString(Dictionary<string, object?> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value as string : null;

    private static bool IsIdentifier(string value) => value.Length is >= 1 and <= 96
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-');

    private static bool IsIncidentId(string value) => value.Length is >= 1 and <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '_' or '-');

    private sealed class ScopeBudgetReachedException : Exception;
}
