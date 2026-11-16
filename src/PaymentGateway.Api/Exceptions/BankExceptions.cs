namespace PaymentGateway.Api.Exceptions;

/// <summary>
/// Base type for any failure talking to the acquiring bank. Callers only need to know
/// "the bank call failed"; the subtypes say how, so the API can return the right status code.
/// </summary>
public abstract class BankException : Exception
{
    protected BankException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The bank couldn't be reached or returned a 5xx. Usually temporary.</summary>
public class BankUnavailableException : BankException
{
    public BankUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The bank didn't respond within the configured timeout.</summary>
public class BankTimeoutException : BankException
{
    public BankTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The bank answered, but not with something we can use: a 4xx (our request was malformed)
/// or a body we couldn't read.
/// </summary>
public class BankInvalidResponseException : BankException
{
    public BankInvalidResponseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
