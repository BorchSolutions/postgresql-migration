namespace DBMigrator.Core.Models.Schema;

public class Column
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string Comment { get; set; } = string.Empty;
    public int OrdinalPosition { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Column other) return false;
        return Name == other.Name &&
               DataType == other.DataType &&
               IsNullable == other.IsNullable &&
               DefaultValue == other.DefaultValue &&
               MaxLength == other.MaxLength &&
               Precision == other.Precision &&
               Scale == other.Scale &&
               IsPrimaryKey == other.IsPrimaryKey &&
               IsIdentity == other.IsIdentity;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, DataType, IsNullable, DefaultValue, 
            MaxLength, Precision, Scale, IsPrimaryKey);
    }

    public override string ToString()
    {
        var nullable = IsNullable ? "NULL" : "NOT NULL";
        var defaultPart = !string.IsNullOrEmpty(DefaultValue) ? $" DEFAULT {DefaultValue}" : "";
        return $"{Name} {DataType} {nullable}{defaultPart}";
    }
}