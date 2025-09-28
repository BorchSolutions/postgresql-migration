namespace DBMigrator.Core.Models.Schema;

public class DatabaseSchema
{
    public List<Table> Tables { get; set; } = new();
    public List<Function> Functions { get; set; } = new();
    public string SchemaName { get; set; } = "public";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string ConnectionString { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is not DatabaseSchema other) return false;
        return Tables.SequenceEqual(other.Tables) && 
               Functions.SequenceEqual(other.Functions);
    }

    public override int GetHashCode()
    {
        var tablesHash = Tables.Aggregate(0, (hash, table) => hash ^ table.GetHashCode());
        var functionsHash = Functions.Aggregate(0, (hash, func) => hash ^ func.GetHashCode());
        return HashCode.Combine(tablesHash, functionsHash);
    }
}