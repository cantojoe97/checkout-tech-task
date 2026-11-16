using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentsRepository> _paymentsRepository = new();
    private readonly Mock<IBankClient> _bankClient = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _sut = new PaymentService(_paymentsRepository.Object, _bankClient.Object, NullLogger<PaymentService>.Instance);
    }

    [Fact]
    public void GetPayment_WhenIdExists_ReturnsPayment()
    {
        // Arrange
        var payment = new PaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            CardNumberLastFour = 8877,
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 100
        };
        _paymentsRepository.Setup(r => r.Get(payment.Id)).Returns(payment);

        // Act
        var result = _sut.GetPayment(payment.Id);

        // Assert
        Assert.Same(payment, result);
        _paymentsRepository.Verify(r => r.Get(payment.Id), Times.Once);
        VerifyBankNeverCalled();
    }

    [Fact]
    public void GetPayment_WhenIdDoesNotExist_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        _paymentsRepository.Setup(r => r.Get(id)).Returns((PaymentResponse?)null);

        // Act
        var result = _sut.GetPayment(id);

        // Assert
        Assert.Null(result);
        _paymentsRepository.Verify(r => r.Get(id), Times.Once);
        VerifyBankNeverCalled();
    }

    [Fact]
    public void GetPayment_WhenIdIsEmptyGuid_ReturnsNullWithoutQueryingRepository()
    {
        // Act
        var result = _sut.GetPayment(Guid.Empty);

        // Assert
        Assert.Null(result);
        _paymentsRepository.Verify(r => r.Get(It.IsAny<Guid>()), Times.Never);
        VerifyBankNeverCalled();
    }

    // ---------- ProcessPaymentAsync ----------

    [Fact]
    public async Task ProcessPayment_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.ProcessPaymentAsync(null!));

        VerifyBankNeverCalled();
    }

    [Fact]
    public async Task ProcessPayment_WhenRequestIsValid_CallsBankAndReturnsItsOutcome()
    {
        // Arrange
        SetupBank(authorized: true);
        var request = ValidRequest();

        // Act
        var result = await _sut.ProcessPaymentAsync(request);

        // Assert
        Assert.Equal(PaymentStatus.Authorized, result.Status);
        _bankClient.Verify(
            c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // CardNumber: 14-19 digits.
    [Theory]
    [InlineData(12345678901234L)]            // 14 digits (minimum)
    [InlineData(1234567890123456L)]          // 16 digits
    [InlineData(1234567890123456789L)]       // 19 digits (maximum)
    public async Task ProcessPayment_WhenCardNumberIsValid_IsNotRejected(long cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        await AssertNotRejected(request);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1234567890123L)]             // 13 digits
    [InlineData(-12345678901234L)]           // negative
    public async Task ProcessPayment_WhenCardNumberIsInvalid_IsRejected(long cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        await AssertRejected(request);
    }

    // CVV: 3-4 characters, digits only.
    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("012")]                      // leading zero is kept because CVV is a string
    public async Task ProcessPayment_WhenCvvIsValid_IsNotRejected(string cvv)
    {
        var request = ValidRequest();
        request.CVV = cvv;

        await AssertNotRejected(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]                       // too short
    [InlineData("12345")]                    // too long
    [InlineData("12a")]                      // non-numeric
    [InlineData(" 123")]                     // whitespace
    public async Task ProcessPayment_WhenCvvIsInvalid_IsRejected(string? cvv)
    {
        var request = ValidRequest();
        request.CVV = cvv!;

        await AssertRejected(request);
    }

    // Amount: an integer in minor units (e.g. 1050 = £10.50). The int type already guarantees
    // it's a whole number; JSON like 10.5 never reaches the service (model binding returns 400).
    [Theory]
    [InlineData(1)]
    [InlineData(1050)]
    [InlineData(int.MaxValue)]
    public async Task ProcessPayment_WhenAmountIsAnInteger_IsNotRejected(int amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        await AssertNotRejected(request);
    }

    // Currency: 3-letter ISO 4217 code from the supported list.
    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public async Task ProcessPayment_WhenCurrencyIsSupported_IsNotRejected(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        await AssertNotRejected(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GB")]                       // too short
    [InlineData("GBPP")]                     // too long
    [InlineData("123")]                      // not letters
    [InlineData("ABC")]                      // 3 letters but not a supported ISO code
    public async Task ProcessPayment_WhenCurrencyIsInvalid_IsRejected(string? currency)
    {
        var request = ValidRequest();
        request.Currency = currency!;

        await AssertRejected(request);
    }

    // ExpiryMonth: 1-12.
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public async Task ProcessPayment_WhenExpiryMonthIsInRange_IsNotRejected(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;

        await AssertNotRejected(request);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task ProcessPayment_WhenExpiryMonthIsOutOfRange_IsRejected(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;

        await AssertRejected(request);
    }

    // Expiry: the month/year together must not be in the past. A card is valid until the end
    // of its expiry month, so the current month is still accepted.
    [Fact]
    public async Task ProcessPayment_WhenExpiryYearIsInTheFuture_IsNotRejected()
    {
        var request = ValidRequest();
        request.ExpiryYear = DateTime.UtcNow.Year + 1;

        await AssertNotRejected(request);
    }

    [Fact]
    public async Task ProcessPayment_WhenExpiryIsCurrentMonth_IsNotRejected()
    {
        var now = DateTime.UtcNow;
        var request = ValidRequest();
        request.ExpiryMonth = now.Month;
        request.ExpiryYear = now.Year;

        await AssertNotRejected(request);
    }

    [Fact]
    public async Task ProcessPayment_WhenExpiryYearIsInThePast_IsRejected()
    {
        var request = ValidRequest();
        request.ExpiryYear = DateTime.UtcNow.Year - 1;

        await AssertRejected(request);
    }

    [Fact]
    public async Task ProcessPayment_WhenExpiryIsLastMonth_IsRejected()
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        var request = ValidRequest();
        request.ExpiryMonth = lastMonth.Month;
        request.ExpiryYear = lastMonth.Year;

        await AssertRejected(request);
    }

    // ---------- Helpers ----------

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = 2222405343248877,
        ExpiryMonth = 4,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };

    private void SetupBank(bool authorized) =>
        _bankClient
            .Setup(c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankClientResponse
            {
                Authorized = authorized,
                AuthorizationCode = authorized ? Guid.NewGuid().ToString() : string.Empty
            });

    private async Task AssertRejected(PostPaymentRequest request)
    {
        SetupBank(authorized: true);

        var result = await _sut.ProcessPaymentAsync(request);

        Assert.Equal(PaymentStatus.Rejected, result.Status);
        VerifyBankNeverCalled();
    }

    private async Task AssertNotRejected(PostPaymentRequest request)
    {
        SetupBank(authorized: true);

        var result = await _sut.ProcessPaymentAsync(request);

        Assert.NotEqual(PaymentStatus.Rejected, result.Status);
        _bankClient.Verify(
            c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void VerifyBankNeverCalled() =>
        _bankClient.Verify(
            c => c.ProcessPaymentAsync(It.IsAny<BankClientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
}
