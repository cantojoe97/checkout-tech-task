using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

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

    public PaymentResponse GetPayment(Guid paymentId)
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
            var rejectedResponse = new PaymentResponse()
            {
                Status = PaymentStatus.Rejected,
                Amount = payment.Amount,
                Currency = payment.Currency,
                ExpiryMonth = payment.ExpiryMonth,
                ExpiryYear = payment.ExpiryYear
            };
            rejectedResponse.SetCardNumberLastFour(payment.CardNumber);

            // Rejected payments are never stored or given an id, so there is no PaymentId to log.
            _logger.LogInformation(
                "Payment rejected by validation for {Amount} {Currency}, card ending {CardNumberLastFour}",
                rejectedResponse.Amount, rejectedResponse.Currency, rejectedResponse.CardNumberLastFour);

            return rejectedResponse;
        }

        var paymentId = Guid.NewGuid();

        // Every log line written while this payment is processed, including BankClient's,
        // carries the PaymentId as a structured field.
        using var _ = _logger.BeginScope("PaymentId:{PaymentId}", paymentId);

        var paymentResponse = new PaymentResponse()
        {
            Id = paymentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear
        };
        paymentResponse.SetCardNumberLastFour(payment.CardNumber);

        _logger.LogInformation(
            "Processing payment {PaymentId} for {Amount} {Currency}, card ending {CardNumberLastFour}",
            paymentId, paymentResponse.Amount, paymentResponse.Currency, paymentResponse.CardNumberLastFour);

        var bankClientRequest = new BankClientRequest()
        {
            CardNumber = payment.CardNumber.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            CVV = payment.CVV,
            ExpiryDate = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}",
        };

        // No caller CancellationToken on purpose: once the request is sent the bank may charge the card,
        // so we always wait for its answer and record it. Bank failures throw and are turned into
        // 502/503/504 responses by GlobalExceptionHandler.
        var bankClientResponse = await _bankClient.ProcessPaymentAsync(bankClientRequest);

        paymentResponse.Status = bankClientResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;

        if (bankClientResponse.Authorized && string.IsNullOrEmpty(bankClientResponse.AuthorizationCode))
        {
            _logger.LogWarning("Payment {PaymentId} authorized by the bank without an authorization code", paymentId);
        }

        _paymentsRepository.Add(paymentResponse);

        _logger.LogInformation("Payment {PaymentId} {PaymentStatus}", paymentId, paymentResponse.Status);

        return paymentResponse;
    }
}
