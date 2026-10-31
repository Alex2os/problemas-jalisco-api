using System.Globalization;
using System.Net;
using System.Text.Json;
using JaliscoProblemsAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace JaliscoProblemsAPI.Tests;

public sealed class LocationSearchTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class Clients(HttpMessageHandler handler, TimeSpan? timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        { BaseAddress = new Uri("https://geocoder.example.invalid/"), Timeout = timeout ?? TimeSpan.FromSeconds(5) };
    }
    private static GeocodingService Service(HttpMessageHandler handler, TimeSpan? timeout = null) => new(new Clients(handler, timeout), NullLogger<GeocodingService>.Instance);
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private const string Match = """
        [{"name":"Escuela Morelos","display_name":"Escuela Morelos, Calle Morelos 10, Centro, Guadalajara",
          "lat":"20.67","lon":"-103.35","address":{"road":"Calle Morelos","house_number":"10","suburb":"Centro","city":"Guadalajara"}}]
        """;

    [Fact]
    public async Task EncodesQueryAndUsesBoundedSpanishMexicoSearch()
    {
        using var handler = new Handler((request, _) =>
        {
            var uri = request.RequestUri!;
            Assert.Equal("/search", uri.AbsolutePath);
            Assert.Contains("countrycodes=mx", uri.Query);
            Assert.Contains("bounded=1", uri.Query);
            Assert.Contains("viewbox=-105.68,22.75,-101.50,18.92", uri.Query);
            Assert.Contains("limit=5", uri.Query);
            Assert.Contains("addressdetails=1", uri.Query);
            Assert.Contains("accept-language=es", uri.Query);
            Assert.Contains("q=" + Uri.EscapeDataString("Morelos & countrycodes=xx #á"), uri.Query);
            return Task.FromResult(Json(Match));
        });
        using var service = Service(handler);
        var result = Assert.Single(await service.SearchAsync("  Morelos   & countrycodes=xx #á  ", CancellationToken.None));
        Assert.Equal("Escuela Morelos", result.Name);
        Assert.Equal("Calle Morelos 10", result.Address);
        Assert.Equal("Calle Morelos", result.Street);
        Assert.Equal("10", result.StreetNumber);
        Assert.Equal("Centro", result.Colonia);
        Assert.Equal("Guadalajara", result.Municipality);
        Assert.Equal(20.67, result.Latitude);
        Assert.Equal(-103.35, result.Longitude);
    }

    [Fact]
    public async Task ConcurrentNormalizedSearchesShareCacheAndDoNotFillReverseCache()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(Match)); });
        using var service = Service(handler);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.SearchAsync("  Calle   Morelos  ", CancellationToken.None)));
        Assert.All(results, result => Assert.Single(result));
        Assert.Single(await service.SearchAsync("calle morelos", CancellationToken.None));
        Assert.Equal(1, calls);
        // Search coordinates are only candidates; selecting a point still uses reverse lookup.
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ReverseAddressAsync(20.67, -103.35, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FiltersOutOfBoundsDeduplicatesAndCapsResultsWithoutInventingAddressParts()
    {
        var items = new[] {
            new {lat = 30.0, lon = -103.0, display_name = "Outside"},
            new {lat = 20.0, lon = -103.0, display_name = "First"},
            new {lat = 20.0, lon = -103.0, display_name = "Duplicate"},
            new {lat = 20.1, lon = -103.0, display_name = "Second"},
            new {lat = 20.2, lon = -103.0, display_name = "Third"},
            new {lat = 20.3, lon = -103.0, display_name = "Fourth"},
            new {lat = 20.4, lon = -103.0, display_name = "Fifth"},
            new {lat = 20.5, lon = -103.0, display_name = "Sixth"}
        };
        using var handler = new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(items))));
        using var service = Service(handler);
        var results = await service.SearchAsync("Morelos", CancellationToken.None);
        Assert.Equal(5, results.Count);
        Assert.Equal("First", results[0].Name);
        Assert.Equal("First", results[0].FormattedAddress);
        Assert.Equal("First", results[0].Address);
        Assert.Null(results[0].Street);
        Assert.Null(results[0].StreetNumber);
        Assert.Null(results[0].Colonia);
        Assert.Null(results[0].Municipality);
        Assert.Equal("Fifth", results[^1].Name);
    }

    [Fact]
    public async Task EmptyResultsAreCachedAndRemainDistinctFromProviderFailure()
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json("[]")); });
        using var service = Service(handler);
        Assert.Empty(await service.SearchAsync("Sin resultados", CancellationToken.None));
        Assert.Empty(await service.SearchAsync("sin resultados", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[{}]")]
    [InlineData("[null]")]
    [InlineData("[{\"lat\":\"NaN\",\"lon\":\"-103\",\"display_name\":\"Invalid\"}]")]
    [InlineData("[{\"lat\":\"20\",\"lon\":\"Infinity\",\"display_name\":\"Invalid\"}]")]
    [InlineData("[{\"lat\":20,\"lon\":-103,\"display_name\":\" \"}]")]
    public async Task UnreadableOrMalformedResponsesAreFailuresNotEmptySearches(string content)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(content)));
        using var service = Service(handler);
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.SearchAsync("Morelos", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpErrorsAreNotCachedAsEmptyAndRetryCanRecover(HttpStatusCode status)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++calls == 1 ? new HttpResponseMessage(status) : Json(Match)));
        using var service = Service(handler);
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.SearchAsync("Morelos", CancellationToken.None));
        Assert.Single(await service.SearchAsync("Morelos", CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TransportFailureCanRecoverAndUnicodeFieldsAreBounded()
    {
        var calls = 0;
        var name = string.Concat(Enumerable.Repeat("😀", 300));
        var payload = JsonSerializer.Serialize(new[] {new {lat = "20.67", lon = "-103.35", display_name = name}});
        using var handler = new Handler((_, _) => ++calls == 1 ? throw new HttpRequestException("offline") : Task.FromResult(Json(payload)));
        using var service = Service(handler);
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.SearchAsync("Morelos", CancellationToken.None));
        var result = Assert.Single(await service.SearchAsync("Morelos", CancellationToken.None));
        Assert.Equal(255, result.FormattedAddress.EnumerateRunes().Count());
        Assert.DoesNotContain('\uFFFD', result.FormattedAddress);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellationRemainDistinct()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("[]"); });
        using var service = Service(handler, TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.SearchAsync("Morelos", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("Morelos", cancelled.Token));
    }

    [Fact]
    public async Task SearchAndReverseShareOneOutboundGateAndInvariantCoordinateParsing()
    {
        var times = new List<DateTimeOffset>();
        using var handler = new Handler((request, _) =>
        {
            times.Add(DateTimeOffset.UtcNow);
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/search" ? Match : "{\"display_name\":\"Jalisco\"}"));
        });
        using var service = Service(handler);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await service.ReverseAsync(20, -103, CancellationToken.None);
            Assert.Equal(20.67, Assert.Single(await service.SearchAsync("Morelos", CancellationToken.None)).Latitude);
            Assert.True(times[1] - times[0] >= TimeSpan.FromMilliseconds(990));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
