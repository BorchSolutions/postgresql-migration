using DBMigrator.Core.Services;
using DBMigrator.Core.Models;

namespace DBMigrator.CLI.Commands;

public static class ValidateCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, string? migrationFile = null)
    {
        try
        {
            Console.WriteLine("🔍 Validating migrations...");
            Console.WriteLine();

            var config = new ValidationConfiguration
            {
                ValidateBeforeApply = true,
                CheckConflictsBeforeApply = true,
                RequireDryRunForDestructive = false
            };

            var logger = new StructuredLogger("Info", true);
            var validator = new MigrationValidator(connectionString, config, logger);

            if (!string.IsNullOrEmpty(migrationFile))
            {
                return await ValidateSingleMigrationAsync(validator, migrationsPath, migrationFile);
            }
            else
            {
                return await ValidateAllMigrationsAsync(validator, migrationsPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Validation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ValidateSingleMigrationAsync(MigrationValidator validator, string migrationsPath, string migrationFile)
    {
        var migrationPath = Path.Combine(migrationsPath, migrationFile);
        
        if (!File.Exists(migrationPath))
        {
            Console.WriteLine($"❌ Migration file not found: {migrationPath}");
            return 1;
        }

        Console.WriteLine($"📄 Validating: {migrationFile}");
        
        var content = await File.ReadAllTextAsync(migrationPath);
        var migrationId = Path.GetFileNameWithoutExtension(migrationFile);
        
        var result = await validator.ValidateMigrationAsync(content, migrationId);
        
        DisplayValidationResult(result);
        
        return result.IsValid ? 0 : 1;
    }

    private static async Task<int> ValidateAllMigrationsAsync(MigrationValidator validator, string migrationsPath)
    {
        if (!Directory.Exists(migrationsPath))
        {
            Console.WriteLine($"❌ Migrations directory not found: {migrationsPath}");
            return 1;
        }

        var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .OrderBy(f => f)
            .ToList();

        if (!migrationFiles.Any())
        {
            Console.WriteLine("ℹ️ No migration files found to validate");
            return 0;
        }

        Console.WriteLine($"📋 Found {migrationFiles.Count} migration(s) to validate");
        Console.WriteLine();

        var totalResults = new List<ValidationResult>();
        var hasErrors = false;

        foreach (var filePath in migrationFiles)
        {
            var fileName = Path.GetFileName(filePath);
            var migrationId = Path.GetFileNameWithoutExtension(fileName);
            
            Console.WriteLine($"🔍 Validating: {fileName}");
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var result = await validator.ValidateMigrationAsync(content, migrationId);
                
                totalResults.Add(result);
                
                if (result.IsValid)
                {
                    Console.WriteLine($"   ✅ Valid");
                }
                else
                {
                    Console.WriteLine($"   ❌ Invalid ({result.Errors.Count} errors, {result.Warnings.Count} warnings)");
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   💥 Validation failed: {ex.Message}");
                hasErrors = true;
            }
            
            Console.WriteLine();
        }

        // Display summary
        DisplayValidationSummary(totalResults);

        return hasErrors ? 1 : 0;
    }

    private static void DisplayValidationResult(ValidationResult result)
    {
        Console.WriteLine($"📊 Validation Results for: {result.MigrationId}");
        Console.WriteLine($"   Overall Status: {(result.IsValid ? "✅ VALID" : "❌ INVALID")}");
        Console.WriteLine($"   Validated at: {result.ValidatedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        if (result.Errors.Any())
        {
            Console.WriteLine("❌ Errors:");
            foreach (var error in result.Errors)
            {
                var severityIcon = GetSeverityIcon(error.Severity);
                Console.WriteLine($"   {severityIcon} {error.Code}: {error.Message}");
                if (error.Line > 0)
                {
                    Console.WriteLine($"      Line: {error.Line}");
                }
                if (!string.IsNullOrEmpty(error.SqlState))
                {
                    Console.WriteLine($"      SQL State: {error.SqlState}");
                }
                Console.WriteLine();
            }
        }

        if (result.Warnings.Any())
        {
            Console.WriteLine("⚠️ Warnings:");
            foreach (var warning in result.Warnings)
            {
                var severityIcon = GetSeverityIcon(warning.Severity);
                Console.WriteLine($"   {severityIcon} {warning.Code}: {warning.Message}");
                if (warning.Line > 0)
                {
                    Console.WriteLine($"      Line: {warning.Line}");
                }
                Console.WriteLine();
            }
        }

        if (!result.Errors.Any() && !result.Warnings.Any())
        {
            Console.WriteLine("✨ No issues found!");
        }
    }

    private static void DisplayValidationSummary(List<ValidationResult> results)
    {
        Console.WriteLine("📈 Validation Summary:");
        Console.WriteLine("═══════════════════════");
        
        var validCount = results.Count(r => r.IsValid);
        var invalidCount = results.Count(r => !r.IsValid);
        var totalErrors = results.Sum(r => r.Errors.Count);
        var totalWarnings = results.Sum(r => r.Warnings.Count);
        var criticalIssues = results.Count(r => r.HasCriticalIssues);

        Console.WriteLine($"   Total Migrations: {results.Count}");
        Console.WriteLine($"   ✅ Valid: {validCount}");
        Console.WriteLine($"   ❌ Invalid: {invalidCount}");
        Console.WriteLine($"   🔴 Critical Issues: {criticalIssues}");
        Console.WriteLine($"   📛 Total Errors: {totalErrors}");
        Console.WriteLine($"   ⚠️ Total Warnings: {totalWarnings}");
        Console.WriteLine();

        if (invalidCount > 0)
        {
            Console.WriteLine("💡 Recommendations:");
            Console.WriteLine("   1. Fix all critical errors before applying migrations");
            Console.WriteLine("   2. Review and address warnings for best practices");
            Console.WriteLine("   3. Use 'dbmigrator dry-run' to test specific migrations");
            Console.WriteLine("   4. Consider running 'dbmigrator check-conflicts' for conflict detection");
        }
        else
        {
            Console.WriteLine("🎉 All migrations are valid and ready to apply!");
        }
    }

    private static string GetSeverityIcon(ValidationSeverity severity)
    {
        return severity switch
        {
            ValidationSeverity.Critical => "🔴",
            ValidationSeverity.High => "🟠", 
            ValidationSeverity.Medium => "🟡",
            ValidationSeverity.Low => "🟢",
            _ => "ℹ️"
        };
    }
}