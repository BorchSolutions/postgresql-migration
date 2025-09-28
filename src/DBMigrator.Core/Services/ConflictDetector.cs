using DBMigrator.Core.Models.Conflicts;

namespace DBMigrator.Core.Services;

public class ConflictDetector
{
    public async Task<ConflictDetection> DetectConflictsAsync(string migrationsPath)
    {
        var detection = new ConflictDetection();

        if (!Directory.Exists(migrationsPath))
        {
            return detection;
        }

        var migrationFiles = GetMigrationFiles(migrationsPath);
        
        // Detect timestamp conflicts
        await DetectTimestampConflictsAsync(migrationFiles, detection);
        
        // Detect out-of-order migrations
        await DetectOutOfOrderMigrationsAsync(migrationFiles, detection);
        
        // Detect missing dependencies
        await DetectMissingDependenciesAsync(migrationFiles, detection);
        
        // Detect checksum mismatches
        await DetectChecksumMismatchesAsync(migrationFiles, detection);

        return detection;
    }

    private List<MigrationFileInfo> GetMigrationFiles(string migrationsPath)
    {
        var files = new List<MigrationFileInfo>();
        
        var sqlFiles = Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .OrderBy(f => f);

        foreach (var file in sqlFiles)
        {
            var fileName = Path.GetFileName(file);
            var timestamp = ExtractTimestamp(fileName);
            
            if (timestamp.HasValue)
            {
                files.Add(new MigrationFileInfo
                {
                    FilePath = file,
                    FileName = fileName,
                    Timestamp = timestamp.Value,
                    MigrationId = ExtractMigrationId(fileName)
                });
            }
        }

        return files.OrderBy(f => f.Timestamp).ToList();
    }

    private async Task DetectTimestampConflictsAsync(List<MigrationFileInfo> files, ConflictDetection detection)
    {
        var timestampGroups = files.GroupBy(f => f.Timestamp).Where(g => g.Count() > 1);

        foreach (var group in timestampGroups)
        {
            var conflictFiles = group.ToList();
            
            detection.Conflicts.Add(new MigrationConflict
            {
                Type = ConflictType.DuplicateTimestamp,
                Severity = ConflictSeverity.Error,
                Description = $"Multiple migrations have the same timestamp: {group.Key:yyyyMMddHHmmss}",
                AffectedObjects = conflictFiles.Select(f => f.FileName).ToList(),
                Resolution = "Rename one of the migration files with a unique timestamp"
            });
        }

        await Task.CompletedTask;
    }

    private async Task DetectOutOfOrderMigrationsAsync(List<MigrationFileInfo> files, ConflictDetection detection)
    {
        // This would need to check against applied migrations from database
        // For MVP 3, we'll simulate this check
        var lastAppliedTimestamp = GetLastAppliedTimestamp(); // Would come from DB
        
        foreach (var file in files)
        {
            if (file.Timestamp < lastAppliedTimestamp && !IsAlreadyApplied(file.MigrationId))
            {
                detection.Conflicts.Add(new MigrationConflict
                {
                    Type = ConflictType.OutOfOrder,
                    Severity = ConflictSeverity.Warning,
                    Description = $"Migration {file.MigrationId} has timestamp earlier than last applied migration",
                    MigrationId = file.MigrationId,
                    MigrationFile = file.FileName,
                    AffectedObjects = new List<string> { file.FileName },
                    Resolution = "Apply migrations in chronological order or resequence this migration"
                });
            }
        }

        await Task.CompletedTask;
    }

    private async Task DetectMissingDependenciesAsync(List<MigrationFileInfo> files, ConflictDetection detection)
    {
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FilePath);
            var dependencies = ExtractDependencies(content);

