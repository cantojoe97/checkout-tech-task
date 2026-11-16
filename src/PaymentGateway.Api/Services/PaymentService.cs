using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankClient _bankClient;

    public PaymentService(IPaymentsRepository paymentsRepository,  IBankClient bankClient)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
    }

    public PaymentResponse GetPayment(Guid paymentId)
    {
        if (paymentId == Guid.Empty)
        {
            return null;
        }

        var payment = _paymentsRepository.Get(paymentId);

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

            return rejectedResponse;
        }

        var bankClientRequest = new BankClientRequest()
        {
            CardNumber = payment.CardNumber.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            CVV = payment.CVV,
            ExpiryDate = $"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}",
        };
        
       var bankClientResponse = await _bankClient.ProcessPaymentAsync(bankClientRequest);

       var paymentResponse = new PaymentResponse()
       {
           Id = Guid.NewGuid(),
           Amount = payment.Amount,
           Currency = payment.Currency,
           ExpiryMonth = payment.ExpiryMonth,
           ExpiryYear = payment.ExpiryYear,
           Status = bankClientResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined
       };
       paymentResponse.SetCardNumberLastFour(payment.CardNumber);

       _paymentsRepository.Add(paymentResponse);

       return paymentResponse;
    }
}