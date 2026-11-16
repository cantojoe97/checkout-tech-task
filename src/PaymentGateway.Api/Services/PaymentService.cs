using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Services;

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
        var payment = _paymentsRepository.Get(paymentId);

        if (payment is null)
        {
            _logger.LogInformation("Payment {PaymentId} not found", paymentId);
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

        _logger.LogInformation(
            "Processing payment {PaymentId} for {Amount} {Currency}, card ending {CardNumberLastFour}",
            paymentId, payment.Amount, payment.Currency, LastFour(payment.CardNumber));

        // No caller CancellationToken on purpose: once the request is sent the bank may charge the card,
        // so we always wait for its answer and record it. Bank failures throw BankUnavailableException,
        // which GlobalExceptionHandler turns into a 503.
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
        CardNumber = payment.CardNumber,
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

    private static string LastFour(string cardNumber) =>
        cardNumber is { Length: >= 4 } ? cardNumber[^4..] : string.Empty;
}
