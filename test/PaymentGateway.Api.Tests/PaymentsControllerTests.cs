using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

// The HTTP layer: routing, model binding, status codes and the error format. The service is mocked,
// so these tests don't depend on validation or the bank.
public class PaymentsControllerTests : IDisposable
{
    private readonly Mock<IPaymentService> _service = new();
    private readonly WebApplicationFactory<PaymentsController> _factory;
    private readonly HttpClient _client;

    public PaymentsControllerTests()
    {
        _factory = new WebApplicationFactory<PaymentsController>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(_service.Object)));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ---------- GET ----------

    [Fact]
    public async Task Get_WhenPaymentExists_Returns200WithThePayment()
    {
        var payment = new PaymentResponse
        {
            Id = Guid.NewGuid(), Status = PaymentStatus.Authorized, CardNumberLastFour = "0123", Currency = "GBP"
        };
        _service.Setup(s => s.GetPayment(payment.Id)).Returns(payment);

        var response = await _client.GetAsync($"/api/Payments/{payment.Id}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payment.Id, body.RootElement.GetProperty("id").GetGuid());
        // The contract merchants see: the status by name and the last four as text, leading zero kept.
        Assert.Equal("Authorized", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("0123", body.RootElement.GetProperty("cardNumberLastFour").GetString());
    }

    [Fact]
    public async Task Get_WhenPaymentDoesNotExist_Returns404()
    {
        var response = await _client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST ----------

    [Theory]
    [InlineData(PaymentStatus.Authorized, HttpStatusCode.OK)]
    [InlineData(PaymentStatus.Declined, HttpStatusCode.OK)]
    [InlineData(PaymentStatus.Rejected, HttpStatusCode.BadRequest)]
    public async Task Post_ReturnsStatusCodeForTheOutcome(PaymentStatus status, HttpStatusCode expected)
    {
        _service.Setup(s => s.ProcessPaymentAsync(It.IsAny<PostPaymentRequest>()))
            .ReturnsAsync(new PaymentResponse { Status = status, Currency = "GBP" });

        var response = await _client.PostAsync("/api/Payments", JsonContent.Create(AnyRequest()));
        var body = await response.Content.ReadFromJsonAsync<PaymentResponse>();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(status, body!.Status);
    }

    [Fact]
    public async Task Post_WhenBodyCannotBeRead_Returns400WithoutCallingTheService()
    {
        // An amount sent as text can't bind to an int, so [ApiController] answers before our code runs.
        var content = new StringContent("""{ "cardNumber": "2222405343248877", "amount": "ten pounds" }""", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/Payments", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        _service.Verify(s => s.ProcessPaymentAsync(It.IsAny<PostPaymentRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(typeof(BankUnavailableException), HttpStatusCode.ServiceUnavailable)]
    [InlineData(typeof(InvalidOperationException), HttpStatusCode.InternalServerError)]
    public async Task Post_WhenProcessingThrows_ReturnsProblemDetailsWithoutInternals(Type exceptionType, HttpStatusCode expected)
    {
        const string internalDetail = "internal detail that must not reach the caller";
        var exception = (Exception)Activator.CreateInstance(exceptionType, internalDetail, null)!;
        _service.Setup(s => s.ProcessPaymentAsync(It.IsAny<PostPaymentRequest>())).ThrowsAsync(exception);

        var response = await _client.PostAsync("/api/Payments", JsonContent.Create(AnyRequest()));
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain(internalDetail, body);
        Assert.DoesNotContain("   at ", body); // no stack trace
    }

    // The service is mocked, so the values don't matter; they only need every required field so
    // model binding lets the request through.
    private static PostPaymentRequest AnyRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };
}
