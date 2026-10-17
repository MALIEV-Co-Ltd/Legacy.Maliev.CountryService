using System.Text.Json.Serialization;
using Legacy.Maliev.CountryService.Api.Diagnostics;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Services;
using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Api.Documentation;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Api.Serialization;
using Maliev.Aspire.ServiceDefaults;

try
{
    await RunHostAsync(args);
}
catch (Microsoft.Extensions.Hosting.HostAbortedException)
{
    // Minimal-host tooling intentionally stops a successfully built host.
    // Preserve that control-flow signal rather than report a production failure.
    throw;
}
catch (Exception exception)
{
    CountryStartupBoundary.ReportFailure(exception);
}

static async Task RunHostAsync(string[] startupArgs)
{
    var builder = WebApplication.CreateBuilder(startupArgs);

    builder.AddServiceDefaults();
    builder.AddDefaultApiVersioning();
    builder.Logging.AddConsoleFormatter<CountryPrivateConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
    builder.Logging.AddConsole(options => options.FormatterName = CountryPrivateConsoleFormatter.FormatterName);
    builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Warning);
    builder.AddPostgresDbContext<CountryDbContext>(connectionName: "CountryDbContext");
    builder.AddStandardCache("legacy:country:");
    builder.AddStandardCors();
    builder.AddJwtAuthentication();
    builder.AddCountryIamComposition();
    builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
    builder.AddStandardOpenApi(
        title: "Legacy MALIEV Country Service API",
        description: "Temporary .NET 10 compatibility service preserving the legacy Country API contract.");
    // Literal application-local registration enables the XML comment generator.
    builder.Services.AddOpenApi("v1", CountryOpenApi.Configure);

    builder.Services.AddControllers().AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(CountryRequestJsonOptions.Configure);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<ICountryRepository, CountryRepository>();
    builder.Services.AddScoped<ICountryCache, DistributedCountryCache>();
    builder.Services.AddScoped<ICountryService, CountryApplicationService>();

    var app = builder.Build();

    app.UseMiddleware<CountryObservabilityProbeMiddleware>();
    app.UseStandardMiddleware();
    app.UseMiddleware<CountryCompletedResponseObservationMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapDefaultEndpoints("countries");
    app.MapControllers();
    app.MapApiDocumentation(servicePrefix: "countries");

    await app.RunAsync();
}

/// <summary>Legacy Country Service entry point.</summary>
public partial class Program;
