using System.Text.Json;

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

    public BankClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BankClientResponse> ProcessPaymentAsync(BankClientRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("payments", request, JsonOptions, cancellationToken);
        
        response.EnsureSuccessStatusCode();

        var bankResponse = await response.Content.ReadFromJsonAsync<BankClientResponse>(JsonOptions, cancellationToken);

        return bankResponse ?? throw new InvalidOperationException("Bank returned an empty response body.");
    }
}
