using System.Globalization;
using Microsoft.Data.Sqlite;
using PremiumSync.Reconciliation.Data;

namespace PremiumSync.Reconciliation.Engine;

public record ReconResult(int PoliciesChecked, int TransactionsChecked, List<ReconFinding> Findings);

/// <summary>
/// Compares the policy and billing databases. Opens an empty in-memory database, attaches both as
/// "pol" and "bil", and runs each query in the Sql folder. Read-only: it never writes to either system.
/// </summary>
public static class ReconEngine
{
    /// <summary>Matches how EF Core stores DateTime in SQLite, so text comparison works.</summary>
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    public static ReconResult Run(string policyDbPath, string billingDbPath, DateTime cutoffUtc)
    {
        EnsureExists(policyDbPath, "Policy");
        EnsureExists(billingDbPath, "Billing");

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Attach(conn, policyDbPath, "pol");
        Attach(conn, billingDbPath, "bil");

        var cutoff = cutoffUtc.ToString(SqliteDateFormat, CultureInfo.InvariantCulture);
        var findings = new List<ReconFinding>();

        foreach (var r in Query(conn, "missing_in_billing"))
            findings.Add(Finding(ReasonCode.MissingInBilling, r, policyCents: r.Cents("PolicyCents"), billingCents: null,
                "Policy system marked this Sent, but billing has no record of it."));

        foreach (var r in Query(conn, "stuck_in_queue", ("@cutoffUtc", cutoff)))
            findings.Add(Finding(ReasonCode.StuckInQueue, r, policyCents: r.Cents("PolicyCents"), billingCents: null,
                $"{r.Str("SyncStatus")} since {r.Str("CreatedAtUtc")} UTC. {r.Str("SyncError")}".Trim()));

        foreach (var r in Query(conn, "missing_in_policy"))
            findings.Add(Finding(ReasonCode.MissingInPolicy, r, policyCents: null, billingCents: r.Cents("BillingCents"),
                "Billing booked a transaction the policy system has no record of."));

        foreach (var r in Query(conn, "amount_mismatch"))
        {
            if (r.Cents("PolicyPremiumCents") != r.Cents("BillingPremiumCents"))
                findings.Add(Finding(ReasonCode.PremiumMismatch, r, r.Cents("PolicyPremiumCents"), r.Cents("BillingPremiumCents"),
                    "Same transaction, different premium."));
            if (r.Cents("PolicyFeeCents") != r.Cents("BillingFeeCents"))
                findings.Add(Finding(ReasonCode.FeeMismatch, r, r.Cents("PolicyFeeCents"), r.Cents("BillingFeeCents"),
                    "Same transaction, different fee."));
        }

        foreach (var r in Query(conn, "commission_mismatch"))
            findings.Add(Finding(ReasonCode.CommissionMismatch, r, r.Cents("ExpectedCommissionCents"), r.Cents("BookedCommissionCents"),
                "Commission booked in billing doesn't match written premium x commission rate."));

        foreach (var r in Query(conn, "status_mismatch", ("@cutoffUtc", cutoff)))
            findings.Add(Finding(ReasonCode.StatusMismatch, r, null, null,
                $"Policy system says {r.Str("PolicyStatus")}; billing says {r.Str("BillingStatus")}."));

        var policies = Scalar(conn, "SELECT COUNT(*) FROM pol.Policies");
        var transactions = Scalar(conn, "SELECT COUNT(*) FROM pol.Transactions");
        return new ReconResult(policies, transactions, findings);
    }

    private static ReconFinding Finding(ReasonCode code, Row r, long? policyCents, long? billingCents, string detail)
    {
        decimal? policy = policyCents / 100m, billing = billingCents / 100m;
        return new ReconFinding
        {
            ReasonCode = code,
            PolicyNumber = r.Str("PolicyNumber") ?? "",
            TransactionId = r.Has("TransactionId") && Guid.TryParse(r.Str("TransactionId"), out var id) ? id : null,
            Sequence = r.Has("Sequence") ? (int?)r.Cents("Sequence") : null,
            PolicyValue = policy,
            BillingValue = billing,
            Difference = policy is not null && billing is not null ? billing - policy : null,
            Detail = detail,
        };
    }

    // ---------- Small helpers over ADO.NET ----------

    private static void EnsureExists(string path, string name)
    {
        // ATTACH would silently create an empty file, so check first.
        if (!File.Exists(path))
            throw new FileNotFoundException($"{name} database not found at {path}. Start the {name.ToLowerInvariant()} service at least once so it creates it.");
    }

    private static void Attach(SqliteConnection conn, string path, string alias)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ATTACH DATABASE @path AS {alias};";
        cmd.Parameters.AddWithValue("@path", path);
        cmd.ExecuteNonQuery();
    }

    private static int Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<Row> Query(SqliteConnection conn, string name, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlQueries.Load(name);
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);

        using var reader = cmd.ExecuteReader();
        var rows = new List<Row>();
        while (reader.Read())
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(new Row(values));
        }
        return rows;
    }

    private record Row(Dictionary<string, object?> Values)
    {
        public bool Has(string col) => Values.ContainsKey(col);
        public string? Str(string col) => Values.TryGetValue(col, out var v) ? v?.ToString() : null;
        public long Cents(string col) => Convert.ToInt64(Values[col], CultureInfo.InvariantCulture);
    }
}
