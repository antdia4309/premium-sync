using System.ComponentModel.DataAnnotations;
using PremiumSync.PolicyService.Data;

namespace PremiumSync.PolicyService;

public record CreatePolicyRequest(
    [property: Required, MinLength(2)] string InsuredName,
    DateOnly EffectiveDate,
    [property: Range(typeof(decimal), "1", "10000000")] decimal AnnualPremium,
    [property: Range(typeof(decimal), "0", "100000")] decimal FeeAmount,
    [property: Range(typeof(decimal), "0", "0.5")] decimal CommissionRate);

public record EndorseRequest(
    DateOnly EffectiveDate,
    decimal AnnualPremiumChange,   // annualized; the service prorates it
    [property: Range(typeof(decimal), "0", "100000")] decimal FeeAmount = 0);

public record CancelRequest(DateOnly EffectiveDate);

public record TransactionDto(Guid TransactionId, int Sequence, string Type, DateOnly EffectiveDate,
    decimal PremiumChange, decimal FeeAmount, string SyncStatus, string? SyncError)
{
    public static TransactionDto From(PolicyTransaction t) => new(t.TransactionId, t.Sequence, t.Type.ToString(),
        t.EffectiveDate, t.PremiumChange, t.FeeAmount, t.SyncStatus.ToString(), t.SyncError);
}

public record PolicyDto(string PolicyNumber, string InsuredName, DateOnly EffectiveDate, DateOnly ExpirationDate,
    decimal CommissionRate, string Status, decimal WrittenPremium, List<TransactionDto> Transactions)
{
    public static PolicyDto From(Policy p) => new(p.PolicyNumber, p.InsuredName, p.EffectiveDate, p.ExpirationDate,
        p.CommissionRate, p.Status.ToString(), p.WrittenPremium,
        p.Transactions.OrderBy(t => t.Sequence).Select(TransactionDto.From).ToList());
}
