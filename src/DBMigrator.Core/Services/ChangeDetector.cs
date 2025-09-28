using DBMigrator.Core.Models.Changes;
using DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Services;

public class ChangeDetector
{
    public DatabaseChanges DetectChanges(DatabaseSchema baseline, DatabaseSchema current)
    {
        var changes = new DatabaseChanges();

        // Detect table changes
        DetectTableChanges(baseline, current, changes);
        
        // Detect function changes
        DetectFunctionChanges(baseline, current, changes);

        return changes;
    }

    private void DetectTableChanges(DatabaseSchema baseline, DatabaseSchema current, DatabaseChanges changes)
    {
        var baselineTables = baseline.Tables.ToDictionary(t => t.Name, t => t);
        var currentTables = current.Tables.ToDictionary(t => t.Name, t => t);

        // New tables
        foreach (var table in current.Tables)
        {
            if (!baselineTables.ContainsKey(table.Name))
            {
                changes.NewTables.Add(table);
            }
        }

        // Deleted tables
        foreach (var table in baseline.Tables)
        {
            if (!currentTables.ContainsKey(table.Name))
            {
                changes.DeletedTables.Add(table);
            }
        }

        // Modified tables
        foreach (var currentTable in current.Tables)
        {
            if (baselineTables.TryGetValue(currentTable.Name, out var baselineTable))
            {
                var tableChanges = DetectTableChanges(baselineTable, currentTable);
                if (tableChanges.HasChanges)
                {
                    changes.ModifiedTables.Add(tableChanges);
                }
            }
        }
    }

    private TableChanges DetectTableChanges(Table baseline, Table current)
    {
        var changes = new TableChanges { TableName = current.Name };

        DetectColumnChanges(baseline, current, changes);
        DetectIndexChanges(baseline, current, changes);

        return changes;
    }

    private void DetectColumnChanges(Table baseline, Table current, TableChanges changes)
    {
        var baselineColumns = baseline.Columns.ToDictionary(c => c.Name, c => c);
        var currentColumns = current.Columns.ToDictionary(c => c.Name, c => c);

        // New columns
        foreach (var column in current.Columns)
        {
            if (!baselineColumns.ContainsKey(column.Name))
            {
                changes.NewColumns.Add(column);
            }
        }

        // Deleted columns
        foreach (var column in baseline.Columns)
        {
            if (!currentColumns.ContainsKey(column.Name))
            {
                changes.DeletedColumns.Add(column);
            }
        }

        // Modified columns
        foreach (var currentColumn in current.Columns)
        {
            if (baselineColumns.TryGetValue(currentColumn.Name, out var baselineColumn))
            {
                var columnChanges = DetectColumnChanges(baselineColumn, currentColumn);
                if (columnChanges.Changes.Any())
                {
                    changes.ModifiedColumns.Add(columnChanges);
                }
            }
        }
    }

