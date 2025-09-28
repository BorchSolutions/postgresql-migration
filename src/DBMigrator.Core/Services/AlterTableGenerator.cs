using DBMigrator.Core.Models.Changes;
using DBMigrator.Core.Models.Schema;
using System.Text;

namespace DBMigrator.Core.Services;

public class AlterTableGenerator
{
    public List<string> GenerateAlterTableStatements(ColumnChanges columnChanges)
    {
        var statements = new List<string>();
        
        if (!columnChanges.HasChanges)
            return statements;

        // Add new columns
        foreach (var column in columnChanges.Added)
        {
            statements.Add(GenerateAddColumnStatement(columnChanges.TableName, column));
        }

        // Modify existing columns
        foreach (var change in columnChanges.Modified)
        {
            statements.AddRange(GenerateModifyColumnStatements(columnChanges.TableName, change));
        }

        // Drop columns (done last to avoid dependency issues)
        foreach (var column in columnChanges.Removed)
        {
            statements.Add(GenerateDropColumnStatement(columnChanges.TableName, column));
        }

        return statements;
    }

    private string GenerateAddColumnStatement(string tableName, Column column)
    {
        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE {EscapeIdentifier(tableName)} ADD COLUMN {EscapeIdentifier(column.Name)} {column.DataType}");
        
        if (column.MaxLength.HasValue && RequiresLength(column.DataType))
        {
            sb.Append($"({column.MaxLength})");
        }
        else if (column.Precision.HasValue && column.Scale.HasValue)
        {
            sb.Append($"({column.Precision},{column.Scale})");
        }
        else if (column.Precision.HasValue)
        {
            sb.Append($"({column.Precision})");
        }

        if (!column.IsNullable)
        {
            if (!string.IsNullOrEmpty(column.DefaultValue))
            {
                sb.Append($" DEFAULT {column.DefaultValue}");
            }
            sb.Append(" NOT NULL");
        }
        else if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            sb.Append($" DEFAULT {column.DefaultValue}");
        }

