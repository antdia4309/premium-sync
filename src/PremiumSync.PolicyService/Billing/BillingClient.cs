using System.Net;
using PremiumSync.Contracts;

namespace PremiumSync.PolicyService.Billing;

/// <summary>Result of one send attempt. Retryable tells the caller whether trying again could help.</summary>
public record SendResult(bool Ok, string? Error, bool Retryable)
{
    public static readonly SendResult Success = new(true, null, false);
}

public record BillingTotals(decimal TotalPremium, decimal TotalCommission, int TransactionCount);

/// <summary>Sends transactions to the billing service's inbound endpoint.</summary>
public class BillingClient(HttpClient http)
{
    public async Task<SendResult> SendAsync(PolicyTransactionMessage message, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/inbound/transactions", message, ContractJson.Options, ct);
            if (response.IsSuccessStatusCode) return SendResult.Success;

            var body = await response.Content.ReadAsStringAsync(ct);
            return new SendResult(false, $"{(int)response.StatusCode}: {Truncate(body, 500)}", IsRetryable(response.StatusCode));
        }
        catch (HttpRequestException ex)
        {
            // Billing is down or unreachable: worth retrying.
            return new SendResult(false, $"Billing unreachable: {ex.Message}", Retryable: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout. Billing may or may not have booked it; safe to retry because billing is idempotent.
            return new SendResult(false, "Billing timed out.", Retryable: true);
        }
    }

    /// <summary>
    /// 5xx, 408 (timeout), 425 (too early: an earlier sequence hasn't arrived), and 429 (too many requests)
    /// are temporary: try again later.
    /// Other 4xx mean billing rejected the data itself (400 validation, 409 conflict): retrying won't change the answer.
    /// </summary>
    public static bool IsRetryable(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)status == ContractStatusCodes.OutOfSequence;

    /// <summary>Billing's totals for one policy, or null if billing has nothing for it.</summary>
    public async Task<BillingTotals?> GetTotalsAsync(string policyNumber, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/billing/accounts/{Uri.EscapeDataString(policyNumber)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BillingTotals>(ContractJson.Options, ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
