using System.Collections.Concurrent;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    private ConcurrentDictionary<Guid, PaymentResponse> Payments = new();
    
    public void Add(PaymentResponse payment)
    {
        Payments.GetOrAdd(payment.Id, payment);
    }

    public PaymentResponse? Get(Guid id)
    {
        var returnValue = Payments.TryGetValue(id, out var payment);
        return returnValue ? payment : null;
    }
}