using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Exceptions;

namespace PaymentGateway.Api.ExceptionHandling;

/// <summary>
/// The one place that decides what callers see when something throws. Responses are ProblemDetails
/// with a safe, generic message; the full exception only goes to the logs.
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
        var (statusCode, title) = exception switch
        {
            BankUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The acquiring bank is unavailable. Please try again later."),
            BankTimeoutException => (StatusCodes.Status504GatewayTimeout, "The acquiring bank did not respond in time. Please try again later."),
            BankInvalidResponseException => (StatusCodes.Status502BadGateway, "The acquiring bank returned an unexpected response."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            // BankClient has already logged the detail; this ties the failure to the request.
            _logger.LogWarning("Bank failure ({ExceptionType}) processing {Method} {Path}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title
            }
        });
    }
}
