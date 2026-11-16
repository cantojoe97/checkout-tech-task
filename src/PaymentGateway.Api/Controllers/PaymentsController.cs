using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<PaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.GetPayment(id);

        return payment is null ? NotFound(id) : Ok(payment);
    }

    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> ProcessPaymentAsync(PostPaymentRequest request)
    {
        var response = await _paymentService.ProcessPaymentAsync(request);

        return response.Status == PaymentStatus.Rejected ? BadRequest(response) : Ok(response);
    }
}
