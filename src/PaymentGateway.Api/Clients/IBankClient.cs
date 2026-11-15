using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Clients;

public interface IBankClient
{
    Task<BankClientResponse> ProcessPaymentAsync(BankClientRequest request, CancellationToken cancellationToken = default);
}
