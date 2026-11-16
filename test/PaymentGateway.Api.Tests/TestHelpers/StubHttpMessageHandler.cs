using System.Net;
using System.Text;

namespace PaymentGateway.Api.Tests.TestHelpers;

/// <summary>Stands in for the network: each request is answered by the function given.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string body) =>
        new((_, _) => Task.FromResult(Json(statusCode, body)));

    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>Never answers, so the caller's timeout or cancellation is what ends the call.</summary>
    public static StubHttpMessageHandler Hanging() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable");
        });

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _respond(request, cancellationToken);
}
