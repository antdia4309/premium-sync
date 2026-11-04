using System.Text.Json;
using System.Text.Json.Serialization;

namespace PremiumSync.Contracts;

public enum TransactionType
{
    NewBusiness,
    Endorsement,
    Cancellation
}

/// <summary>
/// The message the policy system sends to billing for every premium-bearing transaction.
/// This is the integration contract: both sides must agree on it.
/// </summary>
public record PolicyTransactionMessage(
    Guid TransactionId,        // unique per transaction; becomes the idempotency key in Phase 2
    string PolicyNumber,
    int Sequence,              // 1 = new business, then 2, 3... in the order they happened
    TransactionType Type,
    DateOnly EffectiveDate,
    decimal PremiumChange,     // prorated amount for this transaction; negative for returns
    decimal FeeAmount,
    decimal CommissionRate);   // e.g. 0.15 for 15%

public static class ContractJson
{
    /// <summary>Shared JSON settings so both services serialize the contract the same way.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Status codes that are part of the integration contract.</summary>
public static class ContractStatusCodes
{
    /// <summary>
    /// 425 Too Early: billing received a sequence ahead of the one it expects (e.g. 3 before 2).
    /// The sender should retry later, once the earlier transaction has been delivered.
    /// </summary>
    public const int OutOfSequence = 425;
}
