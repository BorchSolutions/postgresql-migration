using DBMigrator.Core.Database;
using DBMigrator.Core.Models.Schema;
using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class CreateCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, bool autoDetect, string? migrationName = null)
    {
        try
        {
            var connectionManager = new ConnectionManager(connectionString);
            var configurationManager = new ConfigurationManager();
            var schemaAnalyzer = new SchemaAnalyzer(connectionManager, configurationManager);
            var changeDetector = new ChangeDetector();
            var migrationGenerator = new MigrationGenerator();

            if (autoDetect)
            {
                Console.WriteLine("🔍 Auto-detecting changes...");
                
                // Load baseline schema
                var baseline = await schemaAnalyzer.LoadBaselineAsync(migrationsPath);
                if (baseline == null)
                {
                    Console.WriteLine("❌ No baseline found. Run 'dbmigrator baseline create' first.");
                    return 1;
                }

                // Get current schema
                var currentSchema = await schemaAnalyzer.GetCurrentSchemaAsync();
                
                // Detect changes
                var changes = changeDetector.DetectChanges(baseline, currentSchema);
                
                if (!changes.HasChanges)
                {
                    Console.WriteLine("✅ No changes detected.");
                    return 0;
                }

                Console.WriteLine($"📊 Changes detected: {changes}");

                // Generate migration
                var migration = migrationGenerator.Generate(changes, migrationName);
                
                if (migration.Warnings.Any())
                {
                    Console.WriteLine("⚠️  Warnings:");
                    foreach (var warning in migration.Warnings)
                    {
                        Console.WriteLine($"   - {warning}");
                    }
                    Console.WriteLine();
                }

                // Save migration files
                Directory.CreateDirectory(migrationsPath);
                
                var upFilePath = Path.Combine(migrationsPath, migration.UpFilename);
                var downFilePath = Path.Combine(migrationsPath, migration.DownFilename);
                
                await File.WriteAllTextAsync(upFilePath, migration.UpScript);
                await File.WriteAllTextAsync(downFilePath, migration.DownScript);

                Console.WriteLine($"✅ Migration created:");
                Console.WriteLine($"   UP:   {upFilePath}");
                Console.WriteLine($"   DOWN: {downFilePath}");

                return 0;
            }
            else
            {
                // Manual migration creation
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var safeName = (migrationName ?? "manual_migration").Replace(" ", "_").ToLowerInvariant();
                var filename = $"{timestamp}_manual_{safeName}.sql";
                var filePath = Path.Combine(migrationsPath, filename);

                Directory.CreateDirectory(migrationsPath);

                var template = $@"-- Manual migration: {migrationName ?? "Manual Migration"}
-- Created: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
-- 
-- Add your SQL statements here:

-- Example:
-- CREATE TABLE example (
--     id SERIAL PRIMARY KEY,
--     name VARCHAR(100) NOT NULL
-- );
";

                await File.WriteAllTextAsync(filePath, template);
                
                Console.WriteLine($"✅ Manual migration template created: {filePath}");
                Console.WriteLine("📝 Edit the file and add your SQL statements, then apply with:");
                Console.WriteLine($"   dbmigrator apply {filename}");

                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error creating migration: {ex.Message}");
            return 1;
        }
    }
}