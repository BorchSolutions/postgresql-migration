using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class DownCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, int count = 1)
    {
        try
        {
            var service = new MigrationService(connectionString);
            
            Console.WriteLine($"🔄 Rolling back {count} migration(s)...");

            // Get applied migrations in reverse order
            var appliedMigrations = await GetAppliedMigrationsAsync(service);
            
            if (!appliedMigrations.Any())
            {
                Console.WriteLine("ℹ️  No migrations to roll back.");
                return 0;
            }

            var migrationsToRollback = appliedMigrations.Take(count).ToList();
            
            Console.WriteLine($"📋 Will roll back {migrationsToRollback.Count} migration(s):");
            foreach (var migration in migrationsToRollback)
            {
                Console.WriteLine($"   - {migration.MigrationId}");
            }
            
            Console.WriteLine();
            Console.Write("❓ Continue with rollback? (y/N): ");
            var response = Console.ReadLine()?.ToLowerInvariant();
            
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("❌ Rollback cancelled.");
                return 1;
            }

            var rollbackCount = 0;
            foreach (var migration in migrationsToRollback)
            {
                var downFile = FindDownMigrationFile(migrationsPath, migration.MigrationId);
                
                if (downFile == null)
                {
                    Console.WriteLine($"⚠️  Down migration file not found for {migration.MigrationId}");
                    Console.WriteLine($"    Looked for files matching: *{migration.MigrationId}*.down.sql");
                    continue;
                }

                Console.WriteLine($"🔄 Rolling back: {migration.MigrationId}");
                
                try
                {
                    await ExecuteDownMigrationAsync(service, downFile, migration.MigrationId);
                    rollbackCount++;
                    Console.WriteLine($"✅ Rolled back: {migration.MigrationId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to roll back {migration.MigrationId}: {ex.Message}");
                    break;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"✅ Successfully rolled back {rollbackCount} migration(s)");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during rollback: {ex.Message}");
            return 1;
        }
    }

    private static Task<List<AppliedMigration>> GetAppliedMigrationsAsync(MigrationService service)
    {
        // This would need to be implemented in MigrationService
        // For now, we'll return an empty list as a placeholder
        return Task.FromResult(new List<AppliedMigration>());
    }

    private static string? FindDownMigrationFile(string migrationsPath, string migrationId)
    {
        if (!Directory.Exists(migrationsPath))
        {
            return null;
        }

        // Look for various down file patterns
        var patterns = new[]
        {
            $"*{migrationId}*.down.sql",
            $"{migrationId}.down.sql",
            $"*{migrationId.Split('_').FirstOrDefault()}*.down.sql"
        };

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(migrationsPath, pattern);
            if (files.Any())
            {
                return files.First();
            }
        }

        return null;
    }

    private static async Task ExecuteDownMigrationAsync(MigrationService service, string downFile, string migrationId)
    {
        if (!File.Exists(downFile))
        {
            throw new FileNotFoundException($"Down migration file not found: {downFile}");
        }

        var downScript = await File.ReadAllTextAsync(downFile);
        
        // This would need to be implemented in MigrationService
        // For now, we'll use the existing ApplyMigrationAsync method
        // In a real implementation, we'd need a specific RollbackMigrationAsync method
        await service.ApplyMigrationAsync(downFile);
        
        // TODO: Remove the migration record from __migrations table
        // This would be done in a proper RollbackMigrationAsync method
    }

    private class AppliedMigration
    {
        public string MigrationId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; }
    }
}