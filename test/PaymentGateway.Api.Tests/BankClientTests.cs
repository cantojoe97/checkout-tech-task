using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Tests.TestHelpers;

using static PaymentGateway.Api.Tests.TestHelpers.StubHttpMessageHandler;

namespace PaymentGateway.Api.Tests;

public class BankClientTests
{
    // ---------- Contract with the bank (snake_case JSON, string card number) ----------

    [Fact]
    public async Task ProcessPayment_SendsSnakeCaseRequestAndReadsAuthorizedResponse()
    {
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            sentBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """{ "authorized": true, "authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad" }""");
        });

        var result = await CreateClient(handler).ProcessPaymentAsync(ValidRequest());

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
        var handler = Returning(HttpStatusCode.OK, """{ "authorized": false, "authorization_code": "" }""");

        var result = await CreateClient(handler).ProcessPaymentAsync(ValidRequest());

        Assert.False(result.Authorized);
        Assert.Equal(string.Empty, result.AuthorizationCode);
    }

    // ---------- Failure translation ----------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ProcessPayment_WhenBankReturns5xx_ThrowsBankUnavailable(HttpStatusCode statusCode)
    {
        var client = CreateClient(Returning(statusCode, "{}"));

        await Assert.ThrowsAsync<BankUnavailableException>(() => client.ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenBankReturns4xx_ThrowsBankUnavailable()
    {
        var client = CreateClient(Returning(HttpStatusCode.BadRequest, """{ "error_message": "Not all required properties were sent in the request" }"""));

        await Assert.ThrowsAsync<BankUnavailableException>(() => client.ProcessPaymentAsync(ValidRequest()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task ProcessPayment_WhenBodyIsUnreadable_ThrowsBankUnavailable(string body)
    {
        var client = CreateClient(Returning(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<BankUnavailableException>(() => client.ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenBankIsUnreachable_ThrowsBankUnavailable()
    {
        var client = CreateClient(Throwing(new HttpRequestException("Connection refused")));

        var ex = await Assert.ThrowsAsync<BankUnavailableException>(() => client.ProcessPaymentAsync(ValidRequest()));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankIsTooSlow_ThrowsBankUnavailable()
    {
        var client = CreateClient(Hanging(), timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<BankUnavailableException>(() => client.ProcessPaymentAsync(ValidRequest()));
    }

    [Fact]
    public async Task ProcessPayment_WhenCallerCancels_PropagatesCancellationNotTimeout()
    {
        // The timeout catch must only fire for HttpClient's own timeout, not a caller's cancellation.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(Hanging()).ProcessPaymentAsync(ValidRequest(), cts.Token));
        Assert.IsNotType<BankUnavailableException>(ex);
    }

    // ---------- Helpers ----------

    private static BankClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://bank.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        }, NullLogger<BankClient>.Instance);

    private static BankClientRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryDate = "04/2030",
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };
}
