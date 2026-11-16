using System.Collections.Concurrent;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PaymentResponse> _payments = new();
    private readonly ILogger<PaymentsRepository> _logger;

    public PaymentsRepository(ILogger<PaymentsRepository> logger)
    {
        _logger = logger;
    }

    public void Add(PaymentResponse payment)
    {
        if (!_payments.TryAdd(payment.Id, payment))
        {
            // Ids come from Guid.NewGuid(), so this should never happen. If it does, the existing
            // payment is kept and the new one is lost, which needs looking into.
            _logger.LogError("Payment {PaymentId} already exists; new payment not stored", payment.Id);
            return;
        }

        _logger.LogDebug("Payment {PaymentId} stored", payment.Id);
    }

    public PaymentResponse? Get(Guid id)
    {
        return _payments.TryGetValue(id, out var payment) ? payment : null;
    }
}