        sb.Append(";");
        return sb.ToString();
    }

    private List<string> GenerateModifyColumnStatements(string tableName, DetailedColumnChange change)
    {
        var statements = new List<string>();
        var columnName = change.NewColumn.Name;

        foreach (var modification in change.Changes)
        {
            switch (modification.Type)
            {
                case ColumnModificationType.DataTypeChanged:
                    statements.Add(GenerateDataTypeChangeStatement(tableName, columnName, change.NewColumn));
                    break;

                case ColumnModificationType.NullabilityChanged:
                    statements.Add(GenerateNullabilityChangeStatement(tableName, columnName, change.NewColumn.IsNullable));
                    break;

                case ColumnModificationType.DefaultValueChanged:
                    statements.AddRange(GenerateDefaultValueChangeStatements(tableName, columnName, change.NewColumn.DefaultValue));
                    break;

                case ColumnModificationType.LengthChanged:
                case ColumnModificationType.PrecisionChanged:
                case ColumnModificationType.ScaleChanged:
                    // These are handled with data type change
                    if (!change.Changes.Any(c => c.Type == ColumnModificationType.DataTypeChanged))
                    {
                        statements.Add(GenerateDataTypeChangeStatement(tableName, columnName, change.NewColumn));
                    }
                    break;
            }
        }

        return statements;
    }

    private string GenerateDataTypeChangeStatement(string tableName, string columnName, Column newColumn)
    {
        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE {EscapeIdentifier(tableName)} ALTER COLUMN {EscapeIdentifier(columnName)} TYPE {newColumn.DataType}");
        
        if (newColumn.MaxLength.HasValue && RequiresLength(newColumn.DataType))
        {
            sb.Append($"({newColumn.MaxLength})");
        }
        else if (newColumn.Precision.HasValue && newColumn.Scale.HasValue)
        {
            sb.Append($"({newColumn.Precision},{newColumn.Scale})");
        }
        else if (newColumn.Precision.HasValue)
        {
            sb.Append($"({newColumn.Precision})");
        }

        // Add USING clause for potentially incompatible type changes
        if (RequiresUsingClause(newColumn.DataType))
        {
            sb.Append($" USING {EscapeIdentifier(columnName)}::{newColumn.DataType}");
        }

        sb.Append(";");
        return sb.ToString();
    }

    private string GenerateNullabilityChangeStatement(string tableName, string columnName, bool isNullable)
    {
        var nullConstraint = isNullable ? "DROP NOT NULL" : "SET NOT NULL";
        return $"ALTER TABLE {EscapeIdentifier(tableName)} ALTER COLUMN {EscapeIdentifier(columnName)} {nullConstraint};";
    }

    private List<string> GenerateDefaultValueChangeStatements(string tableName, string columnName, string? defaultValue)
    {
        var statements = new List<string>();
        
        // Drop existing default first
        statements.Add($"ALTER TABLE {EscapeIdentifier(tableName)} ALTER COLUMN {EscapeIdentifier(columnName)} DROP DEFAULT;");
        
        // Set new default if provided
        if (!string.IsNullOrEmpty(defaultValue))
        {
            statements.Add($"ALTER TABLE {EscapeIdentifier(tableName)} ALTER COLUMN {EscapeIdentifier(columnName)} SET DEFAULT {defaultValue};");
        }

        return statements;
    }

    private string GenerateDropColumnStatement(string tableName, Column column)
    {
        return $"ALTER TABLE {EscapeIdentifier(tableName)} DROP COLUMN {EscapeIdentifier(column.Name)};";
    }

    public List<string> GenerateRollbackStatements(ColumnChanges columnChanges)
    {
        var statements = new List<string>();
        
        if (!columnChanges.HasChanges)
            return statements;

        // Rollback in reverse order
        
        // Re-add dropped columns
        foreach (var column in columnChanges.Removed)
        {
            statements.Add(GenerateAddColumnStatement(columnChanges.TableName, column));
        }

        // Revert modified columns
        foreach (var change in columnChanges.Modified)
        {
            statements.AddRange(GenerateRevertColumnStatements(columnChanges.TableName, change));
        }

        // Drop added columns
        foreach (var column in columnChanges.Added)
        {
            statements.Add(GenerateDropColumnStatement(columnChanges.TableName, column));
        }

        return statements;
    }

    private List<string> GenerateRevertColumnStatements(string tableName, DetailedColumnChange change)
    {
        var statements = new List<string>();
        var columnName = change.OldColumn.Name;

        // Revert in reverse order of changes
        foreach (var modification in change.Changes.AsEnumerable().Reverse())
        {
            switch (modification.Type)
            {
                case ColumnModificationType.DefaultValueChanged:
                    statements.AddRange(GenerateDefaultValueChangeStatements(tableName, columnName, change.OldColumn.DefaultValue));
                    break;

                case ColumnModificationType.NullabilityChanged:
                    statements.Add(GenerateNullabilityChangeStatement(tableName, columnName, change.OldColumn.IsNullable));
                    break;

                case ColumnModificationType.DataTypeChanged:
                case ColumnModificationType.LengthChanged:
                case ColumnModificationType.PrecisionChanged:
                case ColumnModificationType.ScaleChanged:
                    statements.Add(GenerateDataTypeChangeStatement(tableName, columnName, change.OldColumn));
                    break;
            }
        }

        return statements;
    }

    public string GeneratePreMigrationValidation(ColumnChanges columnChanges)
    {
        var validations = new List<string>();

        foreach (var change in columnChanges.Modified)
        {
            if (change.HasDestructiveChanges)
            {
                foreach (var modification in change.Changes.Where(c => c.IsDestructive))
                {
                    switch (modification.Type)
                    {
                        case ColumnModificationType.NullabilityChanged when !change.NewColumn.IsNullable:
                            validations.Add($"-- Validate no NULL values in {change.NewColumn.Name}");
                            validations.Add($"DO $$");
                            validations.Add($"BEGIN");
                            validations.Add($"    IF EXISTS (SELECT 1 FROM {EscapeIdentifier(columnChanges.TableName)} WHERE {EscapeIdentifier(change.NewColumn.Name)} IS NULL) THEN");
                            validations.Add($"        RAISE EXCEPTION 'Cannot set NOT NULL constraint: column {change.NewColumn.Name} contains NULL values';");
                            validations.Add($"    END IF;");
                            validations.Add($"END $$;");
                            break;

                        case ColumnModificationType.LengthChanged when change.NewColumn.MaxLength < change.OldColumn.MaxLength:
                            validations.Add($"-- Validate data fits in new length for {change.NewColumn.Name}");
                            validations.Add($"DO $$");
                            validations.Add($"BEGIN");
                            validations.Add($"    IF EXISTS (SELECT 1 FROM {EscapeIdentifier(columnChanges.TableName)} WHERE LENGTH({EscapeIdentifier(change.NewColumn.Name)}) > {change.NewColumn.MaxLength}) THEN");
                            validations.Add($"        RAISE EXCEPTION 'Cannot reduce column length: {change.NewColumn.Name} contains values longer than {change.NewColumn.MaxLength}';");
                            validations.Add($"    END IF;");
                            validations.Add($"END $$;");
                            break;
                    }
                }
            }
        }

        return string.Join("\n", validations);
    }

    private bool RequiresLength(string dataType)
    {
        var typesRequiringLength = new[]
        {
            "varchar", "character varying", "char", "character", "bit", "bit varying"
        };
        
        return typesRequiringLength.Any(t => dataType.ToLowerInvariant().StartsWith(t));
    }

    private bool RequiresUsingClause(string newDataType)
    {
        // Types that commonly require USING clause for conversion
        var typesRequiringUsing = new[]
        {
            "integer", "bigint", "smallint", "numeric", "decimal", "real", "double precision",
            "timestamp", "timestamptz", "date", "time", "timetz", "boolean", "uuid"
        };
        
        return typesRequiringUsing.Any(t => newDataType.ToLowerInvariant().StartsWith(t));
    }

    private string EscapeIdentifier(string identifier)
    {
        // Escape PostgreSQL identifiers by wrapping in double quotes
        return $"\"{identifier}\"";
    }
}