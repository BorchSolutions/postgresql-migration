namespace DBMigrator.Core.Models.Schema;

public class Table
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public List<Column> Columns { get; set; } = new();
    public List<Index> Indexes { get; set; } = new();
    public string Comment { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is not Table other) return false;
        return Name == other.Name && 
               Schema == other.Schema && 
               Columns.SequenceEqual(other.Columns);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Schema, 
            Columns.Aggregate(0, (hash, col) => hash ^ col.GetHashCode()));
    }

    public override string ToString()
    {
        return $"{Schema}.{Name}";
    }
}