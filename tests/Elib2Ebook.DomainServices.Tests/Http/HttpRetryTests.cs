using System.Net;
using System.Net.Http.Headers;
using Elib2Ebook.DomainServices.Extensions;

namespace Elib2Ebook.DomainServices.Tests.Http;

public class HttpRetryTests
{
    /// <summary>
    /// Обработчик, отдающий заранее заданную последовательность ответов и считающий запросы
    /// </summary>
    private class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var factory = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            var response = factory();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private class CapturingHandler(HttpMessageHandler inner, List<string> bodies) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(1);
    private static readonly Uri Url = new("https://example.test/");

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task PermanentFailures_AreNotRetried(HttpStatusCode code)
    {
        var handler = new SequenceHandler(() => Status(code));
        using var client = new HttpClient(handler);

        using var response = await client.GetWithTriesAsync(Url, FastRetry);

        Assert.Equal(code, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientFailures_AreRetriedUntilSuccess(HttpStatusCode code)
    {
        var handler = new SequenceHandler(() => Status(code), () => Status(code), () => Status(HttpStatusCode.OK));
        using var client = new HttpClient(handler);

        using var response = await client.GetWithTriesAsync(Url, FastRetry);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task PersistentFailure_ReturnsLastResponseAfterFiveAttempts()
    {
        var handler = new SequenceHandler(() => Status(HttpStatusCode.BadGateway));
        using var client = new HttpClient(handler);

        using var response = await client.GetWithTriesAsync(Url, FastRetry);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task NetworkErrors_AreRetriedThenRethrown()
    {
        var handler = new SequenceHandler(() => throw new HttpRequestException("boom"));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetWithTriesAsync(Url, FastRetry));
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task Post_CanResendBody()
    {
        var bodies = new List<string>();
        var handler = new SequenceHandler(() => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.OK));
        using var client = new HttpClient(new CapturingHandler(handler, bodies));

        using var response = await client.PostWithTriesAsync(Url, new FormUrlEncodedContent(new Dictionary<string, string> { ["a"] = "1" }), FastRetry);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["a=1", "a=1"], bodies);
    }

    [Fact]
    public void Backoff_GrowsExponentiallyAndIsCapped()
    {
        var delays = Enumerable.Range(0, 8).Select(i => HttpClientExtensions.GetBackoff(TimeSpan.FromSeconds(1), i)).ToList();

        for (var i = 0; i < 5; i++)
        {
            var expected = Math.Pow(2, i);
            Assert.InRange(delays[i].TotalSeconds, expected, expected * 1.25);
        }

        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(30), delays[^1]);
    }

    [Fact]
    public void RetryAfter_DeltaIsUsedAndCapped()
    {
        var response = Status(HttpStatusCode.TooManyRequests);

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), HttpClientExtensions.GetRetryAfter(response));

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(120), HttpClientExtensions.GetRetryAfter(response));
    }

    [Fact]
    public void RetryAfter_DateInPastIsZero_AndMissingIsNull()
    {
        var response = Status(HttpStatusCode.TooManyRequests);
        Assert.Null(HttpClientExtensions.GetRetryAfter(response));

        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(TimeSpan.Zero, HttpClientExtensions.GetRetryAfter(response));
    }
}
