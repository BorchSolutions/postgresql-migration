namespace DBMigrator.Core.Models.Schema;

public class Index
{
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsPrimary { get; set; }
    public string IndexType { get; set; } = "btree";
    public string? WhereClause { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Index other) return false;
        return Name == other.Name &&
               TableName == other.TableName &&
               Columns.SequenceEqual(other.Columns) &&
               IsUnique == other.IsUnique &&
               IsPrimary == other.IsPrimary &&
               IndexType == other.IndexType &&
               WhereClause == other.WhereClause;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, TableName, 
            Columns.Aggregate(0, (hash, col) => hash ^ col.GetHashCode()),
            IsUnique, IsPrimary, IndexType, WhereClause);
    }

    public override string ToString()
    {
        var unique = IsUnique ? "UNIQUE " : "";
        var columns = string.Join(", ", Columns);
        return $"{unique}INDEX {Name} ON {TableName} ({columns})";
    }
}