using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using Moq.Protected;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests;

public class BankClientTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();

    // ---------- Contract with the bank (snake_case JSON, string card number) ----------

    [Fact]
    public async Task ProcessPayment_SendsSnakeCaseRequestAndReadsAuthorizedResponse()
    {
        // Arrange
        string? sentBody = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                sentBody = await request.Content!.ReadAsStringAsync();
                return Json(HttpStatusCode.OK, """{ "authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad" }""");
            });

        // Act
        var result = await CreateClient().ProcessPaymentAsync(ValidRequest());

        // Assert
        Assert.NotNull(sentBody);
        Assert.Contains("\"card_number\":\"2222405343248877\"", sentBody);
        Assert.Contains("\"expiry_date\":\"04/2030\"", sentBody);
        Assert.Contains("\"cvv\":\"123\"", sentBody);
        Assert.True(result.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", result.AuthorizationCode);
    }

    [Fact]
    public async Task ProcessPayment_ReadsDeclinedResponseWithEmptyAuthorizationCode()
    {
        // The bank sends "" for declined payments, which once broke deserialization into a Guid.
        SetupResponse(Json(HttpStatusCode.OK, """{ "authorized": false, "authorization_code": "" }"""));

        var result = await CreateClient().ProcessPaymentAsync(ValidRequest());

        Assert.False(result.Authorized);
        Assert.Equal(string.Empty, result.AuthorizationCode);
    }

    // ---------- Failure translation ----------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ProcessPayment_WhenBankReturns5xx_ThrowsBankUnavailable(HttpStatusCode statusCode)
    {
        SetupResponse(Json(statusCode, "{}"));

        await Assert.ThrowsAsync<BankUnavailableException>(() => CreateClient().ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenBankReturns4xx_ThrowsBankInvalidResponse()
    {
        SetupResponse(Json(HttpStatusCode.BadRequest, """{ "error_message": "Not all required properties were sent in the request" }"""));

        await Assert.ThrowsAsync<BankInvalidResponseException>(() => CreateClient().ProcessPaymentAsync(ValidRequest()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task ProcessPayment_WhenBodyIsUnreadable_ThrowsBankInvalidResponse(string body)
    {
        SetupResponse(Json(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<BankInvalidResponseException>(() => CreateClient().ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenBankIsUnreachable_ThrowsBankUnavailable()
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<BankUnavailableException>(() => CreateClient().ProcessPaymentAsync(ValidRequest()));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankIsTooSlow_ThrowsBankTimeout()
    {
        SetupHangingBank();
        var client = CreateClient(timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<BankTimeoutException>(() => client.ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenCallerCancels_PropagatesCancellationNotTimeout()
    {
        // The timeout catch must only fire for HttpClient's own timeout, not a caller's cancellation.
        SetupHangingBank();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient().ProcessPaymentAsync(ValidRequest(), cts.Token));
        Assert.IsNotType<BankTimeoutException>(ex);
    }

    // ---------- Helpers ----------

    private BankClient CreateClient(TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(_handler.Object)
        {
            BaseAddress = new Uri("http://bank.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
        return new BankClient(httpClient, NullLogger<BankClient>.Instance);
    }

    private void SetupResponse(HttpResponseMessage response) =>
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

    private void SetupHangingBank() =>
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static BankClientRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryDate = "04/2030",
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };
}
