using System.Buffers;
using System.Reflection;
using System.Text;
using Legacy.Maliev.CountryService.Api.Controllers;
using Legacy.Maliev.CountryService.Api.Serialization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;

namespace Legacy.Maliev.CountryService.Tests.Integration;

// These tests name observable admission/projection/ownership failures. The source
// carrier is transcribed independently from original Country.cs, not production's
// input-only carrier. The oracle is stock MVC, not JsonConvert.DeserializeObject.
public sealed class CountryOriginalBodyBoundaryTests
{
    public static IEnumerable<object?[]> SuccessfulBodies()
    {
        yield return ["{\"Name\":\"Thailand\"}", "Thailand", null, null, null, null];
        yield return ["/*before*/{\"Name\":\"Thailand\"/*inside*/}", "Thailand", null, null, null, null];
        yield return ["{'Name':'Thailand'}", "Thailand", null, null, null, null];
        yield return ["{Name:'Thailand'}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\",}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\"} {}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":0x10}", "Thailand", null, "0x10", null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":010}", "Thailand", null, "010", null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":.5}", "Thailand", null, ".5", null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":NaN}", "Thailand", null, "NaN", null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":Infinity}", "Thailand", null, "Infinity", null, null];
        yield return ["{\"Name\":\"Thailand\",\"CountryCode\":-Infinity}", "Thailand", null, "-Infinity", null, null];
        yield return ["{\"Name\":12,\"Continent\":true,\"CountryCode\":1.2300e+02,\"Iso2\":12,\"Iso3\":-12}", "12", "true", "1.2300e+02", "12", "-12"];
        yield return ["{\"Name\":\"first\",\"NAME\":null,\"name\":\"last\",\"name\":null}", "last", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\",\"Continent\":\"Asia\",\"CONTINENT\":null,\"CountryCode\":764,\"countryCode\":null,\"Iso2\":\"TH\",\"ISO2\":null,\"Iso3\":\"THA\",\"ISO3\":null}", "Thailand", "Asia", "764", "TH", "THA"];
        yield return ["{\"$ref\":null,\"Name\":\"Thailand\"}", "Thailand", null, null, null, null];
        yield return ["{\"$id\":\"1\",\"$type\":\"unresolvable, private-marker\",\"Name\":\"Thailand\"}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\",\"$ref\":1,\"$values\":[]}", "Thailand", null, null, null, null];
        yield return ["{\"$REF\":1,\"Name\":\"Thailand\"}", "Thailand", null, null, null, null];
        yield return ["{\"unknown\":{\"$ref\":1,\"Name\":[]},\"Name\":\"Thailand\"}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"Thailand\",\"Id\":\"2147483647\",\"ID\":null,\"CreatedDate\":\"1900-01-01T00:00:00Z\",\"CreatedDate\":\"\",\"ModifiedDate\":\"/Date(0+0700)/\",\"ModifiedDate\":null}", "Thailand", null, null, null, null];
        yield return ["{\"Name\":\"\\uD800\"}", "\uFFFD", null, null, null, null];
        yield return ["{\"Name\":\"\\uDC00\"}", "\uFFFD", null, null, null, null];
        yield return ["{\"Name\":\"\\uD800x\"}", "\uFFFDx", null, null, null, null];
        yield return ["{\"Name\":\"\\uD83D\\uDE00\"}", "😀", null, null, null, null];
        yield return ["{\"Name\":\"\\uFFFD\"}", "\uFFFD", null, null, null, null];
        yield return ["{\"Name\":\"\\u0000\"}", "\0", null, null, null, null];
        yield return ["{\"Name\":\"2026-10-17T00:00:00Z\"}", "2026-10-17T00:00:00Z", null, null, null, null];
    }

    [Theory]
    [MemberData(nameof(SuccessfulBodies))]
    public async Task CompleteSourceBody_StockMvcAndCandidateProjectOnlyFiveLiteralStrings(
        string json, string name, string? continent, string? code, string? iso2, string? iso3)
    {
        var expected = new UpsertCountryRequest(name, continent, code, iso2, iso3);
        var source = await ReadSourceAsync(json);
        var original = Assert.IsType<SourceCountry>(source.Result!.Model);
        Assert.Equal(expected, Project(original));
        var candidate = await ReadCandidateAsync(json);
        Assert.False(candidate.Result!.HasError);
        Assert.Equal(expected, Assert.IsType<UpsertCountryRequest>(candidate.Result.Model));
        Assert.Empty(candidate.Context.ModelState);
    }

    [Theory]
    [InlineData("{\"$ref\":1,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$ref\":true,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$ref\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"$ref\":[],\"Name\":\"Thailand\"}")]
    [InlineData("{\"$ref\":\"missing\",\"Name\":\"Thailand\"}")]
    [InlineData("{\"$values\":[],\"Name\":\"Thailand\"}")]
    public async Task LeadingReservedMetadata_CannotBeSilentlySkippedByTheOwnedBody(string json)
    {
        Assert.True((await ReadSourceAsync(json)).Result!.HasError);
        var candidate = await ReadCandidateAsync(json);
        Assert.True(candidate.Result!.HasError);
        var native = await ReadCurrentStjAsync(json);
        Assert.Equal("Thailand", Assert.IsType<UpsertCountryRequest>(native.Result!.Model).Name);
    }

    [Theory]
    [InlineData("{\"$type\":null,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$type\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"$type\":[],\"Name\":\"Thailand\"}")]
    [InlineData("{\"$type\":12,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$id\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"$id\":null,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$id\":1,\"Name\":\"Thailand\"}")]
    [InlineData("{\"$value\":{\"Name\":\"nested\"},\"Name\":\"Thailand\"}")]
    public async Task MetadataShapes_PreserveStockOutcomeRatherThanInventingABlanket400(string json)
    {
        var source = await ObserveAsync(() => ReadSourceAsync(json));
        var candidate = await ObserveAsync(() => ReadCandidateAsync(json));
        Assert.Equal(source.ExceptionType, candidate.ExceptionType);
        if (source.Read is null) return;
        Assert.Equal(source.Read.Result!.HasError, candidate.Read!.Result!.HasError);
        Assert.Equal(source.Read.Result.IsModelSet, candidate.Read!.Result.IsModelSet);
        if (!source.Read.Result.HasError && source.Read.Result.Model is SourceCountry original)
            Assert.Equal(Project(original), Assert.IsType<UpsertCountryRequest>(candidate.Read.Result.Model));
    }

    [Theory]
    [InlineData("{\"Name\":{},\"Name\":\"later\"}")]
    [InlineData("{\"Continent\":[],\"Name\":\"Thailand\"}")]
    [InlineData("{\"CountryCode\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"Iso2\":[],\"Name\":\"Thailand\"}")]
    [InlineData("{\"Iso3\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"Id\":2147483648,\"Id\":1,\"Name\":\"Thailand\"}")]
    [InlineData("{\"Id\":true,\"Name\":\"Thailand\"}")]
    [InlineData("{\"Id\":{},\"Name\":\"Thailand\"}")]
    [InlineData("{\"CreatedDate\":\"invalid-private-marker\",\"CreatedDate\":null,\"Name\":\"Thailand\"}")]
    [InlineData("{\"ModifiedDate\":[],\"Name\":\"Thailand\"}")]
    [InlineData("{\"Name\":\"Thailand\",\"unknown\":[}")]
    [InlineData("[]")]
    [InlineData("true")]
    public async Task EachKnownTypedOccurrenceAndMalformedFirstObject_FailsBeforeProjection(string json)
    {
        Assert.True((await ReadSourceAsync(json)).Result!.HasError);
        var candidate = await ReadCandidateAsync(json);
        Assert.True(candidate.Result!.HasError);
        Assert.Null(candidate.Result.Model);
        Assert.All(candidate.Context.ModelState.Keys, key => Assert.Equal("request", key));
        Assert.All(candidate.Context.ModelState.Values.SelectMany(value => value.Errors),
            error => Assert.Equal("The request body contains an invalid value.", error.ErrorMessage));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("undefined")]
    [InlineData("{\"$ref\":\"not-present\"}")]
    public async Task StockNoValue_RemainsNoValueNotSuccessfulDefaultDto(string json)
    {
        var source = await ReadSourceAsync(json);
        var candidate = await ReadCandidateAsync(json);
        Assert.False(source.Result!.HasError);
        Assert.False(source.Result.IsModelSet);
        Assert.False(candidate.Result!.HasError);
        Assert.False(candidate.Result.IsModelSet);
        Assert.Empty(candidate.Context.ModelState);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task ExactActionSelection_DoesNotTakeFutureActionsOrOtherTypes(string method)
    {
        var formatter = Candidate();
        var owned = Context(typeof(UpsertCountryRequest), "{\"Name\":\"Thailand\"}", method);
        Assert.True(formatter.CanRead(owned));
        foreach (var selected in new[] { "no-endpoint", "no-action", "other-controller", "mismatched-method", "get" })
        {
            var other = Context(typeof(UpsertCountryRequest), "{\"Name\":\"Thailand\"}", selected);
            Assert.False(formatter.CanRead(other));
            await Assert.ThrowsAsync<InvalidOperationException>(() => formatter.ReadAsync(other));
            Assert.Equal(0, other.HttpContext.Request.Body.Position);
        }
        var response = Context(typeof(CountryResponse), "{}", method);
        Assert.False(formatter.CanRead(response));
        await Assert.ThrowsAsync<InvalidOperationException>(() => formatter.ReadAsync(response));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public async Task SupportedEncoding_IsSelectedByStockBeforeCompleteBodyParsing(string charset)
    {
        var encoding = charset == "utf-8" ? Encoding.UTF8 : Encoding.Unicode;
        var context = Context(typeof(UpsertCountryRequest), "", "POST");
        context.HttpContext.Request.ContentType = "application/problem+json; charset=" + charset;
        context.HttpContext.Request.Body = new MemoryStream(encoding.GetBytes("{Name:'ไทย'}"));
        var result = await Candidate().ReadAsync(context);
        Assert.Equal("ไทย", Assert.IsType<UpsertCountryRequest>(result.Model).Name);
    }

    [Fact]
    public async Task UnsupportedCharset_Is415ClassificationWithoutReadingOrEchoingInput()
    {
        var context = Context(typeof(UpsertCountryRequest), "", "POST");
        using var body = new AsyncOnlyBody(Encoding.UTF8.GetBytes("private-marker"));
        context.HttpContext.Request.Body = body;
        context.HttpContext.Request.ContentType = "application/json; charset=private-marker";
        var result = await Candidate().ReadAsync(context);
        Assert.True(result.HasError);
        Assert.Equal(0, body.Reads);
        var error = Assert.Single(Assert.Single(context.ModelState).Value!.Errors);
        Assert.IsType<UnsupportedContentTypeException>(error.Exception);
        Assert.DoesNotContain("private-marker", error.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spill_UsesAsyncSourceReadsRestoresBodyAndDeletesOnlyOwnedSpill()
    {
        var context = Context(typeof(UpsertCountryRequest), "", "POST");
        using var body = new AsyncOnlyBody(Encoding.UTF8.GetBytes("{\"Name\":\"Thailand\",\"unknown\":\"" + new string('x', 70000) + "\"}"));
        context.HttpContext.Request.Body = body;
        FileBufferingReadStream? buffered = null;
        string? spill = null;
        var contextWithReader = new InputFormatterContext(context.HttpContext, context.ModelName, context.ModelState,
            context.Metadata, (stream, encoding) =>
            {
                buffered = Assert.IsType<FileBufferingReadStream>(context.HttpContext.Request.Body);
                spill = buffered.TempFileName;
                Assert.NotNull(spill);
                Assert.True(File.Exists(spill));
                return Reader(stream, encoding);
            });
        var result = await Candidate().ReadAsync(contextWithReader);
        Assert.Equal("Thailand", Assert.IsType<UpsertCountryRequest>(result.Model).Name);
        Assert.Same(body, context.HttpContext.Request.Body);
        Assert.False(body.Disposed);
        Assert.True(body.Reads > 0);
        Assert.False(File.Exists(spill));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await buffered!.ReadExactlyAsync(new byte[1]));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("malformed")]
    [InlineData("limit")]
    [InlineData("cancellation")]
    [InlineData("io")]
    public async Task OwnedBuffering_RestoresCallerBodyAndDoesNotTurnUnexpectedFailuresInto400(string mode)
    {
        var context = Context(typeof(UpsertCountryRequest), "", "POST");
        var token = new CancellationTokenSource();
        using var cancellation = token;
        var text = mode == "malformed" ? "{\"Name\":{}" : "{\"Name\":\"Thailand\"}";
        using var body = new AsyncOnlyBody(Encoding.UTF8.GetBytes(text));
        context.HttpContext.Request.Body = body;
        context.HttpContext.RequestAborted = token.Token;
        if (mode == "limit") context.HttpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new LimitFeature(4));
        if (mode == "cancellation") { token.Cancel(); body.Failure = new OperationCanceledException(token.Token); }
        if (mode == "io") body.Failure = new IOException("private-marker");
        if (mode == "limit" || mode == "io")
            await Assert.ThrowsAsync<IOException>(() => Candidate().ReadAsync(context));
        else if (mode == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Candidate().ReadAsync(context));
        else
        {
            var result = await Candidate().ReadAsync(context);
            Assert.Equal(mode == "malformed", result.HasError);
        }
        Assert.Same(body, context.HttpContext.Request.Body);
        Assert.False(body.Disposed);
    }

    [Fact]
    public async Task SeekableCallerPositionAndPrivateLogger_ArePreserved()
    {
        using var capture = new LogCapture();
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        var context = Context(typeof(UpsertCountryRequest), "", "POST");
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("xx{\"private-marker\":{},\"Name\":{},\"Name\":\"private-marker\"}"));
        body.Position = 2;
        context.HttpContext.Request.Body = body;
        var result = await Candidate(factory).ReadAsync(context);
        Assert.True(result.HasError);
        Assert.Same(body, context.HttpContext.Request.Body);
        Assert.Equal(2, body.Position);
        Assert.NotEmpty(capture.Messages);
        Assert.All(capture.Messages, message => Assert.DoesNotContain("private-marker", message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(34)]
    public async Task CompleteOuterReader_UsesSourceDepthForUnknownContent(int depth)
    {
        var json = "{\"Name\":\"Thailand\",\"unknown\":" + new string('[', depth) + "0" + new string(']', depth) + "}";
        var source = await ReadSourceAsync(json);
        var candidate = await ReadCandidateAsync(json);
        Assert.Equal(depth > 32, source.Result!.HasError);
        Assert.Equal(source.Result.HasError, candidate.Result!.HasError);
        if (!source.Result.HasError) Assert.Equal("Thailand", Assert.IsType<UpsertCountryRequest>(candidate.Result.Model).Name);
    }

    [Fact]
    public async Task NativeTrailingAndLoneSurrogateControls_DoNotBecomeTheSourceOracle()
    {
        Assert.True((await ReadCurrentStjAsync("{\"Name\":\"Thailand\"} {}")).Result!.HasError);
        var nativeSurrogate = await ObserveAsync(() => ReadCurrentStjAsync("{\"Name\":\"\\uD800\"}"));
        Assert.True(nativeSurrogate.ExceptionType is not null || nativeSurrogate.Read!.Result!.HasError);
        Assert.Equal("Thailand", Assert.IsType<UpsertCountryRequest>((await ReadCandidateAsync("{\"Name\":\"Thailand\"} {}")).Result!.Model).Name);
        Assert.Equal("\uFFFD", Assert.IsType<UpsertCountryRequest>((await ReadCandidateAsync("{\"Name\":\"\\uD800\"}")).Result!.Model).Name);
    }

    [Theory]
    [MemberData(nameof(CountryDiscardedInputReaderTests.TypedCases), MemberType = typeof(CountryDiscardedInputReaderTests))]
    public async Task CompleteTypedMembers_StockMvcValidatesEveryOrderedOccurrenceBeforeDiscard(string field, string token)
    {
        var valid = field == "id" ? "12" : "\"2026-10-17T00:00:00Z\"";
        foreach (var suffix in new[] { valid, "null" })
        {
            foreach (var json in new[]
            {
                "{\"Name\":\"Thailand\",\"" + field + "\":" + token + "}",
                "{\"Name\":\"Thailand\",\"" + field + "\":" + token + ",\"" + field.ToUpperInvariant() + "\":" + suffix + "}",
                "{\"Name\":\"Thailand\",\"" + field + "\":" + suffix + ",\"" + field.ToUpperInvariant() + "\":" + token + "}",
            })
            {
                var source = await ReadSourceAsync(json);
                var candidate = await ReadCandidateAsync(json);
                Assert.Equal(source.Result!.HasError, candidate.Result!.HasError);
                if (source.Result.HasError) Assert.Null(candidate.Result.Model);
                else Assert.Equal(new UpsertCountryRequest("Thailand", null, null, null, null), candidate.Result.Model);
            }
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public async Task CompleteSourceReader_KeepsInvariantNumbersDatesAndDuplicateNulls(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        var previousUi = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            const string json = "{\"Name\":\"Thailand\",\"CountryCode\":1.2300e+02,\"Id\":\"  +12  \",\"CreatedDate\":\"10/17/2026\",\"ModifiedDate\":\"2026-10-17T07:30:00+07:00\",\"CountryCode\":null}";
            var source = await ReadSourceAsync(json);
            Assert.Equal(12, Assert.IsType<SourceCountry>(source.Result!.Model).Id);
            var candidate = await ReadCandidateAsync(json);
            Assert.Equal(new UpsertCountryRequest("Thailand", null, "1.2300e+02", null, null), candidate.Result!.Model);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            System.Globalization.CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static IInputFormatter Candidate(ILoggerFactory? factory = null)
    {
        var options = new MvcOptions();
        var setup = new CountryOriginalBodyMvcOptionsSetup(factory ?? NullLoggerFactory.Instance, new DefaultObjectPoolProvider());
        setup.Configure(options);
        setup.Configure(options);
        return Assert.Single(options.InputFormatters);
    }

    private static async Task<Read> ReadCandidateAsync(string json)
    {
        var context = Context(typeof(UpsertCountryRequest), json, "POST");
        return new(await Candidate().ReadAsync(context), context);
    }

    private static async Task<Read> ReadSourceAsync(string json)
    {
        var settings = new MvcNewtonsoftJsonOptions();
        settings.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        settings.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        var formatter = new NewtonsoftJsonInputFormatter(NullLogger.Instance, settings.SerializerSettings,
            ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), new MvcOptions(), settings);
        var context = Context(typeof(SourceCountry), json, "POST");
        return new(await formatter.ReadAsync(context), context);
    }

    private static async Task<Read> ReadCurrentStjAsync(string json)
    {
        var options = new JsonOptions();
        CountryRequestJsonOptions.Configure(options);
        var context = Context(typeof(UpsertCountryRequest), json, "POST");
        var formatter = new SystemTextJsonInputFormatter(options, NullLogger<SystemTextJsonInputFormatter>.Instance);
        return new(await formatter.ReadAsync(context), context);
    }

    private static async Task<(Read? Read, Type? ExceptionType)> ObserveAsync(Func<Task<Read>> action)
    {
        try { return (await action(), null); }
        catch (Exception error) { return (null, error.GetType()); }
    }

    private static InputFormatterContext Context(Type type, string json, string selected)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = selected == "PUT" ? "PUT" : "POST";
        http.Request.ContentType = "application/json; charset=utf-8";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (selected != "no-endpoint")
        {
            var metadata = new List<object>();
            if (selected != "no-action")
            {
                var controller = selected == "other-controller" ? typeof(UnownedController) : typeof(CountriesController);
                var name = selected == "PUT" ? nameof(CountriesController.UpdateCountryAsync)
                    : selected == "get" ? nameof(CountriesController.GetAllCountriesAsync) : nameof(CountriesController.CreateCountryAsync);
                metadata.Add(new ControllerActionDescriptor
                {
                    ControllerTypeInfo = controller.GetTypeInfo(),
                    MethodInfo = selected == "mismatched-method" ? typeof(UnownedController).GetMethod(nameof(UnownedController.CreateCountryAsync))! : controller.GetMethod(name)!,
                });
            }
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "body test"));
        }
        return new InputFormatterContext(http, "request", new ModelStateDictionary(),
            new EmptyModelMetadataProvider().GetMetadataForType(type), Reader);
    }

    private static TextReader Reader(Stream stream, Encoding encoding) =>
        new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);

    private static UpsertCountryRequest Project(SourceCountry value) =>
        new(value.Name!, value.Continent, value.CountryCode, value.Iso2, value.Iso3);

    private sealed record Read(InputFormatterResult Result, InputFormatterContext Context);

    // 5fac467f3755 / 72eb9e422909: all eight source members and a default
    // constructor, no DTO validation annotations or production carrier reuse.
    public sealed class SourceCountry
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

    public sealed class UnownedController
    {
        public void CreateCountryAsync() { }
    }

    private sealed class LimitFeature(long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = limit;
    }

    private sealed class AsyncOnlyBody(byte[] bytes) : Stream
    {
        private int position;
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? Failure { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous source I/O is forbidden.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } failure) return ValueTask.FromException<int>(failure);
            var count = Math.Min(buffer.Length, bytes.Length - position);
            bytes.AsSpan(position, count).CopyTo(buffer.Span);
            position += count;
            return ValueTask.FromResult(count);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);
        public void Dispose() { }
        private sealed class CapturedLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception) + exception);
        }
    }
}
