using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Text;
using Legacy.Maliev.CountryService.Api.Controllers;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Legacy.Maliev.CountryService.Api.Serialization;

/// <summary>Adds original complete Country input admission only for the two legacy write actions.</summary>
/// <param name="loggers">The application logger factory.</param>
/// <param name="pools">The application object pool provider.</param>
public sealed class CountryOriginalBodyMvcOptionsSetup(ILoggerFactory loggers, ObjectPoolProvider pools) : IConfigureOptions<MvcOptions>
{
    /// <inheritdoc />
    public void Configure(MvcOptions options)
    {
        if (options.InputFormatters.OfType<CountryOriginalBodyInputFormatter>().Any()) return;
        var json = new MvcNewtonsoftJsonOptions();
        json.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        json.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        // Stock local MVC defaults preserve metadata, invariant culture, depth32,
        // TypeNameHandling.None and CheckAdditionalContent=false. Do not register
        // global Newtonsoft options, an output writer or a replacement STJ resolver.
        var logger = new SourceBodyLogger(loggers.CreateLogger(
            "Microsoft.AspNetCore.Mvc.Formatters.NewtonsoftJsonInputFormatter"));
        options.InputFormatters.Insert(0, new CountryOriginalBodyInputFormatter(
            new BufferedSourceFormatter(logger, json, pools, options)));
    }

    private sealed class SourceBodyLogger(ILogger target) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            target.BeginScope("Country source body admission");
        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (target.IsEnabled(logLevel))
                target.Log(logLevel, eventId, "SourceBodyAdmissionFailure ExceptionType={ExceptionType}",
                    exception?.GetType().Name ?? "InputFormatter");
        }
    }

    private sealed class BufferedSourceFormatter(
        ILogger logger, MvcNewtonsoftJsonOptions json, ObjectPoolProvider pools, MvcOptions mvc)
        : NewtonsoftJsonInputFormatter(logger, json.SerializerSettings, ArrayPool<char>.Shared, pools, mvc, json)
    {
        private const long DefaultRequestLimit = 30_000_000;
        private readonly int memoryThreshold = json.InputFormatterMemoryBufferThreshold;

        protected override Newtonsoft.Json.JsonSerializer CreateJsonSerializer(InputFormatterContext context) =>
            Newtonsoft.Json.JsonSerializer.Create(SerializerSettings);

        protected override void ReleaseJsonSerializer(Newtonsoft.Json.JsonSerializer serializer)
        {
            // Fresh per read, never rented. Stock MVC removes its request-owned
            // error handler; no serializer or metadata references enter a pool.
        }

        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
        {
            // Stock TextInputFormatter selects the supported encoding first.
            // Drain asynchronously before Json.NET performs synchronous reads.
            var request = context.HttpContext.Request;
            var original = request.Body;
            var position = original.CanSeek ? original.Position : (long?)null;
            // Honor the actual finite server feature, even when below the
            // fallback. Missing/unbounded feature is locally capped at30MB;
            // this bounded adapter fallback never widens authoritative MaxBody.
            var limit = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize
                ?? DefaultRequestLimit;
            var buffered = new FileBufferingReadStream(original, memoryThreshold, limit,
                static () => Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") ?? Path.GetTempPath());
            InputFormatterResult? result = null;
            Exception? primary = null;
            var cleanup = new List<Exception>();
            try
            {
                context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                request.Body = buffered;
                await buffered.DrainAsync(context.HttpContext.RequestAborted);
                buffered.Position = 0;
                result = await base.ReadRequestBodyAsync(context, encoding);
                context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            }
            catch (Exception failure) { primary = failure; }
            finally
            {
                try { request.Body = original; }
                catch (Exception failure) { cleanup.Add(failure); }
                try { if (position.HasValue) original.Position = position.Value; }
                catch (Exception failure) { cleanup.Add(failure); }
                try { await buffered.DisposeAsync(); }
                catch (Exception failure) { cleanup.Add(failure); }
            }
            if (cleanup.Count > 0)
            {
                if (primary is not null) cleanup.Insert(0, primary);
                throw cleanup.Count == 1 ? cleanup[0] : new AggregateException(cleanup);
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            return result!;
        }
    }
}

internal sealed class CountryOriginalBodyInputFormatter(NewtonsoftJsonInputFormatter source)
    : IInputFormatter, IInputFormatterExceptionPolicy
{
    public InputFormatterExceptionPolicy ExceptionPolicy => InputFormatterExceptionPolicy.MalformedInputExceptions;

    private static bool Owns(InputFormatterContext context)
    {
        if (context.ModelType != typeof(UpsertCountryRequest)) return false;
        var action = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null || action.ControllerTypeInfo.AsType() != typeof(CountriesController)) return false;
        return action.MethodInfo == typeof(CountriesController).GetMethod(nameof(CountriesController.CreateCountryAsync))
            || action.MethodInfo == typeof(CountriesController).GetMethod(nameof(CountriesController.UpdateCountryAsync));
    }

    public bool CanRead(InputFormatterContext context) => Owns(context) && source.CanRead(context);

    public async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
        if (!Owns(context))
            throw new InvalidOperationException("The Country source body formatter received an unowned action or contract.");
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(OriginalCountry));
        var state = new ModelStateDictionary(context.ModelState.MaxAllowedErrors);
        var originalContext = new InputFormatterContext(context.HttpContext, context.ModelName, state, metadata,
            context.ReaderFactory, context.TreatEmptyInputAsDefaultValue);
        var result = await source.ReadAsync(originalContext);
        if (result.HasError)
        {
            // Caller-supplied property names/values/exception text remain private.
            // Preserve stock unsupported-charset classification for MVC's415.
            var key = context.ModelName is "" or "item" or "request" ? context.ModelName : "$";
            if (state.Values.SelectMany(value => value.Errors).Any(error => error.Exception is UnsupportedContentTypeException))
                context.ModelState.TryAddModelError(key,
                    new UnsupportedContentTypeException("The request content type is unsupported."), context.Metadata);
            else
                context.ModelState.TryAddModelError(key, "The request body contains an invalid value.");
            return InputFormatterResult.Failure();
        }
        if (!result.IsModelSet) return result;
        if (result.Model is null) return InputFormatterResult.Success(null);
        var value = (OriginalCountry)result.Model;
        // The real current record still receives normal MVC Required/length
        // validation. Identity/audits are parsed but never projected or tracked.
        return InputFormatterResult.Success(new UpsertCountryRequest(
            value.Name!, value.Continent, value.CountryCode, value.Iso2, value.Iso3));
    }

    private sealed class OriginalCountry
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Continent { get; set; }
        public string? CountryCode { get; set; }
        public string? Iso2 { get; set; }
        public string? Iso3 { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
