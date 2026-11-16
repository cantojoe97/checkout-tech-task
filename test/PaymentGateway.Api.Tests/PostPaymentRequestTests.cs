using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests;

// Validation rules for a payment request. IsValid takes "now" as an argument, so these tests use a
// fixed date and never depend on when they run.
public class PostPaymentRequestTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string, Action<PostPaymentRequest>> ValidChanges => new()
    {
        { "unchanged", _ => { } },
        { "card number 14 digits (minimum)", r => r.CardNumber = 12345678901234 },
        { "card number 19 digits (maximum)", r => r.CardNumber = 1234567890123456789 },
        { "CVV 4 digits", r => r.CVV = "1234" },
        { "CVV with leading zero", r => r.CVV = "012" },
        { "currency USD", r => r.Currency = "USD" },
        { "currency EUR", r => r.Currency = "EUR" },
        { "amount 1 (minimum)", r => r.Amount = 1 },
        { "amount int.MaxValue", r => r.Amount = int.MaxValue },
        { "expiry month 1", r => r.ExpiryMonth = 1 },
        { "expiry month 12", r => r.ExpiryMonth = 12 },
        { "expires this month", r => { r.ExpiryMonth = 6; r.ExpiryYear = 2026; } },
    };

    public static TheoryData<string, Action<PostPaymentRequest>> InvalidChanges => new()
    {
        { "card number 0", r => r.CardNumber = 0 },
        { "card number 13 digits", r => r.CardNumber = 1234567890123 },
        { "card number negative", r => r.CardNumber = -12345678901234 },
        { "CVV null", r => r.CVV = null! },
        { "CVV empty", r => r.CVV = "" },
        { "CVV 2 digits", r => r.CVV = "12" },
        { "CVV 5 digits", r => r.CVV = "12345" },
        { "CVV with a letter", r => r.CVV = "12a" },
        { "CVV with whitespace", r => r.CVV = " 123" },
        { "currency null", r => r.Currency = null! },
        { "currency empty", r => r.Currency = "" },
        { "currency 2 letters", r => r.Currency = "GB" },
        { "currency 4 letters", r => r.Currency = "GBPP" },
        { "currency digits", r => r.Currency = "123" },
        { "currency not supported", r => r.Currency = "ABC" },
        { "currency lowercase", r => r.Currency = "gbp" },
        { "amount 0", r => r.Amount = 0 },
        { "amount negative", r => r.Amount = -1 },
        { "expiry month 0", r => r.ExpiryMonth = 0 },
        { "expiry month 13", r => r.ExpiryMonth = 13 },
        { "expired last month", r => { r.ExpiryMonth = 5; r.ExpiryYear = 2026; } },
        { "expired last year", r => { r.ExpiryMonth = 12; r.ExpiryYear = 2025; } },
    };

    [Theory]
    [MemberData(nameof(ValidChanges))]
    public void IsValid_ReturnsTrue(string scenario, Action<PostPaymentRequest> change)
    {
        var request = ValidRequest();
        change(request);

        Assert.True(request.IsValid(Now), scenario);
    }

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public void IsValid_ReturnsFalse(string scenario, Action<PostPaymentRequest> change)
    {
        var request = ValidRequest();
        change(request);

        Assert.False(request.IsValid(Now), scenario);
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = 2222405343248877,
        ExpiryMonth = 4,
        ExpiryYear = 2027,
        Currency = "GBP",
        Amount = 100,
        CVV = "123"
    };
}
