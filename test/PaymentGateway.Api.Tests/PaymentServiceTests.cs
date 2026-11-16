using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

// What the service itself does: looking payments up, mapping to the bank request, turning the bank's
// answer into a status and storing the result. The validation rules are in PostPaymentRequestTests.
public class PaymentServiceTests
{
    private readonly Mock<IPaymentsRepository> _repository = new();
    private readonly Mock<IBankClient> _bank = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _sut = new PaymentService(_repository.Object, _bank.Object, NullLogger<PaymentService>.Instance);
    }

    // ---------- GetPayment ----------

    [Fact]
    public void GetPayment_WhenIdExists_ReturnsPayment()
    {
        var payment = new PaymentResponse { Id = Guid.NewGuid() };
        _repository.Setup(r => r.Get(payment.Id)).Returns(payment);

        Assert.Same(payment, _sut.GetPayment(payment.Id));
    }

    [Fact]
    public void GetPayment_WhenIdDoesNotExist_ReturnsNull()
    {
        Assert.Null(_sut.GetPayment(Guid.NewGuid()));
    }

    // ---------- ProcessPaymentAsync ----------

    [Fact]
    public async Task ProcessPayment_WhenRequestIsNull_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.ProcessPaymentAsync(null!));
    }

    [Fact]
    public async Task ProcessPayment_WhenRequestIsInvalid_IsRejectedWithoutCallingBankOrStoring()
    {
        var request = ValidRequest();
        request.CVV = "12";

        var result = await _sut.ProcessPaymentAsync(request);

        Assert.Equal(PaymentStatus.Rejected, result.Status);
        Assert.Equal(Guid.Empty, result.Id);
        Assert.Equal("8877", result.CardNumberLastFour);
        _bank.Verify(b => b.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.Add(It.IsAny<PaymentResponse>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPayment_SendsTheBankTheFieldsInItsFormat()
    {
        BankClientRequest? sent = null;
        _bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BankClientRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new BankClientResponse { Authorized = true, AuthorizationCode = "abc" });
        var request = ValidRequest();

        await _sut.ProcessPaymentAsync(request);

        Assert.NotNull(sent);
        Assert.Equal("2222405343248877", sent!.CardNumber);
        Assert.Equal($"04/{request.ExpiryYear}", sent.ExpiryDate); // month is zero-padded
        Assert.Equal("GBP", sent.Currency);
        Assert.Equal(100, sent.Amount);
        Assert.Equal("123", sent.CVV);
    }

    [Theory]
    [InlineData(true, PaymentStatus.Authorized)]
    [InlineData(false, PaymentStatus.Declined)]
    public async Task ProcessPayment_StoresAndReturnsTheBanksOutcome(bool authorized, PaymentStatus expectedStatus)
    {
        _bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankClientResponse { Authorized = authorized, AuthorizationCode = authorized ? "abc" : "" });
        var request = ValidRequest();

        var result = await _sut.ProcessPaymentAsync(request);

        Assert.Equal(expectedStatus, result.Status);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("8877", result.CardNumberLastFour);
        Assert.Equal(request.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, result.ExpiryYear);
        Assert.Equal(request.Currency, result.Currency);
        Assert.Equal(request.Amount, result.Amount);
        _repository.Verify(r => r.Add(result), Times.Once);
    }

    [Fact]
    public async Task ProcessPayment_KeepsLeadingZerosInTheLastFourDigits()
    {
        _bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankClientResponse { Authorized = true, AuthorizationCode = "abc" });
        var request = ValidRequest();
        request.CardNumber = "2222405343240123";

        var result = await _sut.ProcessPaymentAsync(request);

        Assert.Equal("0123", result.CardNumberLastFour);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankFails_StoresNothing()
    {
        _bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BankUnavailableException("down"));

        await Assert.ThrowsAsync<BankUnavailableException>(() => _sut.ProcessPaymentAsync(ValidRequest()));
        _repository.Verify(r => r.Add(It.IsAny<PaymentResponse>()), Times.Never);
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };
}