            foreach (var dependency in dependencies)
            {
                if (!files.Any(f => f.MigrationId == dependency))
                {
                    detection.Conflicts.Add(new MigrationConflict
                    {
                        Type = ConflictType.MissingDependency,
                        Severity = ConflictSeverity.Error,
                        Description = $"Migration {file.MigrationId} depends on missing migration {dependency}",
                        MigrationId = file.MigrationId,
                        MigrationFile = file.FileName,
                        AffectedObjects = new List<string> { dependency },
                        Resolution = "Create the missing dependency migration or remove the dependency"
                    });
                }
            }
        }
    }

    private async Task DetectChecksumMismatchesAsync(List<MigrationFileInfo> files, ConflictDetection detection)
    {
        foreach (var file in files)
        {
            if (IsAlreadyApplied(file.MigrationId))
            {
                var currentChecksum = await CalculateChecksumAsync(file.FilePath);
                var storedChecksum = GetStoredChecksum(file.MigrationId); // Would come from DB

                if (storedChecksum != null && currentChecksum != storedChecksum)
                {
                    detection.Conflicts.Add(new MigrationConflict
                    {
                        Type = ConflictType.ChecksumMismatch,
                        Severity = ConflictSeverity.Critical,
                        Description = $"Migration {file.MigrationId} has been modified after being applied",
                        MigrationId = file.MigrationId,
                        MigrationFile = file.FileName,
                        AffectedObjects = new List<string> { file.FileName },
                        Resolution = "Revert changes to the migration file or create a new migration for the changes"
                    });
                }
            }
        }
    }

    private DateTime? ExtractTimestamp(string fileName)
    {
        // Extract timestamp from filename like "20241127120000_migration_name.sql"
        var parts = fileName.Split('_');
        if (parts.Length > 0 && parts[0].Length == 14 && DateTime.TryParseExact(parts[0], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var timestamp))
        {
            return timestamp;
        }
        return null;
    }

    private string ExtractMigrationId(string fileName)
    {
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private List<string> ExtractDependencies(string migrationContent)
    {
        var dependencies = new List<string>();
        
        // Look for dependency comments in the migration file
        // Example: -- @depends: 20241127120000_create_users
        var lines = migrationContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("-- @depends:", StringComparison.OrdinalIgnoreCase))
            {
                var dependency = trimmed.Substring("-- @depends:".Length).Trim();
                if (!string.IsNullOrEmpty(dependency))
                {
                    dependencies.Add(dependency);
                }
            }
        }

        return dependencies;
    }

    private DateTime GetLastAppliedTimestamp()
    {
        // This would query the database for the last applied migration timestamp
        // For MVP 3, we'll return a reasonable default
        return DateTime.UtcNow.AddDays(-1);
    }

    private bool IsAlreadyApplied(string migrationId)
    {
        // This would check the database migration history
        // For MVP 3, we'll return false as default
        return false;
    }

    private string? GetStoredChecksum(string migrationId)
    {
        // This would get the checksum from database migration history
        // For MVP 3, we'll return null as default
        return null;
    }

    private async Task<string> CalculateChecksumAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    public async Task<List<ConflictResolution>> GenerateResolutionsAsync(ConflictDetection detection)
    {
        var resolutions = new List<ConflictResolution>();

        foreach (var conflict in detection.Conflicts)
        {
            var resolution = conflict.Type switch
            {
                ConflictType.DuplicateTimestamp => new ConflictResolution
                {
                    ConflictId = conflict.Id,
                    Strategy = ResolutionStrategy.Rename,
                    Description = "Rename migration file with unique timestamp",
                    RequiresManualIntervention = true,
                    Steps = new List<string>
                    {
                        "1. Generate new timestamp: " + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                        "2. Rename migration file with new timestamp",
                        "3. Update any references to the old migration ID"
                    }
                },
                
                ConflictType.OutOfOrder => new ConflictResolution
                {
                    ConflictId = conflict.Id,
                    Strategy = ResolutionStrategy.Reorder,
                    Description = "Resequence migration or apply in correct order",
                    RequiresManualIntervention = false,
                    Steps = new List<string>
                    {
                        "1. Apply migrations in chronological order",
                        "2. Or rename migration with current timestamp if changes are needed"
                    }
                },
                
                ConflictType.ChecksumMismatch => new ConflictResolution
                {
                    ConflictId = conflict.Id,
                    Strategy = ResolutionStrategy.Manual,
                    Description = "Resolve checksum mismatch manually",
                    RequiresManualIntervention = true,
                    Steps = new List<string>
                    {
                        "1. Review changes to the migration file",
                        "2. If changes are valid, create a new migration",
                        "3. If changes are invalid, revert the file to original state"
                    }
                },
                
                _ => new ConflictResolution
                {
                    ConflictId = conflict.Id,
                    Strategy = ResolutionStrategy.Manual,
                    Description = "Manual resolution required",
                    RequiresManualIntervention = true
                }
            };

            resolutions.Add(resolution);
        }

        return resolutions;
    }
}

public class MigrationFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string MigrationId { get; set; } = string.Empty;
}