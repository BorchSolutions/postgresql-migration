namespace DBMigrator.Core.Models.Schema;

public class Function
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string ReturnType { get; set; } = string.Empty;
    public List<FunctionParameter> Parameters { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public string Language { get; set; } = "plpgsql";
    public bool IsVolatile { get; set; } = true;
    public bool IsSecurityDefiner { get; set; } = false;
    public string? Owner { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public string GetSignature()
    {
        var paramTypes = string.Join(", ", Parameters.Select(p => p.DataType));
        return $"{Schema}.{Name}({paramTypes})";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Function other) return false;
        return Name == other.Name &&
               Schema == other.Schema &&
               ReturnType == other.ReturnType &&
               Parameters.SequenceEqual(other.Parameters) &&
               Body == other.Body &&
               Language == other.Language &&
               IsVolatile == other.IsVolatile &&
               IsSecurityDefiner == other.IsSecurityDefiner;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Schema, ReturnType, Body, Language, IsVolatile, IsSecurityDefiner);
    }

    public override string ToString()
    {
        return GetSignature();
    }
}

public class FunctionParameter
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Mode { get; set; } = "IN"; // IN, OUT, INOUT
    public string? DefaultValue { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not FunctionParameter other) return false;
        return Name == other.Name &&
               DataType == other.DataType &&
               Mode == other.Mode &&
               DefaultValue == other.DefaultValue;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, DataType, Mode, DefaultValue);
    }

    public override string ToString()
    {
        var param = $"{Name} {DataType}";
        if (!string.IsNullOrEmpty(DefaultValue))
            param += $" DEFAULT {DefaultValue}";
        return param;
    }
}