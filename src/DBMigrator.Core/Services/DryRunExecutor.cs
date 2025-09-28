using DBMigrator.Core.Models.DryRun;
using System.Text.RegularExpressions;

namespace DBMigrator.Core.Services;

public class DryRunExecutor
{
    public async Task<DryRunResult> SimulateAsync(string migrationFile, string migrationContent)
    {
        var result = new DryRunResult
        {
            MigrationId = Path.GetFileNameWithoutExtension(migrationFile),
            MigrationFile = migrationFile
        };

        try
        {
            // Parse SQL statements
            var statements = ParseSqlStatements(migrationContent);
            
            // Analyze each statement
            for (int i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                var step = await AnalyzeStatementAsync(statement, i + 1);
                result.Steps.Add(step);
            }

            // Perform impact analysis
            result.Impact = AnalyzeImpact(result.Steps);
            
            // Calculate totals
            result.EstimatedDuration = TimeSpan.FromMilliseconds(result.Steps.Sum(s => s.EstimatedDuration.TotalMilliseconds));
            result.EstimatedRowsAffected = result.Steps.Sum(s => s.EstimatedRowsAffected);
            
            // Validate migration
            result.IsValid = ValidateMigration(result);
            
            // Generate warnings
            GenerateWarnings(result);
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new DryRunError
            {
                Code = "PARSE_ERROR",
                Message = $"Failed to parse migration: {ex.Message}",
                Resolution = "Check SQL syntax and ensure migration file is valid"
            });
        }

