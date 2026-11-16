using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    public PaymentResponse? GetPayment(Guid paymentId);

    public Task<PaymentResponse> ProcessPaymentAsync(PostPaymentRequest payment);
}