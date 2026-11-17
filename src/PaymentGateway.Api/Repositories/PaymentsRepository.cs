using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Repositories;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PaymentResponse> _payments = new();
    public void Add(PaymentResponse payment)
    {
        if (!_payments.TryAdd(payment.Id, payment))
        {
            throw new InvalidOperationException($"Payment {payment.Id} already exists.");
        }
    }

    public PaymentResponse? Get(Guid id) => _payments.TryGetValue(id, out var payment) ? payment : null;
}
