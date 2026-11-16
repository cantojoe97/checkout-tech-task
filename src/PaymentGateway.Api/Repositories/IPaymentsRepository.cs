using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Repositories;

public interface IPaymentsRepository
{
    public void Add(PaymentResponse payment);

    public PaymentResponse? Get(Guid id);
}