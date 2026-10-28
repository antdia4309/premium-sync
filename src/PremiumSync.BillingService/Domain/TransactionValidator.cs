using PremiumSync.Contracts;

namespace PremiumSync.BillingService.Domain;

/// <summary>
/// Inbound contract checks. Billing never trusts the sender:
/// anything that breaks these rules is rejected with a 400 before it touches the ledger.
/// </summary>
public static class TransactionValidator
{
    public static Dictionary<string, string[]> Validate(PolicyTransactionMessage m)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string msg)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(msg);
        }

        if (m.TransactionId == Guid.Empty) Add(nameof(m.TransactionId), "TransactionId is required.");
        if (string.IsNullOrWhiteSpace(m.PolicyNumber)) Add(nameof(m.PolicyNumber), "PolicyNumber is required.");
        if (m.Sequence < 1) Add(nameof(m.Sequence), "Sequence must be 1 or greater.");
        if (m.CommissionRate is < 0 or > 0.5m) Add(nameof(m.CommissionRate), "CommissionRate must be between 0 and 0.5.");
        if (m.FeeAmount < 0) Add(nameof(m.FeeAmount), "FeeAmount cannot be negative.");
        if (decimal.Round(m.PremiumChange, 2) != m.PremiumChange) Add(nameof(m.PremiumChange), "PremiumChange must be in whole cents.");

        switch (m.Type)
        {
            case TransactionType.NewBusiness:
                if (m.Sequence != 1) Add(nameof(m.Sequence), "NewBusiness must be sequence 1.");
                if (m.PremiumChange <= 0) Add(nameof(m.PremiumChange), "NewBusiness premium must be positive.");
                break;
            case TransactionType.Endorsement:
                if (m.Sequence < 2) Add(nameof(m.Sequence), "Endorsements must come after NewBusiness (sequence 2+).");
                break;
            case TransactionType.Cancellation:
                if (m.Sequence < 2) Add(nameof(m.Sequence), "Cancellations must come after NewBusiness (sequence 2+).");
                if (m.PremiumChange > 0) Add(nameof(m.PremiumChange), "Cancellation premium cannot be positive.");
                break;
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
