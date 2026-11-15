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

    // `now` is passed in rather than read from DateTime.UtcNow so the expiry rule can be tested at a fixed date.
    // Deliberately not IValidatableObject: ASP.NET would run that during model binding and return its own 400
    // before the service could return a Rejected payment.
    public bool IsValid(DateTime now)
    {
        // Card number: 14-19 digits. A long can only hold digits, so just check it's positive and its length.
        var cardNumberLength = CardNumber.ToString().Length;
        if (CardNumber <= 0 || cardNumberLength < 14 || cardNumberLength > 19)
        {
            return false;
        }

        // CVV: 3-4 characters, digits only.
        if (CVV is null || CVV.Length is < 3 or > 4 || !CVV.All(char.IsAsciiDigit))
        {
            return false;
        }

        // Currency: a supported 3-letter ISO 4217 code.
        if (Currency is null || !SupportedCurrencies.Contains(Currency))
        {
            return false;
        }

        // Expiry month: 1-12.
        if (ExpiryMonth is < 1 or > 12)
        {
            return false;
        }

        // Expiry date: a card is valid until the end of its expiry month.
        if (ExpiryYear < now.Year || (ExpiryYear == now.Year && ExpiryMonth < now.Month))
        {
            return false;
        }

        // Amount: the int type already guarantees a whole number, so there's nothing more to check.
        return true;
    }
}