        return result;
    }

    private List<string> ParseSqlStatements(string content)
    {
        var statements = new List<string>();
        
        // Remove comments
        content = Regex.Replace(content, @"--.*$", "", RegexOptions.Multiline);
        content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
        
        // Split by semicolon (simple approach for MVP 3)
        var parts = content.Split(';', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                statements.Add(trimmed);
            }
        }
        
        return statements;
    }

    private async Task<DryRunStep> AnalyzeStatementAsync(string statement, int order)
    {
        var step = new DryRunStep
        {
            Order = order,
            SqlStatement = statement,
            Type = DetermineStatementType(statement),
            EstimatedDuration = EstimateDuration(statement),
            EstimatedRowsAffected = EstimateRowsAffected(statement),
            AffectedObjects = ExtractAffectedObjects(statement),
            RequiresLock = RequiresLock(statement),
            LockType = DetermineLockType(statement)
        };

        step.Description = GenerateStepDescription(step);

        return await Task.FromResult(step);
    }

    private StepType DetermineStatementType(string statement)
    {
        var upperStatement = statement.ToUpperInvariant().Trim();
        
        if (upperStatement.StartsWith("CREATE TABLE")) return StepType.CreateTable;
        if (upperStatement.StartsWith("DROP TABLE")) return StepType.DropTable;
        if (upperStatement.StartsWith("ALTER TABLE")) return StepType.AlterTable;
        if (upperStatement.StartsWith("CREATE INDEX")) return StepType.CreateIndex;
        if (upperStatement.StartsWith("DROP INDEX")) return StepType.DropIndex;
        if (upperStatement.StartsWith("CREATE UNIQUE INDEX")) return StepType.CreateIndex;
        if (upperStatement.StartsWith("INSERT INTO")) return StepType.InsertData;
        if (upperStatement.StartsWith("UPDATE")) return StepType.UpdateData;
        if (upperStatement.StartsWith("DELETE FROM")) return StepType.DeleteData;
        if (upperStatement.StartsWith("CREATE FUNCTION")) return StepType.CreateFunction;
        if (upperStatement.StartsWith("DROP FUNCTION")) return StepType.DropFunction;
        if (upperStatement.StartsWith("CREATE VIEW")) return StepType.CreateView;
        if (upperStatement.StartsWith("DROP VIEW")) return StepType.DropView;
        
        return StepType.Custom;
    }

    private TimeSpan EstimateDuration(string statement)
    {
        var type = DetermineStatementType(statement);
        
        return type switch
        {
            StepType.CreateTable => TimeSpan.FromMilliseconds(500),
            StepType.DropTable => TimeSpan.FromMilliseconds(200),
            StepType.AlterTable => TimeSpan.FromMilliseconds(1000),
            StepType.CreateIndex => TimeSpan.FromSeconds(5), // Can be slow on large tables
            StepType.DropIndex => TimeSpan.FromMilliseconds(100),
            StepType.InsertData => EstimateDataOperationDuration(statement),
            StepType.UpdateData => EstimateDataOperationDuration(statement),
            StepType.DeleteData => EstimateDataOperationDuration(statement),
            _ => TimeSpan.FromMilliseconds(250)
        };
    }

    private TimeSpan EstimateDataOperationDuration(string statement)
    {
        // Simple heuristic based on statement complexity
        var baseTime = TimeSpan.FromMilliseconds(100);
        var complexity = statement.Length / 100; // Rough complexity measure
        return baseTime.Add(TimeSpan.FromMilliseconds(complexity * 50));
    }

    private long EstimateRowsAffected(string statement)
    {
        var type = DetermineStatementType(statement);
        
        // For MVP 3, we'll use simple heuristics
        // In a real implementation, this would query the database for table sizes
        return type switch
        {
            StepType.CreateTable => 0,
            StepType.DropTable => GetEstimatedTableRows(ExtractTableName(statement)),
            StepType.AlterTable => 0, // Structure change, not data
            StepType.CreateIndex => 0,
            StepType.DropIndex => 0,
            StepType.InsertData => EstimateInsertRows(statement),
            StepType.UpdateData => EstimateUpdateRows(statement),
            StepType.DeleteData => EstimateDeleteRows(statement),
            _ => 0
        };
    }

    private List<string> ExtractAffectedObjects(string statement)
    {
        var objects = new List<string>();
        var upperStatement = statement.ToUpperInvariant();
        
        // Extract table names (simplified regex patterns)
        var tableMatches = Regex.Matches(upperStatement, @"(?:TABLE|FROM|INTO|UPDATE)\s+(\w+)", RegexOptions.IgnoreCase);
        foreach (Match match in tableMatches)
        {
            if (match.Groups.Count > 1)
            {
                objects.Add(match.Groups[1].Value.ToLowerInvariant());
            }
        }
        
        // Extract index names
        var indexMatches = Regex.Matches(upperStatement, @"INDEX\s+(\w+)", RegexOptions.IgnoreCase);
        foreach (Match match in indexMatches)
        {
            if (match.Groups.Count > 1)
            {
                objects.Add($"index_{match.Groups[1].Value.ToLowerInvariant()}");
            }
        }
        
        return objects.Distinct().ToList();
    }

    private bool RequiresLock(string statement)
    {
        var type = DetermineStatementType(statement);
        
        return type switch
        {
            StepType.CreateTable => true,
            StepType.DropTable => true,
            StepType.AlterTable => true,
            StepType.CreateIndex => true,
            StepType.DropIndex => true,
            StepType.UpdateData => true,
            StepType.DeleteData => true,
            _ => false
        };
    }

    private LockType DetermineLockType(string statement)
    {
        var type = DetermineStatementType(statement);
        
        return type switch
        {
            StepType.DropTable => LockType.AccessExclusive,
            StepType.AlterTable => LockType.AccessExclusive,
            StepType.CreateIndex => LockType.Shared,
            StepType.DropIndex => LockType.AccessExclusive,
            StepType.UpdateData => LockType.Exclusive,
            StepType.DeleteData => LockType.Exclusive,
            _ => LockType.None
        };
    }

    private string GenerateStepDescription(DryRunStep step)
    {
        return step.Type switch
        {
            StepType.CreateTable => $"Create table {string.Join(", ", step.AffectedObjects)}",
            StepType.DropTable => $"Drop table {string.Join(", ", step.AffectedObjects)}",
            StepType.AlterTable => $"Alter table {string.Join(", ", step.AffectedObjects)}",
            StepType.CreateIndex => $"Create index on {string.Join(", ", step.AffectedObjects)}",
            StepType.DropIndex => $"Drop index {string.Join(", ", step.AffectedObjects)}",
            StepType.InsertData => $"Insert data into {string.Join(", ", step.AffectedObjects)}",
            StepType.UpdateData => $"Update data in {string.Join(", ", step.AffectedObjects)}",
            StepType.DeleteData => $"Delete data from {string.Join(", ", step.AffectedObjects)}",
            _ => $"Execute custom SQL statement"
        };
    }

    private ImpactAnalysis AnalyzeImpact(List<DryRunStep> steps)
    {
        var analysis = new ImpactAnalysis();
        
        foreach (var step in steps)
        {
            analysis.AffectedTables.AddRange(step.AffectedObjects.Where(o => !o.StartsWith("index_")));
            analysis.AffectedIndexes.AddRange(step.AffectedObjects.Where(o => o.StartsWith("index_")));
        }
        
        analysis.AffectedTables = analysis.AffectedTables.Distinct().ToList();
        analysis.AffectedIndexes = analysis.AffectedIndexes.Distinct().ToList();
        
        analysis.TotalEstimatedRowsAffected = steps.Sum(s => s.EstimatedRowsAffected);
        analysis.TotalEstimatedDuration = TimeSpan.FromMilliseconds(steps.Sum(s => s.EstimatedDuration.TotalMilliseconds));
        
        // Determine if downtime is required
        analysis.RequiresDowntime = steps.Any(s => s.Type == StepType.DropTable || 
                                                   (s.Type == StepType.AlterTable && s.LockType == LockType.AccessExclusive));
        
        if (analysis.RequiresDowntime)
        {
            analysis.DowntimeReason = "Operations require exclusive table locks";
        }
        
        // Assess data risk
        analysis.DataRisk = AssessDataRisk(steps);
        
        // Generate backup recommendations
        if (analysis.DataRisk >= DataRisk.Medium)
        {
            analysis.BackupRecommendations.Add("Create full database backup before migration");
        }
        
        if (steps.Any(s => s.Type == StepType.DropTable))
        {
            analysis.BackupRecommendations.Add("Export data from tables that will be dropped");
        }
        
        return analysis;
    }

    private DataRisk AssessDataRisk(List<DryRunStep> steps)
    {
        if (steps.Any(s => s.Type == StepType.DropTable))
            return DataRisk.Critical;
        
        if (steps.Any(s => s.Type == StepType.DeleteData))
            return DataRisk.High;
        
        if (steps.Any(s => s.Type == StepType.UpdateData))
            return DataRisk.Medium;
        
        if (steps.Any(s => s.Type == StepType.AlterTable))
            return DataRisk.Low;
        
        return DataRisk.None;
    }

    private bool ValidateMigration(DryRunResult result)
    {
        return !result.HasErrors;
    }

    private void GenerateWarnings(DryRunResult result)
    {
        foreach (var step in result.Steps)
        {
            // Warn about potentially destructive operations
            if (step.Type == StepType.DropTable)
            {
                result.Warnings.Add(new DryRunWarning
                {
                    Code = "DESTRUCTIVE_OPERATION",
                    Message = $"Dropping table {string.Join(", ", step.AffectedObjects)} will permanently delete all data",
                    Severity = WarningSeverity.High,
                    Recommendation = "Ensure you have a backup before proceeding",
                    AffectedObjects = step.AffectedObjects
                });
            }
            
            // Warn about long-running operations
            if (step.EstimatedDuration.TotalSeconds > 30)
            {
                result.Warnings.Add(new DryRunWarning
                {
                    Code = "LONG_RUNNING_OPERATION",
                    Message = $"Operation may take {step.EstimatedDuration.TotalSeconds:F1} seconds to complete",
                    Severity = WarningSeverity.Medium,
                    Recommendation = "Consider running during maintenance window",
                    AffectedObjects = step.AffectedObjects
                });
            }
            
            // Warn about operations affecting many rows
            if (step.EstimatedRowsAffected > 10000)
            {
                result.Warnings.Add(new DryRunWarning
                {
                    Code = "HIGH_IMPACT_OPERATION",
                    Message = $"Operation will affect approximately {step.EstimatedRowsAffected:N0} rows",
                    Severity = WarningSeverity.Medium,
                    Recommendation = "Monitor database performance during execution",
                    AffectedObjects = step.AffectedObjects
                });
            }
        }
    }

    // Helper methods for estimation (would be enhanced in real implementation)
    private string ExtractTableName(string statement)
    {
        var match = Regex.Match(statement, @"TABLE\s+(\w+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private long GetEstimatedTableRows(string tableName)
    {
        // In real implementation, this would query pg_stat_user_tables
        return 1000; // Default estimate
    }

    private long EstimateInsertRows(string statement)
    {
        // Count VALUES clauses or estimate from statement complexity
        var valuesCount = Regex.Matches(statement, @"VALUES\s*\(", RegexOptions.IgnoreCase).Count;
        return Math.Max(1, valuesCount);
    }

    private long EstimateUpdateRows(string statement)
    {
        // Simple heuristic - would need table statistics in real implementation
        return statement.ToUpperInvariant().Contains("WHERE") ? 100 : 1000;
    }

    private long EstimateDeleteRows(string statement)
    {
        // Simple heuristic - would need table statistics in real implementation
        return statement.ToUpperInvariant().Contains("WHERE") ? 50 : 1000;
    }
}