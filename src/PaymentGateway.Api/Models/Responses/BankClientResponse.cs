namespace PaymentGateway.Api.Models.Responses;

public class BankClientResponse
{
    public bool Authorized { get; set; }
    // String, not Guid: the bank returns "" for declined payments, which can't be deserialized into a Guid.
    public string? AuthorizationCode { get; set; }
}