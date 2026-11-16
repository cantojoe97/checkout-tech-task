namespace PaymentGateway.Api.Models.Requests;

public class BankClientRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string CVV { get; set; } = string.Empty;
}
