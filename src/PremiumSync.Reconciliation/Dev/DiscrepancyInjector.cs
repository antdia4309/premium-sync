using System.Globalization;
using Microsoft.Data.Sqlite;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Engine;

namespace PremiumSync.Reconciliation.Dev;

public record InjectionResult(string Kind, string PolicyNumber, Guid? TransactionId, string Change, List<string> ExpectedFindings);

/// <summary>
/// DEV ONLY. Corrupts the policy and billing databases directly, bypassing every rule in both services,
/// so reconciliation has something real to find. Each kind targets one reason code and says which findings to expect
/// (some problems also show up as a second, knock-on finding).
/// </summary>
public static class DiscrepancyInjector
{
    public static readonly ReasonCode[] Kinds =
    [
        ReasonCode.MissingInBilling, ReasonCode.StuckInQueue, ReasonCode.MissingInPolicy, ReasonCode.PremiumMismatch,
        ReasonCode.FeeMismatch, ReasonCode.CommissionMismatch, ReasonCode.StatusMismatch, ReasonCode.EarnedPremiumMismatch,
    ];

    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    /// <summary>
    /// Injects one discrepancy. If policyNumber is null, picks a healthy policy: every transaction delivered,
    /// annual amounts present, and nothing reconciliation currently flags. `exclude` skips more policies.
    /// </summary>
    public static InjectionResult Inject(string policyDb, string billingDb, ReasonCode kind, string? policyNumber = null,
        ISet<string>? exclude = null)
    {
        foreach (var (path, name) in new[] { (policyDb, "Policy"), (billingDb, "Billing") })
            if (!File.Exists(path)) throw new FileNotFoundException($"{name} database not found at {path}.");

        // Never pick a policy reconciliation already flags (including ones injected earlier).
        var skip = new HashSet<string>(exclude ?? new HashSet<string>());
        if (policyNumber is null)
            skip.UnionWith(ReconEngine.Run(policyDb, billingDb, DateTime.UtcNow).Findings.Select(f => f.PolicyNumber));

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Exec(conn, "ATTACH DATABASE @p AS pol;", ("@p", policyDb));
        Exec(conn, "ATTACH DATABASE @p AS bil;", ("@p", billingDb));

        using var tx = conn.BeginTransaction();
        var needsCancelled = kind == ReasonCode.EarnedPremiumMismatch;
        var target = policyNumber ?? PickPolicy(conn, needsCancelled, skip)
            ?? throw new InvalidOperationException(
                $"No eligible policy for {kind}. Seed more data (POST /api/dev/seed on the policy service); " +
                (needsCancelled ? "this one needs a cancelled policy." : "it needs a policy where every transaction was delivered."));

        var last = LastTransaction(conn, target)
            ?? throw new InvalidOperationException($"Policy {target} has no transactions.");

        InjectionResult result = kind switch
        {
            ReasonCode.MissingInBilling => Do(conn, kind, target, last.Id,
                "Deleted the billing row for the policy's last transaction (policy side still says Sent).",
                ["MissingInBilling", "CommissionMismatch"],
                ("DELETE FROM bil.Transactions WHERE SourceTransactionId = @id;", [("@id", last.Id)])),

            ReasonCode.StuckInQueue => Do(conn, kind, target, last.Id,
                "Removed the last transaction from billing and marked it DeadLettered two hours ago on the policy side.",
                ["StuckInQueue"],
                ("DELETE FROM bil.Transactions WHERE SourceTransactionId = @id;", [("@id", last.Id)]),
                ("UPDATE pol.Transactions SET SyncStatus = 'DeadLettered', SyncError = 'Injected: billing rejected this transaction.', CreatedAtUtc = @old WHERE TransactionId = @id;",
                    [("@id", last.Id), ("@old", DateTime.UtcNow.AddHours(-2).ToString(SqliteDateFormat, CultureInfo.InvariantCulture))])),

            ReasonCode.MissingInPolicy => Do(conn, kind, target, null,
                "Booked a $100.00 endorsement in billing (no commission) that the policy system never sent.",
                ["MissingInPolicy"],
                ("""
                 INSERT INTO bil.Transactions (SourceTransactionId, PolicyNumber, Sequence, Type, EffectiveDate, Premium, FeeAmount, CommissionRate, CommissionAmount, ReceivedAtUtc)
                 SELECT @newId, PolicyNumber, MAX(Sequence) + 1, 'Endorsement', MAX(EffectiveDate), '100.00', '0', CommissionRate, '0', @now
                 FROM bil.Transactions WHERE PolicyNumber = @policy GROUP BY PolicyNumber, CommissionRate;
                 """,
                    [("@newId", Guid.NewGuid().ToString().ToUpperInvariant()), ("@policy", target),
                     ("@now", DateTime.UtcNow.ToString(SqliteDateFormat, CultureInfo.InvariantCulture))])),

            ReasonCode.PremiumMismatch => Do(conn, kind, target, last.Id,
                "Added $10.00 to the premium of the last transaction in billing only.",
                ["PremiumMismatch"],
                ("UPDATE bil.Transactions SET Premium = CAST(ROUND(CAST(Premium AS REAL) + 10, 2) AS TEXT) WHERE SourceTransactionId = @id;", [("@id", last.Id)])),

            ReasonCode.FeeMismatch => Do(conn, kind, target, last.Id,
                "Added a $25.00 fee to the last transaction in billing only.",
                ["FeeMismatch"],
                ("UPDATE bil.Transactions SET FeeAmount = CAST(ROUND(CAST(FeeAmount AS REAL) + 25, 2) AS TEXT) WHERE SourceTransactionId = @id;", [("@id", last.Id)])),

            ReasonCode.CommissionMismatch => Do(conn, kind, target, last.Id,
                "Added $0.10 to the commission on the last transaction in billing (the RCA 004 drift).",
                ["CommissionMismatch"],
                ("UPDATE bil.Transactions SET CommissionAmount = CAST(ROUND(CAST(CommissionAmount AS REAL) + 0.10, 2) AS TEXT) WHERE SourceTransactionId = @id;", [("@id", last.Id)])),

            ReasonCode.StatusMismatch => Do(conn, kind, target, null,
                "Set the policy's status to Cancelled with no cancellation transaction (the RCA 005 end state).",
                ["StatusMismatch"],
                ("UPDATE pol.Policies SET Status = 'Cancelled' WHERE PolicyNumber = @policy;", [("@policy", target)])),

            ReasonCode.EarnedPremiumMismatch => InjectEarnedPremium(conn, target),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), $"{kind} can't be injected."),
        };

        tx.Commit();
        return result;
    }

    /// <summary>Injects one of every kind, each on a different policy.</summary>
    public static List<InjectionResult> InjectAll(string policyDb, string billingDb)
    {
        var used = new HashSet<string>();
        var results = new List<InjectionResult>();
        // Cancelled-policy kind first, so it gets first pick of the (rarer) cancelled policies.
        foreach (var kind in Kinds.OrderByDescending(k => k == ReasonCode.EarnedPremiumMismatch))
        {
            var r = Inject(policyDb, billingDb, kind, exclude: used);
            used.Add(r.PolicyNumber);
            results.Add(r);
        }
        return results.OrderBy(r => Array.IndexOf(Kinds, Enum.Parse<ReasonCode>(r.Kind))).ToList();
    }

    /// <summary>
    /// Both systems agree, both are wrong: under-refund the cancellation by $50 on BOTH sides, and adjust billing's
    /// commission so commission still matches written premium. Only the independent recalculation can catch it.
    /// </summary>
    private static InjectionResult InjectEarnedPremium(SqliteConnection conn, string policy)
    {
        var cancel = ScalarString(conn, """
            SELECT t.TransactionId FROM pol.Transactions t JOIN pol.Policies p ON p.Id = t.PolicyId
            WHERE p.PolicyNumber = @p AND t.Type = 'Cancellation'
            """, ("@p", policy)) ?? throw new InvalidOperationException($"{policy} has no cancellation transaction.");

        var written = ScalarDecimal(conn, """
            SELECT SUM(CAST(t.PremiumChange AS REAL)) FROM pol.Transactions t JOIN pol.Policies p ON p.Id = t.PolicyId WHERE p.PolicyNumber = @p
            """, ("@p", policy));
        var rate = ScalarDecimal(conn, "SELECT CAST(CommissionRate AS REAL) FROM pol.Policies WHERE PolicyNumber = @p", ("@p", policy));
        const decimal delta = 50m;
        var commissionDelta = Round(Math.Round(written, 2) + delta, rate) - Round(Math.Round(written, 2), rate);

        return Do(conn, ReasonCode.EarnedPremiumMismatch, policy, Guid.Parse(cancel),
            "Reduced the cancellation refund by $50.00 in BOTH systems and kept commission consistent, so the systems still agree with each other.",
            ["EarnedPremiumMismatch"],
            ("UPDATE pol.Transactions SET PremiumChange = CAST(ROUND(CAST(PremiumChange AS REAL) + @d, 2) AS TEXT) WHERE TransactionId = @id;",
                [("@id", cancel), ("@d", (double)delta)]),
            ("UPDATE bil.Transactions SET Premium = CAST(ROUND(CAST(Premium AS REAL) + @d, 2) AS TEXT), CommissionAmount = CAST(ROUND(CAST(CommissionAmount AS REAL) + @c, 2) AS TEXT) WHERE SourceTransactionId = @id;",
                [("@id", cancel), ("@d", (double)delta), ("@c", (double)commissionDelta)]));
    }

    private static decimal Round(decimal premium, decimal rate) => Math.Round(premium * rate, 2, MidpointRounding.AwayFromZero);

    // ---------- Picking a target ----------

    /// <summary>
    /// A healthy policy: every transaction delivered and booked, status agrees with billing, annual amounts present.
    /// In force for most kinds; cancelled (with a cancellation in billing) for EarnedPremiumMismatch.
    /// </summary>
    private static string? PickPolicy(SqliteConnection conn, bool cancelled, ISet<string> exclude)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.PolicyNumber
            FROM pol.Policies p
            JOIN pol.Transactions t ON t.PolicyId = p.Id
            LEFT JOIN bil.Transactions b ON b.SourceTransactionId = t.TransactionId
            WHERE p.Status = @status
            GROUP BY p.PolicyNumber
            HAVING SUM(CASE WHEN t.SyncStatus <> 'Sent' OR b.Id IS NULL THEN 1 ELSE 0 END) = 0
               AND SUM(CASE WHEN t.Type <> 'Cancellation' AND t.AnnualPremiumChange IS NULL THEN 1 ELSE 0 END) = 0
            ORDER BY COUNT(*) DESC, p.PolicyNumber;
            """;
        cmd.Parameters.AddWithValue("@status", cancelled ? "Cancelled" : "InForce");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var number = reader.GetString(0);
            if (!exclude.Contains(number)) return number;
        }
        return null;
    }

    private record LastTxn(Guid Id);

    private static LastTxn? LastTransaction(SqliteConnection conn, string policy)
    {
        var id = ScalarString(conn, """
            SELECT t.TransactionId FROM pol.Transactions t JOIN pol.Policies p ON p.Id = t.PolicyId
            WHERE p.PolicyNumber = @p ORDER BY t.Sequence DESC LIMIT 1
            """, ("@p", policy));
        return id is null ? null : new LastTxn(Guid.Parse(id));
    }

    // ---------- ADO.NET helpers ----------

    private static InjectionResult Do(SqliteConnection conn, ReasonCode kind, string policy, Guid? txnId, string change,
        List<string> expected, params (string Sql, (string, object)[] Params)[] statements)
    {
        foreach (var (sql, ps) in statements)
        {
            var normalized = ps.Select(p => p.Item2 is Guid g ? (p.Item1, (object)g.ToString().ToUpperInvariant()) : p).ToArray();
            var rows = Exec(conn, sql, normalized);
            if (rows == 0) throw new InvalidOperationException($"Injection for {kind} on {policy} changed nothing. Is the policy already corrupted?");
        }
        return new InjectionResult(kind.ToString(), policy, txnId, change, expected);
    }

    private static int Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteNonQuery();
    }

    private static string? ScalarString(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar()?.ToString();
    }

    private static decimal ScalarDecimal(SqliteConnection conn, string sql, params (string Name, object Value)[] ps) =>
        Convert.ToDecimal(ScalarString(conn, sql, ps), CultureInfo.InvariantCulture);
}
