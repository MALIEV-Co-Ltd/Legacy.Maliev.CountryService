using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Actual entry-point fatal configuration and minimal-host tooling boundaries, with no database or helper service.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryFatalStartupAcceptanceTests
{
    private const string Sentinel = "private-country59-startup-marker";

    /// <summary>Failures before host logging and while configuring services must emit one private failure and exit1.</summary>
    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-root")]
    [InlineData("invalid-jwt")]
    public async Task FatalConfiguration_ActualEntryPoint_EmitsPrivate5102AndExitsOne(string phase)
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "appsettings.json"),
                phase == "malformed" ? "{ invalid-" + Sentinel : JsonSerializer.Serialize(Settings(phase)));
            var api = typeof(Program).Assembly.Location;
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            var allowed = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE",
                "DOTNET_ROOT", "DOTNET_ROOT_X64", "ProgramFiles" };
            var system = allowed.Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name))).ToArray();
            start.Environment.Clear();
            foreach (var (name, value) in system)
                if (value is not null) start.Environment[name] = value;
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
            foreach (var argument in new[] { api, "--environment=Production", "--contentRoot=" +
                (phase == "missing-root" ? Path.Combine(root, Sentinel) : root) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Owned Country startup process did not start.");
            var startedAt = process.StartTime;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            string output;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                output = await stdout + await stderr;
                Assert.Equal(1, process.ExitCode);
            }
            finally
            {
                if (!process.HasExited && process.StartTime == startedAt)
                    process.Kill();
                if (!process.HasExited)
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                deadline.Cancel();
                try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) { /* Keep the original timeout/read failure; no process is left running. */ }
            }
            Assert.DoesNotContain(Sentinel, output, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", output, StringComparison.OrdinalIgnoreCase);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Single(lines);
            using var json = JsonDocument.Parse(lines[0]);
            AssertPrivateFailure(json.RootElement);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A healthy normal host remains interceptable; no success or authorization semantics change.</summary>
    [Fact]
    public async Task HealthyHost_NormalPipeline_PreservesLivenessAuthorizationAndExitCode()
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 37;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                foreach (var setting in Settings("healthy"))
                    builder.UseSetting(setting.Key, setting.Value);
            });
            using var client = factory.CreateClient();
            using var live = await client.GetAsync("/countries/liveness");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            using var denied = await client.PostAsync("/Countries", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal(37, Environment.ExitCode);
        }
        finally { Environment.ExitCode = previous; }
    }

    /// <summary>Minimal-host tooling abort is deliberately rethrown, unlike the historical catch-all host wrapper.</summary>
    [Fact]
    public async Task ActualEntryPoint_HostAborted_RethrowsWithoutFailureOutputOrExitChange()
    {
        var root = CreateRoot();
        var previous = Environment.ExitCode;
        var oldOut = Console.Out;
        var oldError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var abort = new HostAbortInterceptor();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(Settings("healthy")));
            Environment.ExitCode = 37;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            abort.Enter();
            var escaped = await Record.ExceptionAsync(async () =>
            {
                var entry = typeof(Program).Assembly.EntryPoint ?? throw new InvalidOperationException("Country entry point missing.");
                try
                {
                    var returned = entry.Invoke(null, [new[] { "--environment=Production", "--contentRoot=" + root }]);
                    if (returned is Task task) await task;
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            });
            Assert.Equal(1, abort.Calls);
            Assert.Same(abort.Abort, escaped);
            Assert.Equal(37, Environment.ExitCode);
            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            abort.Exit();
            Console.SetOut(oldOut);
            Console.SetError(oldError);
            Environment.ExitCode = previous;
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Safe reporting handles nested exception metadata without leaking messages or stack text.</summary>
    [Fact]
    public void ReportFailure_NestedException_EmitsOnePrivateSourceEvent()
    {
        var previous = Environment.ExitCode;
        var oldError = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            Environment.ExitCode = 37;
            CountryStartupBoundary.ReportFailure(new InvalidOperationException(Sentinel, new Exception(Sentinel)));
            Assert.Equal(1, Environment.ExitCode);
            Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain(Sentinel, output.ToString(), StringComparison.Ordinal);
            using var json = JsonDocument.Parse(output.ToString());
            AssertPrivateFailure(json.RootElement);
            Assert.Equal(typeof(InvalidOperationException).FullName, json.RootElement.GetProperty("exceptionType").GetString());
            Assert.Equal(typeof(Exception).FullName, json.RootElement.GetProperty("innerExceptionType").GetString());
        }
        finally { Console.SetError(oldError); Environment.ExitCode = previous; }
    }

    /// <summary>An unavailable diagnostic sink cannot escape as plaintext or make failed startup successful.</summary>
    [Fact]
    public void ReportFailure_UnavailableConsoleError_PreservesExitOneWithoutFallbackEmission()
    {
        var previous = Environment.ExitCode;
        var oldError = Console.Error;
        using var sink = new UnavailableWriter();
        try
        {
            Console.SetError(sink);
            Environment.ExitCode = 37;
            var escaped = Record.Exception(() => CountryStartupBoundary.ReportFailure(new InvalidOperationException(Sentinel)));
            Assert.Null(escaped);
            Assert.Equal(1, Environment.ExitCode);
            Assert.Equal(1, sink.Attempts);
        }
        finally { Console.SetError(oldError); Environment.ExitCode = previous; }
    }


    /// <summary>The source's optional trace correlation is retained without activity names or arbitrary state.</summary>
    [Fact]
    public void ReportFailure_ActiveTrace_PreservesOnlySafeTraceIdentifiers()
    {
        var previous = Environment.ExitCode;
        var oldError = Console.Error;
        using var output = new StringWriter();
        using var activity = new Activity(Sentinel).SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
        try
        {
            Console.SetError(output);
            CountryStartupBoundary.ReportFailure(new InvalidOperationException(Sentinel));
            Assert.Equal(1, Environment.ExitCode);
            using var json = JsonDocument.Parse(output.ToString());
            AssertPrivateFailure(json.RootElement);
            Assert.Equal("11111111111111111111111111111111", json.RootElement.GetProperty("traceId").GetString());
            Assert.Equal(activity.SpanId.ToHexString(), json.RootElement.GetProperty("spanId").GetString());
            Assert.DoesNotContain(Sentinel, output.ToString(), StringComparison.Ordinal);
        }
        finally { Console.SetError(oldError); Environment.ExitCode = previous; }
    }

    private static void AssertPrivateFailure(JsonElement entry)
    {
        Assert.Equal("CRITICAL", entry.GetProperty("severity").GetString());
        Assert.Equal(5102, entry.GetProperty("eventId").GetInt32());
        Assert.Equal("StartupFailure", entry.GetProperty("EventName").GetString());
        Assert.Equal("HostInitialization", entry.GetProperty("Operation").GetString());
        Assert.Matches("^[A-Za-z][A-Za-z0-9_.+\u0060]{0,191}$", entry.GetProperty("exceptionType").GetString()!);
        Assert.False(entry.TryGetProperty("State", out _));
        Assert.False(entry.TryGetProperty("Scopes", out _));
        Assert.False(entry.TryGetProperty("StackTrace", out _));
        Assert.True(DateTimeOffset.TryParse(entry.GetProperty("occurredAtUtc").GetString(), out var occurred));
        Assert.Equal(TimeSpan.Zero, occurred.Offset);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "country59-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static Dictionary<string, string?> Settings(string phase)
    {
        using var key = RSA.Create(2048);
        return new()
        {
            ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=country59;Username=fixture",
            ["Cache:RedisEnabled"] = "false",
            ["CORS:AllowedOrigins:0"] = "https://country59.invalid",
            ["Jwt:PublicKey"] = phase == "invalid-jwt" ? Sentinel :
                Convert.ToBase64String(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem())),
            ["Jwt:Issuer"] = "country59",
            ["Jwt:Audience"] = "country59",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["Observability:TracingEnabled"] = "false",
            ["Observability:RuntimeMetricsEnabled"] = "false"
        };
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token);
            if (read == 0) return output.ToString();
            if (output.Length + read > 65536)
                throw new IOException("Owned Country startup output exceeded its fixed capture limit.");
            output.Append(buffer, 0, read);
        }
    }

    private sealed class UnavailableWriter : StringWriter
    {
        internal int Attempts;
        public override void WriteLine(string? value)
        {
            Attempts++;
            throw new IOException(Sentinel);
        }
    }

    private sealed class HostAbortInterceptor : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly AsyncLocal<bool> owned = new();
        private readonly IDisposable listeners;
        private readonly List<IDisposable> subscriptions = [];
        private IHost? host;
        internal HostAbortedException Abort { get; } = new();
        internal int Calls { get; private set; }
        internal HostAbortInterceptor() => listeners = DiagnosticListener.AllListeners.Subscribe(this);
        internal void Enter() => owned.Value = true;
        internal void Exit() => owned.Value = false;
        public void OnNext(DiagnosticListener listener)
        {
            if (owned.Value && listener.Name == "Microsoft.Extensions.Hosting") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> observation)
        {
            if (!owned.Value || observation.Key != "HostBuilt") return;
            host = Assert.IsAssignableFrom<IHost>(observation.Value);
            Calls++;
            throw Abort;
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
            listeners.Dispose();
            host?.Dispose();
        }
    }
}