    private DetailedColumnChange DetectColumnChanges(Column baseline, Column current)
    {
        var change = new DetailedColumnChange 
        { 
            OldColumn = baseline, 
            NewColumn = current 
        };

        if (baseline.DataType != current.DataType)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.DataTypeChanged,
                OldValue = baseline.DataType,
                NewValue = current.DataType,
                Description = $"Type changed from {baseline.DataType} to {current.DataType}",
                IsDestructive = true
            });

        if (baseline.IsNullable != current.IsNullable)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.NullabilityChanged,
                OldValue = baseline.IsNullable.ToString(),
                NewValue = current.IsNullable.ToString(),
                Description = $"Nullable changed from {baseline.IsNullable} to {current.IsNullable}",
                IsDestructive = !baseline.IsNullable && current.IsNullable == false
            });

        if (baseline.DefaultValue != current.DefaultValue)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.DefaultValueChanged,
                OldValue = baseline.DefaultValue ?? "NULL",
                NewValue = current.DefaultValue ?? "NULL",
                Description = $"Default changed from '{baseline.DefaultValue}' to '{current.DefaultValue}'",
                IsDestructive = false
            });

        if (baseline.MaxLength != current.MaxLength)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.LengthChanged,
                OldValue = baseline.MaxLength?.ToString() ?? "unlimited",
                NewValue = current.MaxLength?.ToString() ?? "unlimited",
                Description = $"Max length changed from {baseline.MaxLength} to {current.MaxLength}",
                IsDestructive = current.MaxLength < baseline.MaxLength
            });

        if (baseline.Precision != current.Precision)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.PrecisionChanged,
                OldValue = baseline.Precision?.ToString() ?? "default",
                NewValue = current.Precision?.ToString() ?? "default",
                Description = $"Precision changed from {baseline.Precision} to {current.Precision}",
                IsDestructive = current.Precision < baseline.Precision
            });

        if (baseline.Scale != current.Scale)
            change.Changes.Add(new ColumnModification
            {
                Type = ColumnModificationType.ScaleChanged,
                OldValue = baseline.Scale?.ToString() ?? "default",
                NewValue = current.Scale?.ToString() ?? "default",
                Description = $"Scale changed from {baseline.Scale} to {current.Scale}",
                IsDestructive = current.Scale < baseline.Scale
            });

        return change;
    }

    private void DetectIndexChanges(Table baseline, Table current, TableChanges changes)
    {
        var baselineIndexes = baseline.Indexes.ToDictionary(i => i.Name, i => i);
        var currentIndexes = current.Indexes.ToDictionary(i => i.Name, i => i);

        // New indexes
        foreach (var index in current.Indexes)
        {
            if (!baselineIndexes.ContainsKey(index.Name))
            {
                changes.NewIndexes.Add(index);
            }
        }

        // Deleted indexes
        foreach (var index in baseline.Indexes)
        {
            if (!currentIndexes.ContainsKey(index.Name))
            {
                changes.DeletedIndexes.Add(index);
            }
        }
    }

    private void DetectFunctionChanges(DatabaseSchema baseline, DatabaseSchema current, DatabaseChanges changes)
    {
        var baselineFunctions = baseline.Functions.ToDictionary(f => f.GetSignature(), f => f);
        var currentFunctions = current.Functions.ToDictionary(f => f.GetSignature(), f => f);

        // New functions
        foreach (var function in current.Functions)
        {
            if (!baselineFunctions.ContainsKey(function.GetSignature()))
            {
                changes.NewFunctions.Add(function);
            }
        }

        // Deleted functions
        foreach (var function in baseline.Functions)
        {
            if (!currentFunctions.ContainsKey(function.GetSignature()))
            {
                changes.DeletedFunctions.Add(function);
            }
        }

        // Modified functions (check if body, return type, or other properties changed)
        foreach (var currentFunction in current.Functions)
        {
            if (baselineFunctions.TryGetValue(currentFunction.GetSignature(), out var baselineFunction))
            {
                // Compare function body and properties
                if (!FunctionsAreEqual(baselineFunction, currentFunction))
                {
                    changes.ModifiedFunctions.Add(currentFunction);
                }
            }
        }
    }

    private bool FunctionsAreEqual(Function baseline, Function current)
    {
        // Compare key properties that would indicate a function change
        return baseline.Body == current.Body &&
               baseline.ReturnType == current.ReturnType &&
               baseline.Language == current.Language &&
               baseline.IsVolatile == current.IsVolatile &&
               baseline.IsSecurityDefiner == current.IsSecurityDefiner &&
               ParametersAreEqual(baseline.Parameters, current.Parameters);
    }

    private bool ParametersAreEqual(List<FunctionParameter> baseline, List<FunctionParameter> current)
    {
        if (baseline.Count != current.Count)
            return false;

        for (int i = 0; i < baseline.Count; i++)
        {
            if (!baseline[i].Equals(current[i]))
                return false;
        }

        return true;
    }
}