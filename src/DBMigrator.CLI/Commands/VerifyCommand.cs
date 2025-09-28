using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public static class VerifyCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, string[] args)
    {
        try
        {
            Console.WriteLine("🔍 Verifying migration integrity...");
            Console.WriteLine();

            var logger = new StructuredLogger("Info", true);
            var checksumManager = new ChecksumManager(connectionString, logger);
            
            var action = args.Length > 0 ? args[0] : "checksums";
            
            return action.ToLower() switch
            {
                "checksums" => await VerifyChecksumsAsync(checksumManager, migrationsPath),
                "applied" => await VerifyAppliedMigrationsAsync(connectionString, migrationsPath),
                "all" => await VerifyAllAsync(checksumManager, connectionString, migrationsPath),
                _ => ShowVerifyHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Verification failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> VerifyChecksumsAsync(ChecksumManager checksumManager, string migrationsPath)
    {
        Console.WriteLine("🔐 Verifying migration checksums...");
        
        try
        {
            var mismatches = await checksumManager.VerifyAllChecksumsAsync(migrationsPath);
            
            if (!mismatches.Any())
            {
                Console.WriteLine("✅ All migration checksums are valid");
                return 0;
            }

            Console.WriteLine($"❌ Found {mismatches.Count} checksum mismatch(es):");
            Console.WriteLine();

            foreach (var mismatch in mismatches)
            {
                Console.WriteLine($"   📄 {mismatch.MigrationId}");
                Console.WriteLine($"      File: {mismatch.FilePath}");
                Console.WriteLine($"      Stored checksum:  {(mismatch.StoredChecksum.Length >= 8 ? mismatch.StoredChecksum[..8] + "..." : mismatch.StoredChecksum)}");
                Console.WriteLine($"      Current checksum: {(mismatch.CurrentChecksum.Length >= 8 ? mismatch.CurrentChecksum[..8] + "..." : mismatch.CurrentChecksum)}");
                Console.WriteLine($"      Detected at: {mismatch.DetectedAt:yyyy-MM-dd HH:mm:ss}");
                
                if (!string.IsNullOrEmpty(mismatch.Error))
                {
                    Console.WriteLine($"      Error: {mismatch.Error}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("💡 Next steps:");
            Console.WriteLine("   1. Review the changes to the affected migration files");
            Console.WriteLine("   2. If changes are intentional, use 'dbmigrator repair checksums --force'");
            Console.WriteLine("   3. If changes are accidental, restore the original files");
            Console.WriteLine("   4. For specific migrations, use 'dbmigrator repair checksums --migration-id <id>'");

            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Checksum verification failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> VerifyAppliedMigrationsAsync(string connectionString, string migrationsPath)
    {
        Console.WriteLine("📋 Verifying applied migrations against files...");
        
        try
        {
            using var connection = new Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // Get applied migrations from database
            var appliedMigrationsSql = @"
                SELECT migration_id, applied_at, checksum 
                FROM __dbmigrator_schema_migrations 
                ORDER BY applied_at";

            var appliedMigrations = new List<(string migrationId, DateTime appliedAt, string? checksum)>();
            
            try
            {
                using var command = new Npgsql.NpgsqlCommand(appliedMigrationsSql, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    appliedMigrations.Add((
                        reader.GetString(reader.GetOrdinal("migration_id")),
                        reader.GetDateTime(reader.GetOrdinal("applied_at")),
                        reader.IsDBNull(reader.GetOrdinal("checksum")) ? null : reader.GetString(reader.GetOrdinal("checksum"))
                    ));
                }
            }
            catch (Npgsql.PostgresException)
            {
                Console.WriteLine("⚠️ Migration history table not found. Run 'dbmigrator init' first.");
                return 1;
            }

            // Get migration files
            var migrationFiles = Directory.Exists(migrationsPath) 
                ? Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToHashSet()
                : new HashSet<string>();

            Console.WriteLine($"📊 Applied migrations in database: {appliedMigrations.Count}");
            Console.WriteLine($"📁 Migration files on disk: {migrationFiles.Count}");
            Console.WriteLine();

            // Check for orphaned database entries (applied but no file)
            var orphanedMigrations = appliedMigrations
                .Where(am => !migrationFiles.Contains(am.migrationId))
                .ToList();

            if (orphanedMigrations.Any())
            {
                Console.WriteLine("⚠️ Orphaned migrations (applied but file missing):");
                foreach (var (migrationId, appliedAt, _) in orphanedMigrations)
                {
                    Console.WriteLine($"   📄 {migrationId}");
                    Console.WriteLine($"      Applied: {appliedAt:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"      Status: FILE MISSING");
                    Console.WriteLine();
                }
            }

            // Check for pending migrations (file exists but not applied)
            var appliedIds = appliedMigrations.Select(am => am.migrationId).ToHashSet();
            var pendingMigrations = migrationFiles.Where(mf => !appliedIds.Contains(mf)).ToList();

            if (pendingMigrations.Any())
            {
                Console.WriteLine("⏳ Pending migrations (file exists but not applied):");
                foreach (var migrationId in pendingMigrations.OrderBy(m => m))
                {
                    Console.WriteLine($"   📄 {migrationId}");
                    Console.WriteLine($"      Status: PENDING");
                    Console.WriteLine();
                }
            }

            if (!orphanedMigrations.Any() && !pendingMigrations.Any())
            {
                Console.WriteLine("✅ All applied migrations have corresponding files");
                Console.WriteLine("✅ Database and file system are in sync");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Applied migration verification failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> VerifyAllAsync(ChecksumManager checksumManager, string connectionString, string migrationsPath)
    {
        Console.WriteLine("🔍 Running comprehensive verification...");
        Console.WriteLine();

        var checksumResult = await VerifyChecksumsAsync(checksumManager, migrationsPath);
        Console.WriteLine();
        
        var appliedResult = await VerifyAppliedMigrationsAsync(connectionString, migrationsPath);
        Console.WriteLine();

        Console.WriteLine("📈 Verification Summary:");
        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"   Checksum verification: {(checksumResult == 0 ? "✅ PASSED" : "❌ FAILED")}");
        Console.WriteLine($"   Applied migrations:    {(appliedResult == 0 ? "✅ PASSED" : "❌ FAILED")}");
        Console.WriteLine();

        if (checksumResult == 0 && appliedResult == 0)
        {
            Console.WriteLine("🎉 All verifications passed! Migration system is healthy.");
            return 0;
        }
        else
        {
            Console.WriteLine("⚠️ Some verifications failed. Review the issues above.");
            return 1;
        }
    }

    private static int ShowVerifyHelp()
    {
        Console.WriteLine("Usage: dbmigrator verify <action>");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  checksums              Verify migration file checksums");
        Console.WriteLine("  applied                Verify applied migrations against files");
        Console.WriteLine("  all                    Run all verification checks");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator verify checksums");
        Console.WriteLine("  dbmigrator verify applied");
        Console.WriteLine("  dbmigrator verify all");
        
        return 1;
    }
}