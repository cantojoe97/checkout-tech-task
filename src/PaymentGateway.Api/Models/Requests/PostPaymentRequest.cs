namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    private static readonly HashSet<string> SupportedCurrencies = new() { "GBP", "USD", "EUR" };
    
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string CVV { get; set; } = string.Empty;

    public bool IsValid(DateTime now)
    {
        if (CardNumber is null || CardNumber.Length is < 14 or > 19 || !CardNumber.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (CVV is null || CVV.Length is < 3 or > 4 || !CVV.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (Currency is null || !SupportedCurrencies.Contains(Currency))
        {
            return false;
        }

        if (Amount <= 0)
        {
            return false;
        }

        if (ExpiryMonth is < 1 or > 12)
        {
            return false;
        }

        // A card is valid until the end of its expiry month, so the current month is accepted.
        if (ExpiryYear < now.Year || (ExpiryYear == now.Year && ExpiryMonth < now.Month))
        {
            return false;
        }

        return true;
    }
}
