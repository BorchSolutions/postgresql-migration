using DBMigrator.Core.Models.Changes;
using DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Services;

public class ColumnChangeDetector
{
    public ColumnChanges DetectColumnChanges(Table oldTable, Table newTable)
    {
        var changes = new ColumnChanges
        {
            TableName = newTable.Name
        };

        var oldColumns = oldTable.Columns.ToDictionary(c => c.Name, c => c);
        var newColumns = newTable.Columns.ToDictionary(c => c.Name, c => c);

        // New columns
        foreach (var newColumn in newTable.Columns)
        {
            if (!oldColumns.ContainsKey(newColumn.Name))
            {
                changes.Added.Add(newColumn);
            }
        }

        // Removed columns
        foreach (var oldColumn in oldTable.Columns)
        {
            if (!newColumns.ContainsKey(oldColumn.Name))
            {
                changes.Removed.Add(oldColumn);
            }
        }

        // Modified columns
        foreach (var newColumn in newTable.Columns)
        {
            if (oldColumns.TryGetValue(newColumn.Name, out var oldColumn))
            {
                var modifications = DetectColumnModifications(oldColumn, newColumn);
                if (modifications.Any())
                {
                    changes.Modified.Add(new DetailedColumnChange
                    {
                        OldColumn = oldColumn,
                        NewColumn = newColumn,
                        Changes = modifications
                    });
                }
            }
        }

        return changes;
    }

    public List<ColumnModification> DetectColumnModifications(Column oldColumn, Column newColumn)
    {
        var modifications = new List<ColumnModification>();

        // Data type changes
        if (oldColumn.DataType != newColumn.DataType)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.DataTypeChanged,
                OldValue = oldColumn.DataType,
                NewValue = newColumn.DataType,
                Description = $"Data type changed from {oldColumn.DataType} to {newColumn.DataType}",
                IsDestructive = IsDataTypeChangeDestructive(oldColumn.DataType, newColumn.DataType)
            });
        }

        // Nullable changes
        if (oldColumn.IsNullable != newColumn.IsNullable)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.NullabilityChanged,
                OldValue = oldColumn.IsNullable.ToString(),
                NewValue = newColumn.IsNullable.ToString(),
                Description = $"Nullability changed from {(oldColumn.IsNullable ? "NULL" : "NOT NULL")} to {(newColumn.IsNullable ? "NULL" : "NOT NULL")}",
                IsDestructive = !oldColumn.IsNullable && newColumn.IsNullable == false // Making column NOT NULL can fail
            });
        }

        // Default value changes
        if (oldColumn.DefaultValue != newColumn.DefaultValue)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.DefaultValueChanged,
                OldValue = oldColumn.DefaultValue ?? "NULL",
                NewValue = newColumn.DefaultValue ?? "NULL",
                Description = $"Default value changed from '{oldColumn.DefaultValue ?? "NULL"}' to '{newColumn.DefaultValue ?? "NULL"}'",
                IsDestructive = false
            });
        }

        // Length changes
        if (oldColumn.MaxLength != newColumn.MaxLength)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.LengthChanged,
                OldValue = oldColumn.MaxLength?.ToString() ?? "unlimited",
                NewValue = newColumn.MaxLength?.ToString() ?? "unlimited",
                Description = $"Max length changed from {oldColumn.MaxLength?.ToString() ?? "unlimited"} to {newColumn.MaxLength?.ToString() ?? "unlimited"}",
                IsDestructive = newColumn.MaxLength < oldColumn.MaxLength
            });
        }

        // Precision changes (for numeric types)
        if (oldColumn.Precision != newColumn.Precision)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.PrecisionChanged,
                OldValue = oldColumn.Precision?.ToString() ?? "default",
                NewValue = newColumn.Precision?.ToString() ?? "default",
                Description = $"Precision changed from {oldColumn.Precision?.ToString() ?? "default"} to {newColumn.Precision?.ToString() ?? "default"}",
                IsDestructive = newColumn.Precision < oldColumn.Precision
            });
        }

        // Scale changes (for numeric types)
        if (oldColumn.Scale != newColumn.Scale)
        {
            modifications.Add(new ColumnModification
            {
                Type = ColumnModificationType.ScaleChanged,
                OldValue = oldColumn.Scale?.ToString() ?? "default",
                NewValue = newColumn.Scale?.ToString() ?? "default",
                Description = $"Scale changed from {oldColumn.Scale?.ToString() ?? "default"} to {newColumn.Scale?.ToString() ?? "default"}",
                IsDestructive = newColumn.Scale < oldColumn.Scale
            });
        }

        return modifications;
    }

    private bool IsDataTypeChangeDestructive(string oldType, string newType)
    {
        // Define compatibility matrix for PostgreSQL types
        var compatibleChanges = new Dictionary<string, List<string>>
        {
            ["varchar"] = new List<string> { "text" },
            ["character varying"] = new List<string> { "text" },
            ["int4"] = new List<string> { "int8", "bigint" },
            ["integer"] = new List<string> { "bigint" },
            ["smallint"] = new List<string> { "integer", "bigint" },
            ["real"] = new List<string> { "double precision" },
            ["float4"] = new List<string> { "float8" }
        };

        // Normalize type names
        oldType = oldType.ToLowerInvariant();
        newType = newType.ToLowerInvariant();

        // Same type is not destructive
        if (oldType == newType) return false;

        // Check if change is in compatible list
        if (compatibleChanges.TryGetValue(oldType, out var compatibleTypes))
        {
            return !compatibleTypes.Contains(newType);
        }

        // By default, assume type changes are potentially destructive
        return true;
    }
}

public class ColumnChanges
{
    public string TableName { get; set; } = string.Empty;
    public List<Column> Added { get; set; } = new();
    public List<Column> Removed { get; set; } = new();
    public List<DetailedColumnChange> Modified { get; set; } = new();

    public bool HasChanges => Added.Any() || Removed.Any() || Modified.Any();
    public int TotalChanges => Added.Count + Removed.Count + Modified.Count;
}

