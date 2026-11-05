using System.Reflection;

namespace PremiumSync.Reconciliation.Engine;

/// <summary>Loads the .sql files embedded in this assembly (see the Sql folder).</summary>
public static class SqlQueries
{
    public static string Load(string name)
    {
        var assembly = typeof(SqlQueries).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Sql.{name}.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
