using System.Diagnostics;
using System.Text.Json;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Clients;

public class BankClient : IBankClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Never log the request itself: it holds the full card number and CVV.
    public async Task<BankClientResponse> ProcessPaymentAsync(BankClientRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("payments", request, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var level = (int)response.StatusCode >= 500 ? LogLevel.Warning : LogLevel.Error;
                _logger.Log(level, "Bank returned {StatusCode} after {ElapsedMs}ms",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                throw new BankUnavailableException($"Bank returned {(int)response.StatusCode}.");
            }

            var bankResponse = await response.Content.ReadFromJsonAsync<BankClientResponse>(JsonOptions, cancellationToken)
                               ?? throw new BankUnavailableException("Bank returned an empty response body.");

            _logger.LogInformation("Bank responded {StatusCode}, authorized {Authorized}, in {ElapsedMs}ms",
                (int)response.StatusCode, bankResponse.Authorized, stopwatch.ElapsedMilliseconds);

            return bankResponse;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Bank call timed out after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw new BankUnavailableException("Bank did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach the bank after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw new BankUnavailableException("Could not reach the bank.", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Bank returned a response body that could not be read");
            throw new BankUnavailableException("Bank returned an unreadable response body.", ex);
        }
    }
}
