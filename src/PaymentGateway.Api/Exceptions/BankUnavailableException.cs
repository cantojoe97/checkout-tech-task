namespace PaymentGateway.Api.Exceptions;

/// <summary>
/// The payment couldn't be completed with the bank: it was unreachable, timed out, returned an error
/// or sent a response we couldn't read. BankClient logs which; callers only need to know it failed.
/// </summary>
public class BankUnavailableException : Exception
{
    public BankUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
