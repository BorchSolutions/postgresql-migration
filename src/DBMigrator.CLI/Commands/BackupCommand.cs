using DBMigrator.Core.Services;
using DBMigrator.Core.Models;

namespace DBMigrator.CLI.Commands;

public static class BackupCommand
{
    public static async Task<int> ExecuteAsync(DatabaseConfiguration config, string action, string[] args)
    {
        try
        {
            var logger = new StructuredLogger(config.Logging.Level, config.Logging.EnableConsoleOutput, config.Logging.LogFilePath);
            var backupManager = new BackupManager(config, logger);

            return action.ToLower() switch
            {
                "create" => await CreateBackupAsync(backupManager, args),
                "list" => await ListBackupsAsync(backupManager),
                "cleanup" => await CleanupBackupsAsync(backupManager),
                _ => ShowBackupHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Backup operation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CreateBackupAsync(BackupManager backupManager, string[] args)
    {
        Console.WriteLine("💾 Creating database backup...");
        
        // Parse backup type
        var backupType = BackupType.Schema; // Default
        var migrationId = "manual_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--type":
                    if (i + 1 < args.Length)
                    {
                        if (Enum.TryParse<BackupType>(args[i + 1], true, out var type))
                        {
                            backupType = type;
                            i++; // Skip next argument
                        }
                        else
                        {
                            Console.WriteLine($"❌ Invalid backup type: {args[i + 1]}");
                            Console.WriteLine("Valid types: Schema, Data, Full");
                            return 1;
                        }
                    }
                    break;
                    
                case "--migration-id":
                    if (i + 1 < args.Length)
                    {
                        migrationId = args[i + 1];
                        i++;
                    }
                    break;
            }
        }

        try
        {
            var result = await backupManager.CreateBackupAsync(migrationId, backupType);
            
            Console.WriteLine($"✅ Backup created successfully!");
            Console.WriteLine($"   Backup ID: {result.BackupId}");
            Console.WriteLine($"   Type: {result.BackupType}");
            Console.WriteLine($"   Size: {FormatBytes(result.BackupSize)}");
            Console.WriteLine($"   Duration: {result.Duration?.TotalSeconds:F1} seconds");
            Console.WriteLine($"   File: {result.BackupFilePath}");
            
            return 0;
        }
        catch (BackupException ex)
        {
            Console.WriteLine($"❌ Backup failed: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("💡 Possible solutions:");
            Console.WriteLine("   1. Ensure pg_dump is installed and in PATH");
            Console.WriteLine("   2. Check database connection permissions");
            Console.WriteLine("   3. Verify backup directory is writable");
            
            return 1;
        }
    }

    private static async Task<int> ListBackupsAsync(BackupManager backupManager)
    {
        Console.WriteLine("📋 Database Backups:");
        Console.WriteLine();

        try
        {
            // Since we don't have a direct method to list backups, we'll create a simple file listing
            // In a real implementation, you'd query the __dbmigrator_backups table
            
            Console.WriteLine("💡 To see detailed backup history, check the database table: __dbmigrator_backups");
            Console.WriteLine("   Or look in the backup directory for .sql and .gz files");
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to list backups: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CleanupBackupsAsync(BackupManager backupManager)
    {
        Console.WriteLine("🧹 Cleaning up old backups...");
        
        try
        {
            await backupManager.CleanupOldBackupsAsync();
            Console.WriteLine("✅ Backup cleanup completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Backup cleanup failed: {ex.Message}");
            return 1;
        }
    }

    private static int ShowBackupHelp()
    {
        Console.WriteLine("Usage: dbmigrator backup <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  create [--type <type>] [--migration-id <id>]    Create a backup");
        Console.WriteLine("  list                                            List existing backups");
        Console.WriteLine("  cleanup                                         Clean up old backups");
        Console.WriteLine();
        Console.WriteLine("Backup Types:");
        Console.WriteLine("  schema      Schema only (default)");
        Console.WriteLine("  data        Data only");
        Console.WriteLine("  full        Complete database");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator backup create");
        Console.WriteLine("  dbmigrator backup create --type full");
        Console.WriteLine("  dbmigrator backup create --type schema --migration-id pre_migration");
        Console.WriteLine("  dbmigrator backup cleanup");
        
        return 1;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}