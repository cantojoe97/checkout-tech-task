namespace PaymentGateway.Api.Models.Responses;

public class BankClientResponse
{
    public bool Authorized { get; set; }
    public string? AuthorizationCode { get; set; }
}