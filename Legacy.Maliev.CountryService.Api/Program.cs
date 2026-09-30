using System.Text.Json.Serialization;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Services;
using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Api.Documentation;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddPostgresDbContext<CountryDbContext>(connectionName: "CountryDbContext");
builder.AddStandardCache("legacy:country:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Country Service API",
    description: "Temporary .NET 10 compatibility service preserving the legacy Country API contract.");
// Literal application-local registration enables the XML comment generator.
builder.Services.AddOpenApi("v1", CountryOpenApi.Configure);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<ICountryCache, DistributedCountryCache>();
builder.Services.AddScoped<ICountryService, CountryApplicationService>();

var app = builder.Build();

app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("countries");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "countries");

await app.RunAsync();

/// <summary>Legacy Country Service entry point.</summary>
public partial class Program;
