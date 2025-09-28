using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public static class DryRunCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, string migrationFile)
    {
        try
        {
            Console.WriteLine("🔍 Running dry-run simulation...");
            Console.WriteLine($"Migration file: {migrationFile}");
            Console.WriteLine();

            var migrationFilePath = Path.Combine(migrationsPath, migrationFile);
            
            if (!File.Exists(migrationFilePath))
            {
                Console.WriteLine($"❌ Migration file not found: {migrationFilePath}");
                return 1;
            }

            var migrationContent = await File.ReadAllTextAsync(migrationFilePath);
            var executor = new DryRunExecutor();
            
            var result = await executor.SimulateAsync(migrationFile, migrationContent);
            
            // Display results
            Console.WriteLine($"📊 Dry Run Results for: {result.MigrationId}");
            Console.WriteLine($"   Valid: {(result.IsValid ? "✅ Yes" : "❌ No")}");
            Console.WriteLine($"   Can Proceed: {(result.CanProceed ? "✅ Yes" : "❌ No")}");
            Console.WriteLine($"   Estimated Duration: {result.EstimatedDuration.TotalSeconds:F1} seconds");
            Console.WriteLine($"   Estimated Rows Affected: {result.EstimatedRowsAffected:N0}");
            Console.WriteLine();

            // Show steps
            if (result.Steps.Any())
            {
                Console.WriteLine("📋 Migration Steps:");
                foreach (var step in result.Steps)
                {
                    var lockIcon = step.RequiresLock ? "🔒" : "🔓";
                    var durationText = step.EstimatedDuration.TotalSeconds > 1 
                        ? $"{step.EstimatedDuration.TotalSeconds:F1}s"
                        : $"{step.EstimatedDuration.TotalMilliseconds:F0}ms";
                    
                    Console.WriteLine($"   {step.Order}. {lockIcon} {step.Description}");
                    Console.WriteLine($"      Type: {step.Type}, Duration: {durationText}, Rows: {step.EstimatedRowsAffected:N0}");
                    
                    if (step.AffectedObjects.Any())
                    {
                        Console.WriteLine($"      Objects: {string.Join(", ", step.AffectedObjects)}");
                    }
                    Console.WriteLine();
                }
            }

            // Show impact analysis
            Console.WriteLine("📈 Impact Analysis:");
            Console.WriteLine($"   Data Risk: {GetRiskIcon(result.Impact.DataRisk)} {result.Impact.DataRisk}");
            Console.WriteLine($"   Requires Downtime: {(result.Impact.RequiresDowntime ? "⚠️ Yes" : "✅ No")}");
            
            if (!string.IsNullOrEmpty(result.Impact.DowntimeReason))
            {
                Console.WriteLine($"   Downtime Reason: {result.Impact.DowntimeReason}");
            }
            
            if (result.Impact.AffectedTables.Any())
            {
                Console.WriteLine($"   Affected Tables: {string.Join(", ", result.Impact.AffectedTables)}");
            }
            
            if (result.Impact.AffectedIndexes.Any())
            {
                Console.WriteLine($"   Affected Indexes: {string.Join(", ", result.Impact.AffectedIndexes)}");
            }
            Console.WriteLine();

            // Show warnings
            if (result.HasWarnings)
            {
                Console.WriteLine("⚠️ Warnings:");
                foreach (var warning in result.Warnings)
                {
                    var severityIcon = warning.Severity switch
                    {
                        DBMigrator.Core.Models.DryRun.WarningSeverity.High => "🔴",
                        DBMigrator.Core.Models.DryRun.WarningSeverity.Medium => "🟡",
                        DBMigrator.Core.Models.DryRun.WarningSeverity.Low => "🟢",
                        _ => "ℹ️"
                    };
                    
                    Console.WriteLine($"   {severityIcon} {warning.Message}");
                    Console.WriteLine($"      Recommendation: {warning.Recommendation}");
                    Console.WriteLine();
                }
            }

            // Show errors
            if (result.HasErrors)
            {
                Console.WriteLine("❌ Errors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"   {error.Code}: {error.Message}");
                    Console.WriteLine($"   Resolution: {error.Resolution}");
                    Console.WriteLine();
                }
            }

            // Show backup recommendations
            if (result.Impact.BackupRecommendations.Any())
            {
                Console.WriteLine("💾 Backup Recommendations:");
                foreach (var recommendation in result.Impact.BackupRecommendations)
                {
                    Console.WriteLine($"   • {recommendation}");
                }
                Console.WriteLine();
            }

            if (result.CanProceed)
            {
                Console.WriteLine("✅ Migration simulation completed successfully");
                Console.WriteLine("💡 Use 'dbmigrator apply' to execute this migration");
            }
            else
            {
                Console.WriteLine("❌ Migration has issues that must be resolved before applying");
            }

            return result.CanProceed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Dry run failed: {ex.Message}");
            return 1;
        }
    }

    private static string GetRiskIcon(DBMigrator.Core.Models.DryRun.DataRisk risk)
    {
        return risk switch
        {
            DBMigrator.Core.Models.DryRun.DataRisk.Critical => "🔴",
            DBMigrator.Core.Models.DryRun.DataRisk.High => "🟠",
            DBMigrator.Core.Models.DryRun.DataRisk.Medium => "🟡",
            DBMigrator.Core.Models.DryRun.DataRisk.Low => "🟢",
            _ => "⚪"
        };
    }
}