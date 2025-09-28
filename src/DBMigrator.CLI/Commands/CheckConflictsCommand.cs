using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public static class CheckConflictsCommand
{
    public static async Task<int> ExecuteAsync(string migrationsPath)
    {
        try
        {
            Console.WriteLine("🔍 Checking for migration conflicts...");
            Console.WriteLine($"Migrations path: {migrationsPath}");
            Console.WriteLine();

            if (!Directory.Exists(migrationsPath))
            {
                Console.WriteLine($"❌ Migrations directory not found: {migrationsPath}");
                return 1;
            }

            var detector = new ConflictDetector();
            var detection = await detector.DetectConflictsAsync(migrationsPath);

            if (!detection.HasConflicts)
            {
                Console.WriteLine("✅ No migration conflicts detected");
                Console.WriteLine($"Checked {Directory.GetFiles(migrationsPath, "*.sql").Length} migration files");
                return 0;
            }

            Console.WriteLine($"⚠️ Found {detection.ConflictCount} conflict(s):");
            Console.WriteLine();

            // Group conflicts by severity
            var criticalConflicts = detection.GetCriticalConflicts();
            var errorConflicts = detection.Conflicts.Where(c => c.Severity == DBMigrator.Core.Models.Conflicts.ConflictSeverity.Error).ToList();
            var warningConflicts = detection.Conflicts.Where(c => c.Severity == DBMigrator.Core.Models.Conflicts.ConflictSeverity.Warning).ToList();

            // Show critical conflicts first
            if (criticalConflicts.Any())
            {
                Console.WriteLine("🔴 Critical Conflicts (Must be resolved):");
                await DisplayConflicts(criticalConflicts, detector);
                Console.WriteLine();
            }

            // Show error conflicts
            if (errorConflicts.Any())
            {
                Console.WriteLine("❌ Error Conflicts (Should be resolved):");
                await DisplayConflicts(errorConflicts, detector);
                Console.WriteLine();
            }

            // Show warning conflicts
            if (warningConflicts.Any())
            {
                Console.WriteLine("⚠️ Warning Conflicts (Review recommended):");
                await DisplayConflicts(warningConflicts, detector);
                Console.WriteLine();
            }

            // Show summary and recommendations
            Console.WriteLine("📋 Summary:");
            Console.WriteLine($"   Total conflicts: {detection.ConflictCount}");
            Console.WriteLine($"   Critical: {criticalConflicts.Count}");
            Console.WriteLine($"   Errors: {errorConflicts.Count}");
            Console.WriteLine($"   Warnings: {warningConflicts.Count}");
            Console.WriteLine();

            if (criticalConflicts.Any() || errorConflicts.Any())
            {
                Console.WriteLine("🛠️ Next Steps:");
                Console.WriteLine("   1. Resolve critical and error conflicts");
                Console.WriteLine("   2. Run 'dbmigrator check-conflicts' again to verify");
                Console.WriteLine("   3. Use 'dbmigrator dry-run' to test individual migrations");
                Console.WriteLine();
            }

            return criticalConflicts.Any() ? 2 : (errorConflicts.Any() ? 1 : 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Conflict detection failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task DisplayConflicts(List<DBMigrator.Core.Models.Conflicts.MigrationConflict> conflicts, ConflictDetector detector)
    {
        var resolutions = await detector.GenerateResolutionsAsync(new DBMigrator.Core.Models.Conflicts.ConflictDetection 
        { 
            Conflicts = conflicts 
        });

        for (int i = 0; i < conflicts.Count; i++)
        {
            var conflict = conflicts[i];
            var resolution = resolutions.FirstOrDefault(r => r.ConflictId == conflict.Id);

            Console.WriteLine($"   {i + 1}. {GetConflictTypeIcon(conflict.Type)} {conflict.Type}");
            Console.WriteLine($"      {conflict.Description}");
            
            if (!string.IsNullOrEmpty(conflict.MigrationId))
            {
                Console.WriteLine($"      Migration: {conflict.MigrationId}");
            }
            
            if (conflict.AffectedObjects.Any())
            {
                Console.WriteLine($"      Affected: {string.Join(", ", conflict.AffectedObjects)}");
            }

            if (resolution != null)
            {
                Console.WriteLine($"      Resolution: {resolution.Description}");
                
                if (resolution.RequiresManualIntervention)
                {
                    Console.WriteLine($"      ⚠️ Requires manual intervention");
                }

                if (resolution.Steps.Any())
                {
                    Console.WriteLine($"      Steps:");
                    foreach (var step in resolution.Steps)
                    {
                        Console.WriteLine($"         • {step}");
                    }
                }
            }
            
            Console.WriteLine();
        }
    }

    private static string GetConflictTypeIcon(DBMigrator.Core.Models.Conflicts.ConflictType type)
    {
        return type switch
        {
            DBMigrator.Core.Models.Conflicts.ConflictType.DuplicateTimestamp => "🔄",
            DBMigrator.Core.Models.Conflicts.ConflictType.OutOfOrder => "📅",
            DBMigrator.Core.Models.Conflicts.ConflictType.MissingDependency => "🔗",
            DBMigrator.Core.Models.Conflicts.ConflictType.CircularDependency => "🔄",
            DBMigrator.Core.Models.Conflicts.ConflictType.ChecksumMismatch => "🔐",
            DBMigrator.Core.Models.Conflicts.ConflictType.SchemaConflict => "🏗️",
            DBMigrator.Core.Models.Conflicts.ConflictType.DataConflict => "📊",
            DBMigrator.Core.Models.Conflicts.ConflictType.AlreadyApplied => "✅",
            _ => "❓"
        };
    }
}