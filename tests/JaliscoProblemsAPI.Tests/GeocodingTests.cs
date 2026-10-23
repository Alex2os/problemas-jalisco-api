using System.Globalization;
using System.Net;
using System.Text;
using JaliscoProblemsAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace JaliscoProblemsAPI.Tests;

public sealed class GeocodingTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class Clients(HttpMessageHandler handler, TimeSpan? timeout = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://geocoder.example.invalid/"), Timeout = timeout ?? TimeSpan.FromSeconds(5) };
    }
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private static GeocodingService Service(HttpMessageHandler handler, TimeSpan? timeout = null) => new(new Clients(handler, timeout), NullLogger<GeocodingService>.Instance);

    [Fact]
    public async Task ConcurrentIdenticalCoordinatesShareOneLookupAndUseInvariantNumbers()
    {
        var calls = 0;
        using var handler = new Handler(async (request, ct) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Contains("lat=20.65&lon=-103.3", request.RequestUri!.Query);
            await Task.Delay(20, ct);
            return Json("{\"display_name\":\"Guadalajara\"}");
        });
        using var service = Service(handler);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-MX");
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ReverseAsync(20.65, -103.3, CancellationToken.None)));
            Assert.All(results, location => Assert.Equal("Guadalajara", location));
            Assert.Equal(1, calls);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task DistinctLookupsAreSpacedAtLeastOneSecondApart()
    {
        var times = new List<DateTimeOffset>();
        using var handler = new Handler((_, _) => { times.Add(DateTimeOffset.UtcNow); return Task.FromResult(Json("{\"display_name\":\"Jalisco\"}")); });
        using var service = Service(handler);
        await Task.WhenAll(service.ReverseAsync(20, -103, CancellationToken.None), service.ReverseAsync(21, -103, CancellationToken.None));
        Assert.Equal(2, times.Count);
        Assert.True(times[1] - times[0] >= TimeSpan.FromMilliseconds(990));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"display_name\":null}")]
    [InlineData("{\"display_name\":\" \"}")]
    [InlineData("[]")]
    public async Task InvalidUpstreamPayloadsProduceAControlledFailure(string content)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(content)));
        using var service = Service(handler);
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ReverseAsync(20, -103, CancellationToken.None));
    }

    [Fact]
    public async Task TimeoutIsControlledButCallerCancellationIsNotSwallowed()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); });
        using var service = Service(handler, TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ReverseAsync(20, -103, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReverseAsync(20, -103, cancelled.Token));
    }

    [Fact]
    public async Task LongAddressesFitTheExistingColumnWithoutSplittingEmoji()
    {
        var name = string.Concat(Enumerable.Repeat("😀", 300));
        using var handler = new Handler((_, _) => Task.FromResult(Json(System.Text.Json.JsonSerializer.Serialize(new { display_name = name }))));
        using var service = Service(handler);
        var address = await service.ReverseAsync(20, -103, CancellationToken.None);
        Assert.Equal(255, address.EnumerateRunes().Count());
        Assert.DoesNotContain('\uFFFD', address);
    }
}
