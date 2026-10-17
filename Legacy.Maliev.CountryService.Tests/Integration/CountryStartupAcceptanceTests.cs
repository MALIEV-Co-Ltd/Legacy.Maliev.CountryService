using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryStartupAcceptanceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ProductionStartup_UsesConfiguredPostgresAndEmitsPrivateCorrelatedNativeFailure()
    {
        // No repository/service replacement: the child process runs the actual API entry point.
        var options = new DbContextOptionsBuilder<CountryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using (var context = new CountryDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Countries.Add(new Country { Name = "Thailand", Iso2 = "TH", Iso3 = "THA", CountryCode = "764" });
            await context.SaveChangesAsync();
        }

        await using var api = await ApiProcess.StartAsync(_postgres.GetConnectionString());
        foreach (var route in new[] { "/Countries", "/country/v1/countries" })
        {
            using var response = await api.Client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var country = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal("Thailand", country.GetProperty("name").GetString());
            Assert.Equal("TH", country.GetProperty("iso2").GetString());
        }

        // The same already-running child exercises real Kestrel and the selected console.
        const string probeId = "aaaaaaaa222233334444555555555555";
        using (var probe = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability?token=country67-native-private"))
        {
            probe.Headers.Add("X-Maliev-Diagnostic-Id", probeId);
            probe.Headers.Add("Authorization", "Bearer country67-native-private");
            using var controlled = await api.Client.SendAsync(probe);
            Assert.Equal(HttpStatusCode.InternalServerError, controlled.StatusCode);
            Assert.Equal("application/problem+json", controlled.Content.Headers.ContentType?.MediaType);
            Assert.Equal(string.Empty, await controlled.Content.ReadAsStringAsync());
            Assert.Equal("no-store", controlled.Headers.CacheControl?.ToString());
            Assert.Equal("noindex", Assert.Single(controlled.Headers.GetValues("X-Robots-Tag")));
            Assert.Equal(probeId, Assert.Single(controlled.Headers.GetValues("X-Maliev-Diagnostic-Id")));
            Assert.False(controlled.Headers.Contains("X-Maliev-Health-Instance"));
        }
        await api.WaitForProbeCriticalAsync(probeId);
        var probeEvents = api.ProbeEvents(probeId);
        Assert.Equal(new[] { "WARNING", "ERROR", "CRITICAL" },
            probeEvents.Select(entry => entry.GetProperty("severity").GetString()));
        Assert.Equal("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
            probeEvents[1].GetProperty("Category").GetString());
        Assert.DoesNotContain("country67-native-private", api.Output, StringComparison.Ordinal);
        using (var repeat = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability"))
        {
            repeat.Headers.Add("X-Maliev-Diagnostic-Id", "bbbbbbbb222233334444555555555555");
            using var limited = await api.Client.SendAsync(repeat);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
            Assert.Equal("noindex", Assert.Single(limited.Headers.GetValues("X-Robots-Tag")));
            Assert.False(limited.Headers.Contains("X-Maliev-Diagnostic-Id"));
        }
        using (var ordinary = await api.Client.GetAsync("/Countries"))
        {
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
            Assert.False(ordinary.Headers.Contains("X-Maliev-Diagnostic-Id"));
            Assert.False(ordinary.Headers.Contains("X-Maliev-Health-Instance"));
        }

        // Anonymous write must fail before the real database is touched.
        using (var denied = await api.Client.PostAsync("/Countries", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        // Invalidate the disposable database object, not the service implementation. A fresh
        // process avoids the successful collection cache and exercises real EF/Npgsql failure.
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DROP TABLE \"Country\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var failingApi = await ApiProcess.StartAsync(_postgres.GetConnectionString());
        const string privateMarker = "private-country-acceptance-marker";
        using (var unmatched = await failingApi.Client.GetAsync("/unmatched/" + privateMarker))
        {
            Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Countries?token={privateMarker}");
        request.Headers.Add("X-Private-Test", privateMarker);
        request.Headers.Add("X-Correlation-ID", privateMarker + "/invalid");
        request.Headers.Add("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        using var failure = await failingApi.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        var failureText = await failure.Content.ReadAsStringAsync();
        using var failureBody = JsonDocument.Parse(failureText);
        var incidentId = failureBody.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(incidentId));
        Assert.Equal(JsonValueKind.Null, failureBody.RootElement.GetProperty("details").ValueKind);
        Assert.DoesNotContain(privateMarker, failureText, StringComparison.Ordinal);
        Assert.DoesNotContain("42P01", failureText, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", failureText, StringComparison.Ordinal);

        var incident = await failingApi.WaitForIncidentAsync();
        Assert.Equal("CRITICAL", incident.GetProperty("severity").GetString());
        var state = incident.GetProperty("State");
        Assert.Equal("Legacy.Maliev.CountryService.Api", state.GetProperty("Service").GetString());
        Assert.Equal("GET", state.GetProperty("Method").GetString());
        Assert.Equal("Countries", state.GetProperty("Path").GetString()!.Trim('/'));
        Assert.Equal(500, state.GetProperty("StatusCode").GetInt32());
        Assert.Equal("PostgresException", state.GetProperty("ExceptionType").GetString());
        Assert.Equal(incidentId, state.GetProperty("IncidentId").GetString());
        Assert.True(DateTimeOffset.TryParse(state.GetProperty("OccurredAtUtc").GetString(), out var occurred));
        Assert.Equal(TimeSpan.Zero, occurred.Offset);
        Assert.Contains("11111111111111111111111111111111", incident.GetRawText(), StringComparison.Ordinal);
        // The source-selected console is Warning+; shared request completions remain Information.
        // Drain the exact child output after the real request, not a sleep-only absence check.
        await failingApi.StopAndDrainAsync();
        foreach (var line in failingApi.Output.Split('\n').Where(line => line.StartsWith('{')))
        {
            using var logged = JsonDocument.Parse(line);
            Assert.Contains(logged.RootElement.GetProperty("LogLevel").GetString(), new[] { "Warning", "Error", "Critical" });
            Assert.Equal("Application diagnostic; see event, exception and trace metadata",
                logged.RootElement.GetProperty("Message").GetString());
        }
        Assert.DoesNotContain(privateMarker, failingApi.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", incident.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class ApiProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();
        private readonly string _contentRoot;
        private bool _started;
        private bool _stopped;
        private readonly TaskCompletionSource _stdoutClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stderrClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpClient Client { get; }
        public string Output => string.Join('\n', _output);

        private ApiProcess(Process process, int port, string contentRoot)
        {
            _process = process;
            _contentRoot = contentRoot;
            Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null) _output.Enqueue(args.Data);
                else _stdoutClosed.TrySetResult();
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null) _output.Enqueue(args.Data);
                else _stderrClosed.TrySetResult();
            };
        }

        public static async Task<ApiProcess> StartAsync(string connectionString)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var contentRoot = Path.Combine(Path.GetTempPath(), "country-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(contentRoot);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = contentRoot,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            // Do not inherit deployment secrets, telemetry endpoints or service destinations.
            var systemVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE",
                "DOTNET_ROOT", "DOTNET_ROOT_X64", "ProgramFiles",
            };
            foreach (var name in start.Environment.Keys.Where(name => !systemVariables.Contains(name)).ToArray())
            {
                start.Environment.Remove(name);
            }
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
            start.Environment["ConnectionStrings__CountryDbContext"] = connectionString;
            start.Environment["CORS__AllowedOrigins__0"] = "https://example.test";
            // Explicit supported local memory cache; no Redis/OTLP/IAM destination is configured.
            start.Environment["Cache__RedisEnabled"] = "false";
            using var rsa = RSA.Create(2048);
            start.Environment["Jwt__PublicKey"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem()));
            start.Environment["Jwt__Issuer"] = "country-acceptance";
            start.Environment["Jwt__Audience"] = "country-acceptance";
            var process = new Process { StartInfo = start };
            var api = new ApiProcess(process, port, contentRoot);
            try
            {
                Assert.True(process.Start());
                api._started = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!deadline.IsCancellationRequested)
                {
                    if (process.HasExited) throw new InvalidOperationException($"Country API exited before liveness (exit code {process.ExitCode}; category StartupFailure).");
                    try
                    {
                        using var response = await api.Client.GetAsync("/countries/liveness", deadline.Token);
                        if (response.IsSuccessStatusCode) return api;
                    }
                    catch (HttpRequestException) { }
                    await Task.Delay(100, deadline.Token);
                }
                throw new TimeoutException("Country API did not become live.");
            }
            catch
            {
                await api.DisposeAsync();
                throw;
            }
        }

        public Task<JsonElement> WaitForIncidentAsync() => WaitForLogAsync(entry =>
            entry.TryGetProperty("State", out var state)
            && state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("EventName", out var name)
            && name.GetString() == "UnhandledRequestFailure");

        public Task<JsonElement> WaitForProbeCriticalAsync(string nonce) => WaitForLogAsync(entry =>
            entry.TryGetProperty("DiagnosticId", out var id) && id.GetString() == nonce
            && entry.TryGetProperty("EventName", out var name) && name.GetString() == "UnhandledRequestFailure"
            && entry.GetProperty("severity").GetString() == "CRITICAL");

        public JsonElement[] ProbeEvents(string nonce) => _output.Where(line => line.StartsWith('{')).Select(line =>
        {
            using var json = JsonDocument.Parse(line);
            return json.RootElement.Clone();
        }).Where(entry => entry.TryGetProperty("DiagnosticId", out var id) && id.GetString() == nonce
            && entry.TryGetProperty("Synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True).ToArray();

        private async Task<JsonElement> WaitForLogAsync(Func<JsonElement, bool> matches)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!deadline.IsCancellationRequested)
            {
                foreach (var line in _output.Where(line => line.StartsWith('{')))
                {
                    using var document = JsonDocument.Parse(line);
                    if (matches(document.RootElement)) return document.RootElement.Clone();
                }
                await Task.Delay(50, deadline.Token);
            }
            throw new TimeoutException("Expected Country native log event was not emitted.");
        }

        public async Task StopAndDrainAsync()
        {
            if (!_started || _stopped) return;
            if (!_process.HasExited) _process.Kill();
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_stdoutClosed.Task, _stderrClosed.Task).WaitAsync(TimeSpan.FromSeconds(10));
            _stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await StopAndDrainAsync();
            }
            finally
            {
                _process.Dispose();
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
    }
}
