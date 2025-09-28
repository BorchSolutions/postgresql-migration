using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public static class ListCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, bool showApplied, bool showPending, bool showAll)
    {
        try
        {
            Console.WriteLine("📋 Migration List");
            Console.WriteLine();

            var migrationService = new MigrationService(connectionString);
            
            // Get applied migrations info from database
            bool canAccessDatabase = true;
            try
            {
                await migrationService.ShowStatusAsync();
            }
            catch (Exception)
            {
                Console.WriteLine("⚠️ Could not connect to database. Showing file-based information only.");
                canAccessDatabase = false;
                showApplied = false;
                showAll = false;
                showPending = true;
            }

            // Get migration files from directory
            var migrationFiles = new List<(string fileName, string filePath, DateTime? timestamp)>();
            
            if (Directory.Exists(migrationsPath))
            {
                var sqlFiles = Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .OrderBy(f => f);

                foreach (var file in sqlFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var timestamp = ExtractTimestamp(fileName);
                    migrationFiles.Add((fileName, file, timestamp));
                }
            }

            // Sort by timestamp
            migrationFiles = migrationFiles.OrderBy(f => f.timestamp ?? DateTime.MinValue).ToList();

            if (showApplied || showAll)
            {
                if (canAccessDatabase)
                {
                    Console.WriteLine("✅ Applied Migrations:");
                    Console.WriteLine("   Use 'dbmigrator status' for detailed migration history");
                    Console.WriteLine();
                }
            }

            if (showPending || showAll)
            {
                await ShowMigrationFiles(migrationFiles);
            }

            // Show summary
            Console.WriteLine("📊 Summary:");
            Console.WriteLine($"   Migration files: {migrationFiles.Count}");
            
            if (canAccessDatabase)
            {
                Console.WriteLine("   For applied migration details, use 'dbmigrator status'");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to list migrations: {ex.Message}");
            return 1;
        }
    }

    private static async Task ShowMigrationFiles(List<(string fileName, string filePath, DateTime? timestamp)> migrationFiles)
    {
        Console.WriteLine("📁 Migration Files:");
        
        if (!migrationFiles.Any())
        {
            Console.WriteLine("   No migration files found");
        }
        else
        {
            foreach (var file in migrationFiles)
            {
                var size = new FileInfo(file.filePath).Length;
                var sizeText = size < 1024 ? $"{size}B" : 
                              size < 1024 * 1024 ? $"{size / 1024}KB" : 
                              $"{size / (1024 * 1024)}MB";

                Console.WriteLine($"   📄 {file.fileName}");
                
                if (file.timestamp.HasValue)
                {
                    Console.WriteLine($"      Timestamp: {file.timestamp:yyyy-MM-dd HH:mm:ss}");
                }
                
                Console.WriteLine($"      Size: {sizeText}");
                
                // Try to read first few lines for preview
                try
                {
                    var lines = await File.ReadAllLinesAsync(file.filePath);
                    var contentLines = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("--")).Take(2);
                    
                    if (contentLines.Any())
                    {
                        var preview = string.Join(" ", contentLines);
                        if (preview.Length > 60)
                        {
                            preview = preview.Substring(0, 60) + "...";
                        }
                        Console.WriteLine($"      Preview: {preview}");
                    }
                }
                catch
                {
                    // Ignore preview errors
                }
                
                Console.WriteLine();
            }
        }
        
        Console.WriteLine();
    }

    private static DateTime? ExtractTimestamp(string fileName)
    {
        // Extract timestamp from filename like "20241127120000_migration_name.sql"
        var parts = fileName.Split('_');
        if (parts.Length > 0 && parts[0].Length == 14 && 
            DateTime.TryParseExact(parts[0], "yyyyMMddHHmmss", null, 
                System.Globalization.DateTimeStyles.None, out var timestamp))
        {
            return timestamp;
        }
        return null;
    }
}