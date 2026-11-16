namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    private static readonly HashSet<string> SupportedCurrencies = new() { "GBP", "USD", "EUR" };

    public long CardNumber { get; set; }
    public int  ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string Currency { get; set; }
    public int Amount { get; set; }
    public string CVV { get; set; }
    
    public bool IsValid(DateTime now)
    {
        var cardNumberLength = CardNumber.ToString().Length;
        if (CardNumber <= 0 || cardNumberLength < 14 || cardNumberLength > 19)
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

        if (ExpiryMonth is < 1 or > 12)
        {
            return false;
        }

        if (ExpiryYear < now.Year || (ExpiryYear == now.Year && ExpiryMonth < now.Month))
        {
            return false;
        }

        return true;
    }
}
