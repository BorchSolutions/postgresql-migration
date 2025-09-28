using System.Text;
using DBMigrator.Core.Models;
using DBMigrator.Core.Models.Changes;
using DBMigrator.Core.Models.Schema;
using Schema = DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Services;

public class MigrationGenerator
{
    public GeneratedMigration Generate(DatabaseChanges changes, string? migrationName = null)
    {
        var migration = new GeneratedMigration
        {
            Id = Guid.NewGuid().ToString(),
            Name = migrationName ?? GenerateMigrationName(changes),
            Description = changes.ToString()
        };

        var upScript = new StringBuilder();
        var downScript = new StringBuilder();

        upScript.AppendLine("-- Auto-generated migration");
        upScript.AppendLine($"-- Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        upScript.AppendLine($"-- Description: {migration.Description}");
        upScript.AppendLine();

        downScript.AppendLine("-- Auto-generated rollback migration");
        downScript.AppendLine($"-- Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        downScript.AppendLine();

        // Generate UP script
        GenerateNewTables(changes.NewTables, upScript, downScript, migration);
        GenerateDeletedTables(changes.DeletedTables, upScript, downScript, migration);
        GenerateModifiedTables(changes.ModifiedTables, upScript, downScript, migration);
        
        // Generate function changes
        GenerateNewFunctions(changes.NewFunctions, upScript, downScript, migration);
        GenerateDeletedFunctions(changes.DeletedFunctions, upScript, downScript, migration);
        GenerateModifiedFunctions(changes.ModifiedFunctions, upScript, downScript, migration);

        migration.UpScript = upScript.ToString();
        migration.DownScript = downScript.ToString();

        return migration;
    }

    private void GenerateNewTables(List<Table> newTables, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!newTables.Any()) return;

        upScript.AppendLine("-- Create new tables");
        downScript.AppendLine("-- Drop new tables (in reverse order)");

        foreach (var table in newTables)
        {
            // UP: CREATE TABLE
            upScript.AppendLine(GenerateCreateTableScript(table));
            upScript.AppendLine();

            // DOWN: DROP TABLE (add to beginning for correct order)
            downScript.Insert(downScript.ToString().IndexOf("-- Drop new tables") + "-- Drop new tables\n".Length,
                $"DROP TABLE IF EXISTS {table.Schema}.{table.Name};\n");

            // Generate indexes
            foreach (var index in table.Indexes.Where(i => !i.IsPrimary))
            {
                upScript.AppendLine(GenerateCreateIndexScript(index));
                downScript.Insert(downScript.ToString().IndexOf($"DROP TABLE IF EXISTS {table.Schema}.{table.Name};"),
                    $"DROP INDEX IF EXISTS {index.Name};\n");
            }
        }
    }

    private void GenerateDeletedTables(List<Table> deletedTables, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!deletedTables.Any()) return;

        upScript.AppendLine("-- Drop deleted tables");
        downScript.AppendLine("-- Recreate deleted tables");

        foreach (var table in deletedTables)
        {
            // UP: DROP TABLE
            upScript.AppendLine($"DROP TABLE IF EXISTS {table.Schema}.{table.Name};");
            upScript.AppendLine();

            // DOWN: CREATE TABLE
            downScript.AppendLine(GenerateCreateTableScript(table));
            downScript.AppendLine();

            // Add warning about data loss
            migration.Warnings.Add($"Table '{table.Name}' will be dropped - this may result in data loss!");
        }
    }

    private void GenerateModifiedTables(List<TableChanges> modifiedTables, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!modifiedTables.Any()) return;

        upScript.AppendLine("-- Modify existing tables");
        downScript.AppendLine("-- Reverse table modifications");

