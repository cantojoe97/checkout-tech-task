using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();

    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = new PaymentResponse
        {
            Id = Guid.NewGuid(),
            ExpiryYear = _random.Next(2023, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1111, 9999),
            Currency = "GBP"
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var bankClient = new Mock<IBankClient>();
        var client = CreateClient(paymentsRepository, bankClient.Object);

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<PaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(payment.Id, paymentResponse.Id);
        bankClient.Verify(
            c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAPaymentSuccessfully()
    {
        // Arrange
        var payment = new PostPaymentRequest()
        {
            CardNumber = 2222405343248877,
            ExpiryYear = DateTime.UtcNow.Year + _random.Next(1, 5),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            Currency = "GBP",
            CVV = "123"
        };

        HttpContent content = JsonContent.Create(payment);

        var paymentsRepository = new PaymentsRepository();
        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankClientResponse { Authorized = true, AuthorizationCode = Guid.NewGuid().ToString() });

        var client = CreateClient(paymentsRepository, bankClient.Object);

        // Act
        var response = await client.PostAsync($"/api/Payments", content);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
        Assert.Equal(8877, paymentResponse.CardNumberLastFour);
        Assert.NotNull(paymentsRepository.Get(paymentResponse.Id));
        bankClient.Verify(
            c => c.ProcessPaymentAsync(
                It.Is<BankClientRequest>(r => r.CardNumber == "2222405343248877" && r.Amount == payment.Amount),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Returns400AndRejectedStatusIfPaymentIsInvalid()
    {
        // Arrange
        var payment = new PostPaymentRequest()
        {
            CardNumber = 2222405343248877,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            ExpiryMonth = 4,
            Amount = 100,
            Currency = "GBP",
            CVV = "12" // invalid: too short
        };

        var paymentsRepository = new PaymentsRepository();
        var bankClient = new Mock<IBankClient>();
        var client = CreateClient(paymentsRepository, bankClient.Object);

        // Act
        var response = await client.PostAsync("/api/Payments", JsonContent.Create(payment));
        var paymentResponse = await response.Content.ReadFromJsonAsync<PaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
        bankClient.Verify(
            c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(typeof(BankUnavailableException), HttpStatusCode.ServiceUnavailable)]
    [InlineData(typeof(BankTimeoutException), HttpStatusCode.GatewayTimeout)]
    [InlineData(typeof(BankInvalidResponseException), HttpStatusCode.BadGateway)]
    [InlineData(typeof(InvalidOperationException), HttpStatusCode.InternalServerError)]
    public async Task ReturnsProblemDetailsWithoutInternalsWhenProcessingThrows(Type exceptionType, HttpStatusCode expectedStatus)
    {
        // Arrange
        const string internalDetail = "internal detail that must not reach the caller";
        var exception = (Exception)Activator.CreateInstance(exceptionType, internalDetail, null)!;

        var bankClient = new Mock<IBankClient>();
        bankClient
            .Setup(c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var client = CreateClient(new PaymentsRepository(), bankClient.Object);
        var payment = new PostPaymentRequest()
        {
            CardNumber = 2222405343248877,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            ExpiryMonth = 4,
            Amount = 100,
            Currency = "GBP",
            CVV = "123"
        };

        // Act
        var response = await client.PostAsync("/api/Payments", JsonContent.Create(payment));
        var body = await response.Content.ReadAsStringAsync();
        var problem = System.Text.Json.JsonSerializer.Deserialize<ProblemDetails>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        // Assert
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.DoesNotContain(internalDetail, body);
        Assert.DoesNotContain("   at ", body); // no stack trace
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ConfigureTestServices runs after Program.cs, so these registrations replace the app's own.
    private static HttpClient CreateClient(IPaymentsRepository paymentsRepository, IBankClient bankClient)
    {
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        return webApplicationFactory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(paymentsRepository);
                    services.AddSingleton(bankClient);
                }))
            .CreateClient();
    }
}
