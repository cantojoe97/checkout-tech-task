using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Exceptions;

namespace PaymentGateway.Api.ExceptionHandling;

/// <summary>
/// Decides what callers see when something throws: a ProblemDetails body with a generic message.
/// The exception itself only goes to the logs.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int statusCode;
        string title;

        if (exception is BankUnavailableException)
        {
            // BankClient has already logged the detail.
            _logger.LogWarning("Bank unavailable processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            statusCode = StatusCodes.Status503ServiceUnavailable;
            title = "The acquiring bank is unavailable. Please try again later.";
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            statusCode = StatusCodes.Status500InternalServerError;
            title = "An unexpected error occurred.";
        }

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = statusCode, Title = title }
        });
    }
}