        foreach (var tableChange in modifiedTables)
        {
            upScript.AppendLine($"-- Changes for table: {tableChange.TableName}");
            downScript.AppendLine($"-- Reverse changes for table: {tableChange.TableName}");

            GenerateColumnChanges(tableChange, upScript, downScript, migration);
            GenerateIndexChanges(tableChange, upScript, downScript, migration);

            upScript.AppendLine();
            downScript.AppendLine();
        }
    }

    private void GenerateColumnChanges(TableChanges tableChange, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        // New columns
        foreach (var column in tableChange.NewColumns)
        {
            var nullable = column.IsNullable ? "" : " NOT NULL";
            var defaultClause = !string.IsNullOrEmpty(column.DefaultValue) ? $" DEFAULT {column.DefaultValue}" : "";
            
            upScript.AppendLine($"ALTER TABLE {tableChange.TableName} ADD COLUMN {column.Name} {column.DataType}{nullable}{defaultClause};");
            downScript.Insert(downScript.ToString().LastIndexOf($"-- Reverse changes for table: {tableChange.TableName}") + $"-- Reverse changes for table: {tableChange.TableName}\n".Length,
                $"ALTER TABLE {tableChange.TableName} DROP COLUMN IF EXISTS {column.Name};\n");
        }

        // Deleted columns
        foreach (var column in tableChange.DeletedColumns)
        {
            upScript.AppendLine($"ALTER TABLE {tableChange.TableName} DROP COLUMN IF EXISTS {column.Name};");
            
            var nullable = column.IsNullable ? "" : " NOT NULL";
            var defaultClause = !string.IsNullOrEmpty(column.DefaultValue) ? $" DEFAULT {column.DefaultValue}" : "";
            downScript.AppendLine($"ALTER TABLE {tableChange.TableName} ADD COLUMN {column.Name} {column.DataType}{nullable}{defaultClause};");

            migration.Warnings.Add($"Column '{tableChange.TableName}.{column.Name}' will be dropped - this may result in data loss!");
        }

        // Modified columns
        foreach (var columnChange in tableChange.ModifiedColumns)
        {
            var newCol = columnChange.NewColumn;
            var oldCol = columnChange.OldColumn;

            if (columnChange.Changes.Any(c => c.Type == ColumnModificationType.DataTypeChanged))
            {
                upScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {newCol.Name} TYPE {newCol.DataType};");
                downScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {oldCol.Name} TYPE {oldCol.DataType};");
            }

            if (columnChange.Changes.Any(c => c.Type == ColumnModificationType.NullabilityChanged))
            {
                var constraint = newCol.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
                var reverseConstraint = oldCol.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
                
                upScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {newCol.Name} {constraint};");
                downScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {oldCol.Name} {reverseConstraint};");
            }

            if (columnChange.Changes.Any(c => c.Type == ColumnModificationType.DefaultValueChanged))
            {
                if (!string.IsNullOrEmpty(newCol.DefaultValue))
                    upScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {newCol.Name} SET DEFAULT {newCol.DefaultValue};");
                else
                    upScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {newCol.Name} DROP DEFAULT;");

                if (!string.IsNullOrEmpty(oldCol.DefaultValue))
                    downScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {oldCol.Name} SET DEFAULT {oldCol.DefaultValue};");
                else
                    downScript.AppendLine($"ALTER TABLE {tableChange.TableName} ALTER COLUMN {oldCol.Name} DROP DEFAULT;");
            }
        }
    }

    private void GenerateIndexChanges(TableChanges tableChange, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        // New indexes
        foreach (var index in tableChange.NewIndexes)
        {
            upScript.AppendLine(GenerateCreateIndexScript(index));
            downScript.Insert(downScript.ToString().LastIndexOf($"-- Reverse changes for table: {tableChange.TableName}") + $"-- Reverse changes for table: {tableChange.TableName}\n".Length,
                $"DROP INDEX IF EXISTS {index.Name};\n");
        }

        // Deleted indexes
        foreach (var index in tableChange.DeletedIndexes)
        {
            upScript.AppendLine($"DROP INDEX IF EXISTS {index.Name};");
            downScript.AppendLine(GenerateCreateIndexScript(index));
        }
    }

    private string GenerateCreateTableScript(Table table)
    {
        var script = new StringBuilder();
        script.AppendLine($"CREATE TABLE {table.Schema}.{table.Name} (");

        var columnDefinitions = new List<string>();

        foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
        {
            var definition = new StringBuilder();
            definition.Append($"    {column.Name} {column.DataType}");

            if (!column.IsNullable)
                definition.Append(" NOT NULL");

            if (!string.IsNullOrEmpty(column.DefaultValue))
                definition.Append($" DEFAULT {column.DefaultValue}");

            columnDefinitions.Add(definition.ToString());
        }

        // Add primary key constraint
        var primaryKeyColumns = table.Columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.OrdinalPosition)
            .Select(c => c.Name)
            .ToList();

        if (primaryKeyColumns.Any())
        {
            columnDefinitions.Add($"    PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})");
        }

        script.AppendLine(string.Join(",\n", columnDefinitions));
        script.Append(");");

        return script.ToString();
    }

    private string GenerateCreateIndexScript(Schema.Index index)
    {
        var unique = index.IsUnique ? "UNIQUE " : "";
        var columns = string.Join(", ", index.Columns);
        var whereClause = !string.IsNullOrEmpty(index.WhereClause) ? $" WHERE {index.WhereClause}" : "";
        
        return $"CREATE {unique}INDEX {index.Name} ON {index.TableName} ({columns}){whereClause};";
    }

    private string GenerateMigrationName(DatabaseChanges changes)
    {
        if (!changes.HasChanges) return "no_changes";

        var parts = new List<string>();

        if (changes.NewTables.Any())
            parts.Add($"create_{string.Join("_", changes.NewTables.Take(2).Select(t => t.Name))}");

        if (changes.DeletedTables.Any())
            parts.Add($"drop_{string.Join("_", changes.DeletedTables.Take(2).Select(t => t.Name))}");

        if (changes.ModifiedTables.Any())
            parts.Add($"alter_{string.Join("_", changes.ModifiedTables.Take(2).Select(t => t.TableName))}");

        if (changes.NewFunctions.Any())
            parts.Add($"create_func_{string.Join("_", changes.NewFunctions.Take(2).Select(f => f.Name))}");

        if (changes.DeletedFunctions.Any())
            parts.Add($"drop_func_{string.Join("_", changes.DeletedFunctions.Take(2).Select(f => f.Name))}");

        if (changes.ModifiedFunctions.Any())
            parts.Add($"alter_func_{string.Join("_", changes.ModifiedFunctions.Take(2).Select(f => f.Name))}");

        var name = string.Join("_and_", parts);
        return name.Length > 50 ? name.Substring(0, 50) : name;
    }

    private void GenerateNewFunctions(List<Function> newFunctions, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!newFunctions.Any()) return;

        upScript.AppendLine("-- Create new functions");
        downScript.AppendLine("-- Drop new functions (in reverse order)");

        foreach (var function in newFunctions)
        {
            upScript.AppendLine($"-- Create function {function.GetSignature()}");
            upScript.AppendLine(function.Body);
            upScript.AppendLine();

            // For rollback, drop the function
            downScript.AppendLine($"DROP FUNCTION IF EXISTS {function.Schema}.{function.Name}({string.Join(", ", function.Parameters.Select(p => p.DataType))});");
        }

        upScript.AppendLine();
        downScript.AppendLine();
    }

    private void GenerateDeletedFunctions(List<Function> deletedFunctions, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!deletedFunctions.Any()) return;

        upScript.AppendLine("-- Drop deleted functions");
        downScript.AppendLine("-- Recreate deleted functions");

        foreach (var function in deletedFunctions)
        {
            // For up script, drop the function
            upScript.AppendLine($"DROP FUNCTION IF EXISTS {function.Schema}.{function.Name}({string.Join(", ", function.Parameters.Select(p => p.DataType))});");

            // For rollback, recreate the function
            downScript.AppendLine($"-- Recreate function {function.GetSignature()}");
            downScript.AppendLine(function.Body);
            downScript.AppendLine();

            migration.Warnings.Add($"Function {function.GetSignature()} will be dropped - this is irreversible without backup");
        }

        upScript.AppendLine();
        downScript.AppendLine();
    }

    private void GenerateModifiedFunctions(List<Function> modifiedFunctions, StringBuilder upScript, StringBuilder downScript, GeneratedMigration migration)
    {
        if (!modifiedFunctions.Any()) return;

        upScript.AppendLine("-- Modify existing functions");
        downScript.AppendLine("-- Restore original functions");

        foreach (var function in modifiedFunctions)
        {
            upScript.AppendLine($"-- Replace function {function.GetSignature()}");
            upScript.AppendLine($"DROP FUNCTION IF EXISTS {function.Schema}.{function.Name}({string.Join(", ", function.Parameters.Select(p => p.DataType))});");
            upScript.AppendLine(function.Body);
            upScript.AppendLine();

            // For rollback, we would need the original function body (not available in current function)
            downScript.AppendLine($"-- WARNING: Original function body not available for {function.GetSignature()}");
            downScript.AppendLine($"-- Manual restoration required");
            downScript.AppendLine();

            migration.Warnings.Add($"Function {function.GetSignature()} will be modified - original version should be backed up manually");
        }

        upScript.AppendLine();
        downScript.AppendLine();
    }
}