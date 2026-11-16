using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Services;

// Logging rules: only the last four digits of the card are ever logged. Never the full card number,
// the CVV or the request object. Bank failures are logged by BankClient and GlobalExceptionHandler,
// so this class doesn't log them again.
public class PaymentService : IPaymentService
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankClient _bankClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IPaymentsRepository paymentsRepository, IBankClient bankClient, ILogger<PaymentService> logger)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
        _logger = logger;
    }

    public PaymentResponse? GetPayment(Guid paymentId)
    {
        if (paymentId == Guid.Empty)
        {
            _logger.LogInformation("Payment lookup with an empty payment id");
            return null;
        }

        var payment = _paymentsRepository.Get(paymentId);

        if (payment is null)
        {
            _logger.LogInformation("Payment {PaymentId} not found", paymentId);
        }
        else
        {
            _logger.LogDebug("Payment {PaymentId} retrieved with status {PaymentStatus}", paymentId, payment.Status);
        }

        return payment;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(PostPaymentRequest payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!payment.IsValid(DateTime.UtcNow))
        {
            // Rejected payments are never stored or given an id, so there is no PaymentId to log.
            var rejected = CreateResponse(payment, Guid.Empty, PaymentStatus.Rejected);
            _logger.LogInformation(
                "Payment rejected by validation for {Amount} {Currency}, card ending {CardNumberLastFour}",
                rejected.Amount, rejected.Currency, rejected.CardNumberLastFour);

            return rejected;
        }

        var paymentId = Guid.NewGuid();

        // Every log line written while this payment is processed, including BankClient's,
        // carries the PaymentId as a structured field.
        using var _ = _logger.BeginScope("PaymentId:{PaymentId}", paymentId);

        _logger.LogInformation(
            "Processing payment {PaymentId} for {Amount} {Currency}, card ending {CardNumberLastFour}",
            paymentId, payment.Amount, payment.Currency, LastFour(payment.CardNumber));

        // No caller CancellationToken on purpose: once the request is sent the bank may charge the card,
        // so we always wait for its answer and record it. Bank failures throw and are turned into
        // 502/503/504 responses by GlobalExceptionHandler.
        var bankResponse = await _bankClient.ProcessPaymentAsync(ToBankRequest(payment));

        if (bankResponse.Authorized && string.IsNullOrEmpty(bankResponse.AuthorizationCode))
        {
            _logger.LogWarning("Payment {PaymentId} authorized by the bank without an authorization code", paymentId);
        }

        var response = CreateResponse(payment, paymentId,
            bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined);

        _paymentsRepository.Add(response);

        _logger.LogInformation("Payment {PaymentId} {PaymentStatus}", paymentId, response.Status);

        return response;
    }

    private static BankClientRequest ToBankRequest(PostPaymentRequest payment) => new()
    {
        CardNumber = payment.CardNumber.ToString(),
        ExpiryDate = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}",
        Currency = payment.Currency,
        Amount = payment.Amount,
        CVV = payment.CVV
    };

    private static PaymentResponse CreateResponse(PostPaymentRequest payment, Guid id, PaymentStatus status) => new()
    {
        Id = id,
        Status = status,
        CardNumberLastFour = LastFour(payment.CardNumber),
        ExpiryMonth = payment.ExpiryMonth,
        ExpiryYear = payment.ExpiryYear,
        Currency = payment.Currency,
        Amount = payment.Amount
    };

    private static int LastFour(long cardNumber) => (int)(Math.Abs(cardNumber) % 10000);
}
