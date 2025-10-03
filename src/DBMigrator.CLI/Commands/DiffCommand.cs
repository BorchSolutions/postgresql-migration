using DBMigrator.Core.Database;
using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class DiffCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath)
    {
        try
        {
            var connectionManager = new ConnectionManager(connectionString);
            var configurationManager = new ConfigurationManager();
            var schemaAnalyzer = new SchemaAnalyzer(connectionManager, configurationManager);
            var changeDetector = new ChangeDetector();

            Console.WriteLine("🔍 Analyzing differences...");

            // Load baseline
            var baseline = await schemaAnalyzer.LoadBaselineAsync(migrationsPath);
            if (baseline == null)
            {
                Console.WriteLine("❌ No baseline found. Create one with 'dbmigrator baseline create'");
                return 1;
            }

            // Get current schema
            var currentSchema = await schemaAnalyzer.GetCurrentSchemaAsync();

            // Detect changes
            var changes = changeDetector.DetectChanges(baseline, currentSchema);

            Console.WriteLine("📊 Schema Comparison Report");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"Baseline: {baseline.CapturedAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Current:  {currentSchema.CapturedAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine();

            if (!changes.HasChanges)
            {
                Console.WriteLine("✅ No differences found - schemas are identical");
                return 0;
            }

            Console.WriteLine($"📈 Summary: {changes}");
            Console.WriteLine();

            // Show new tables
            if (changes.NewTables.Any())
            {
                Console.WriteLine("🆕 New Tables:");
                foreach (var table in changes.NewTables)
                {
                    Console.WriteLine($"   + {table.Name} ({table.Columns.Count} columns)");
                    if (table.Columns.Any())
                    {
                        foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
                        {
                            var pk = column.IsPrimaryKey ? " [PK]" : "";
                            var nullable = column.IsNullable ? "NULL" : "NOT NULL";
                            Console.WriteLine($"     - {column.Name}: {column.DataType} {nullable}{pk}");
                        }
                    }
                    Console.WriteLine();
                }
            }

            // Show deleted tables
            if (changes.DeletedTables.Any())
            {
                Console.WriteLine("🗑️  Deleted Tables:");
                foreach (var table in changes.DeletedTables)
                {
                    Console.WriteLine($"   - {table.Name} ({table.Columns.Count} columns)");
                }
                Console.WriteLine();
            }

            // Show modified tables
            if (changes.ModifiedTables.Any())
            {
                Console.WriteLine("🔄 Modified Tables:");
                foreach (var tableChange in changes.ModifiedTables)
                {
                    Console.WriteLine($"   📝 {tableChange.TableName}:");

                    if (tableChange.NewColumns.Any())
                    {
                        Console.WriteLine("     New columns:");
                        foreach (var column in tableChange.NewColumns)
                        {
                            var nullable = column.IsNullable ? "NULL" : "NOT NULL";
                            Console.WriteLine($"       + {column.Name}: {column.DataType} {nullable}");
                        }
                    }

                    if (tableChange.DeletedColumns.Any())
                    {
                        Console.WriteLine("     Deleted columns:");
                        foreach (var column in tableChange.DeletedColumns)
                        {
                            Console.WriteLine($"       - {column.Name}");
                        }
                    }

                    if (tableChange.ModifiedColumns.Any())
                    {
                        Console.WriteLine("     Modified columns:");
                        foreach (var columnChange in tableChange.ModifiedColumns)
                        {
                            Console.WriteLine($"       ~ {columnChange.NewColumn.Name}:");
                            foreach (var change in columnChange.Changes)
                            {
                                Console.WriteLine($"         • {change}");
                            }
                        }
                    }

                    if (tableChange.NewIndexes.Any())
                    {
                        Console.WriteLine("     New indexes:");
                        foreach (var index in tableChange.NewIndexes)
                        {
                            var unique = index.IsUnique ? "UNIQUE " : "";
                            Console.WriteLine($"       + {unique}{index.Name} ({string.Join(", ", index.Columns)})");
                        }
                    }

                    if (tableChange.DeletedIndexes.Any())
                    {
                        Console.WriteLine("     Deleted indexes:");
                        foreach (var index in tableChange.DeletedIndexes)
                        {
                            Console.WriteLine($"       - {index.Name}");
                        }
                    }

                    Console.WriteLine();
                }
            }

            Console.WriteLine($"💡 To generate migration: dbmigrator create --auto");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error analyzing differences: {ex.Message}");
            return 1;
        }
    }
}