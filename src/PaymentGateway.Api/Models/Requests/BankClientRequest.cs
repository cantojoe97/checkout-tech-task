using System.Runtime.InteropServices.JavaScript;

namespace PaymentGateway.Api.Models.Requests;

public class BankClientRequest
{
    // Must be a string: the bank expects "card_number" as a JSON string and errors on a number.
    public string CardNumber { get; set; }
    public string ExpiryDate { get; set; }
    public string Currency { get; set; }
    public int Amount { get; set; }
    public string CVV { get; set; }
}