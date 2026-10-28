using PremiumSync.Contracts;

namespace PremiumSync.PolicyService.Billing;

/// <summary>Sends transactions to the billing service's inbound endpoint.</summary>
public class BillingClient(HttpClient http)
{
    public async Task<(bool Ok, string? Error)> SendAsync(PolicyTransactionMessage message, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/inbound/transactions", message, ContractJson.Options, ct);
            if (response.IsSuccessStatusCode) return (true, null);

            var body = await response.Content.ReadAsStringAsync(ct);
            return (false, $"{(int)response.StatusCode}: {Truncate(body, 500)}");
        }
        catch (HttpRequestException ex)
        {
            // Billing is down or unreachable. The transaction stays Failed so Phase 2 can retry it.
            return (false, $"Billing unreachable: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
