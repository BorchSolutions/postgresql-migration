using Npgsql;
using System.Text.RegularExpressions;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class MigrationValidator
{
    private readonly string _connectionString;
    private readonly StructuredLogger _logger;
    private readonly ValidationConfiguration _config;

    // Dangerous SQL patterns that should be flagged
    private static readonly Dictionary<string, ValidationRule> DangerousPatterns = new()
    {
        ["DROP_DATABASE"] = new ValidationRule
        {
            Pattern = @"\bDROP\s+DATABASE\b",
            Severity = ValidationSeverity.Critical,
            Message = "DROP DATABASE statements are not allowed in migrations"
        },
        ["TRUNCATE_TABLE"] = new ValidationRule
        {
            Pattern = @"\bTRUNCATE\s+TABLE\b",
            Severity = ValidationSeverity.High,
            Message = "TRUNCATE TABLE can cause data loss"
        },
        ["DELETE_WITHOUT_WHERE"] = new ValidationRule
        {
            Pattern = @"\bDELETE\s+FROM\s+\w+\s*(?!.*WHERE)",
            Severity = ValidationSeverity.Critical,
            Message = "DELETE without WHERE clause will remove all data"
        },
        ["UPDATE_WITHOUT_WHERE"] = new ValidationRule
        {
            Pattern = @"\bUPDATE\s+\w+\s+SET\s+.*?(?!.*WHERE)",
            Severity = ValidationSeverity.High,
            Message = "UPDATE without WHERE clause will modify all rows"
        },
        ["DROP_TABLE"] = new ValidationRule
        {
            Pattern = @"\bDROP\s+TABLE\b",
            Severity = ValidationSeverity.Medium,
            Message = "DROP TABLE will permanently delete table and all data"
        },
        ["ALTER_COLUMN_TYPE"] = new ValidationRule
        {
            Pattern = @"\bALTER\s+TABLE\s+\w+\s+ALTER\s+COLUMN\s+\w+\s+TYPE\b",
            Severity = ValidationSeverity.Medium,
            Message = "Changing column type may cause data loss or conversion errors"
        },
        ["GRANT_ALL"] = new ValidationRule
        {
            Pattern = @"\bGRANT\s+ALL\b",
            Severity = ValidationSeverity.Medium,
            Message = "GRANT ALL provides extensive permissions"
        }
    };

    public MigrationValidator(string connectionString, ValidationConfiguration config, StructuredLogger logger)
    {
        _connectionString = connectionString;
        _config = config;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateMigrationAsync(string migrationContent, string migrationId)
    {
        await _logger.LogAsync(LogLevel.Info, "Starting migration validation", new { MigrationId = migrationId });

        var result = new ValidationResult
        {
            MigrationId = migrationId,
            IsValid = true,
            ValidatedAt = DateTime.UtcNow
        };

        try
        {
            // 1. Syntax validation
            await ValidateSyntaxAsync(migrationContent, result);

            // 2. Pattern validation  
            ValidateDangerousPatterns(migrationContent, result);

            // 3. Schema validation
            if (_config.ValidateBeforeApply)
            {
                await ValidateSchemaChangesAsync(migrationContent, result);
            }

            // 4. Dependency validation
            await ValidateDependenciesAsync(migrationContent, result);

            // 5. Transaction validation
            ValidateTransactionStructure(migrationContent, result);

            // 6. Performance validation
            ValidatePerformanceImpact(migrationContent, result);

            // Determine overall validity
            result.IsValid = !result.Errors.Any() && 
                           (!result.Warnings.Any(w => w.Severity == ValidationSeverity.Critical));

            await _logger.LogAsync(LogLevel.Info, "Migration validation completed", new 
            { 
                MigrationId = migrationId,
                IsValid = result.IsValid,
                ErrorsCount = result.Errors.Count,
                WarningsCount = result.Warnings.Count
            });

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError
            {
                Code = "VALIDATION_EXCEPTION",
                Message = $"Validation failed with exception: {ex.Message}",
                Severity = ValidationSeverity.Critical,
                Line = 0
            });

            await _logger.LogAsync(LogLevel.Error, "Migration validation failed", new 
            { 
                MigrationId = migrationId,
                Error = ex.Message
            }, ex);

            return result;
        }
    }

    private async Task ValidateSyntaxAsync(string migrationContent, ValidationResult result)
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Split into individual statements
            var statements = SplitIntoStatements(migrationContent);
            
            foreach (var (statement, lineNumber) in statements)
            {
                if (string.IsNullOrWhiteSpace(statement)) continue;

                try
                {
                    // Use EXPLAIN to validate syntax without executing
                    var explainQuery = $"EXPLAIN (FORMAT JSON) {statement}";
                    using var command = new NpgsqlCommand(explainQuery, connection);
                    command.CommandTimeout = 30; // Short timeout for validation
                    
                    await command.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "SYNTAX_ERROR",
                        Message = $"SQL syntax error: {pgEx.MessageText}",
                        Severity = ValidationSeverity.Critical,
                        Line = lineNumber,
                        SqlState = pgEx.SqlState
                    });
                }
                catch (Exception ex)
                {
                    // Some statements can't be explained (like DDL), check differently
                    if (!IsDDLStatement(statement))
                    {
                        result.Warnings.Add(new ValidationWarning
                        {
                            Code = "SYNTAX_CHECK_FAILED",
                            Message = $"Could not validate syntax: {ex.Message}",
                            Severity = ValidationSeverity.Low,
                            Line = lineNumber
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "CONNECTION_ERROR",
                Message = $"Could not connect to database for validation: {ex.Message}",
                Severity = ValidationSeverity.High,
                Line = 0
            });
        }
    }

    private void ValidateDangerousPatterns(string migrationContent, ValidationResult result)
    {
        var lines = migrationContent.Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            foreach (var (ruleKey, rule) in DangerousPatterns)
            {
                if (Regex.IsMatch(line, rule.Pattern, RegexOptions.IgnoreCase))
                {
                    if (rule.Severity == ValidationSeverity.Critical)
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Code = ruleKey,
                            Message = rule.Message,
                            Severity = rule.Severity,
                            Line = lineNumber
                        });
                    }
                    else
                    {
                        result.Warnings.Add(new ValidationWarning
                        {
                            Code = ruleKey,
                            Message = rule.Message,
                            Severity = rule.Severity,
                            Line = lineNumber
                        });
                    }
                }
            }
        }
    }

    private async Task ValidateSchemaChangesAsync(string migrationContent, ValidationResult result)
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check for schema conflicts
            if (migrationContent.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
            {
                await ValidateTableCreationAsync(migrationContent, connection, result);
            }

            if (migrationContent.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
            {
                await ValidateTableAlterationsAsync(migrationContent, connection, result);
            }

            if (migrationContent.Contains("CREATE INDEX", StringComparison.OrdinalIgnoreCase))
            {
                await ValidateIndexCreationAsync(migrationContent, connection, result);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add(new ValidationWarning
            {
                Code = "SCHEMA_VALIDATION_ERROR",
                Message = $"Could not validate schema changes: {ex.Message}",
                Severity = ValidationSeverity.Medium,
                Line = 0
            });
        }
    }

    private async Task ValidateTableCreationAsync(string migrationContent, NpgsqlConnection connection, ValidationResult result)
    {
        var tableMatches = Regex.Matches(migrationContent, @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(\w+)", RegexOptions.IgnoreCase);
        
        foreach (Match match in tableMatches)
        {
            var tableName = match.Groups[1].Value;
            
            // Check if table already exists
            var checkSql = @"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables 
                    WHERE table_name = @tableName 
                    AND table_schema = 'public'
                )";
            
            using var command = new NpgsqlCommand(checkSql, connection);
            command.Parameters.AddWithValue("tableName", tableName);
            
            var exists = (bool)await command.ExecuteScalarAsync()!;
            
            if (exists && !match.Value.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "TABLE_EXISTS",
                    Message = $"Table '{tableName}' already exists. Consider using 'IF NOT EXISTS'",
                    Severity = ValidationSeverity.Medium,
                    Line = GetLineNumber(migrationContent, match.Index)
                });
            }
        }
    }

    private async Task ValidateTableAlterationsAsync(string migrationContent, NpgsqlConnection connection, ValidationResult result)
    {
        var alterMatches = Regex.Matches(migrationContent, @"ALTER\s+TABLE\s+(\w+)", RegexOptions.IgnoreCase);
        
        foreach (Match match in alterMatches)
        {
            var tableName = match.Groups[1].Value;
            
            // Check if table exists
            var checkSql = @"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables 
                    WHERE table_name = @tableName 
                    AND table_schema = 'public'
                )";
            
            using var command = new NpgsqlCommand(checkSql, connection);
            command.Parameters.AddWithValue("tableName", tableName);
            
            var exists = (bool)await command.ExecuteScalarAsync()!;
            
            if (!exists)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "TABLE_NOT_EXISTS",
                    Message = $"Cannot alter table '{tableName}': table does not exist",
                    Severity = ValidationSeverity.Critical,
                    Line = GetLineNumber(migrationContent, match.Index)
                });
            }
        }
    }

    private async Task ValidateIndexCreationAsync(string migrationContent, NpgsqlConnection connection, ValidationResult result)
    {
        var indexMatches = Regex.Matches(migrationContent, @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?(\w+)", RegexOptions.IgnoreCase);
        
        foreach (Match match in indexMatches)
        {
            var indexName = match.Groups[1].Value;
            
            // Check if index already exists
            var checkSql = @"
                SELECT EXISTS (
                    SELECT 1 FROM pg_indexes 
                    WHERE indexname = @indexName
                )";
            
            using var command = new NpgsqlCommand(checkSql, connection);
            command.Parameters.AddWithValue("indexName", indexName);
            
            var exists = (bool)await command.ExecuteScalarAsync()!;
            
            if (exists && !match.Value.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "INDEX_EXISTS",
                    Message = $"Index '{indexName}' already exists. Consider using 'IF NOT EXISTS'",
                    Severity = ValidationSeverity.Medium,
                    Line = GetLineNumber(migrationContent, match.Index)
                });
            }
        }
    }

    private async Task ValidateDependenciesAsync(string migrationContent, ValidationResult result)
    {
        // Extract dependency comments
        var dependencyPattern = @"--\s*@depends:\s*(.+)";
        var matches = Regex.Matches(migrationContent, dependencyPattern, RegexOptions.IgnoreCase);
        
        foreach (Match match in matches)
        {
            var dependency = match.Groups[1].Value.Trim();
            // TODO: Check if dependency migration exists and has been applied
            // This would require checking the migration history
            
            await _logger.LogAsync(LogLevel.Debug, "Found migration dependency", new 
            { 
                Dependency = dependency,
                Line = GetLineNumber(migrationContent, match.Index)
            });
        }
    }

    private void ValidateTransactionStructure(string migrationContent, ValidationResult result)
    {
        var hasBegin = migrationContent.Contains("BEGIN", StringComparison.OrdinalIgnoreCase);
        var hasCommit = migrationContent.Contains("COMMIT", StringComparison.OrdinalIgnoreCase);
        var hasRollback = migrationContent.Contains("ROLLBACK", StringComparison.OrdinalIgnoreCase);
        
        if (hasBegin && !hasCommit && !hasRollback)
        {
            result.Warnings.Add(new ValidationWarning
            {
                Code = "INCOMPLETE_TRANSACTION",
                Message = "Transaction started with BEGIN but no COMMIT or ROLLBACK found",
                Severity = ValidationSeverity.Medium,
                Line = 0
            });
        }
        
        if ((hasCommit || hasRollback) && !hasBegin)
        {
            result.Warnings.Add(new ValidationWarning
            {
                Code = "ORPHAN_TRANSACTION_END",
                Message = "COMMIT or ROLLBACK found without corresponding BEGIN",
                Severity = ValidationSeverity.Medium,
                Line = 0
            });
        }
    }

    private void ValidatePerformanceImpact(string migrationContent, ValidationResult result)
    {
        // Check for potentially slow operations
        var slowOperations = new[]
        {
            (@"\bCREATE\s+INDEX\s+(?!CONCURRENTLY)", "Creating index without CONCURRENTLY can lock table"),
            (@"\bALTER\s+TABLE\s+\w+\s+ADD\s+CONSTRAINT", "Adding constraints can be slow on large tables"),
            (@"\bVACUUM\s+FULL", "VACUUM FULL requires exclusive lock"),
            (@"\bREINDEX", "REINDEX requires exclusive lock")
        };
        
        foreach (var (pattern, message) in slowOperations)
        {
            var matches = Regex.Matches(migrationContent, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "PERFORMANCE_IMPACT",
                    Message = message,
                    Severity = ValidationSeverity.Low,
                    Line = GetLineNumber(migrationContent, match.Index)
                });
            }
        }
    }

    private List<(string statement, int lineNumber)> SplitIntoStatements(string content)
    {
        var statements = new List<(string, int)>();
        var lines = content.Split('\n');
        var currentStatement = "";
        var startLine = 1;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith("--"))
                continue;
            
            if (string.IsNullOrEmpty(currentStatement))
                startLine = i + 1;
            
            currentStatement += line + " ";
            
            if (line.EndsWith(";"))
            {
                statements.Add((currentStatement.Trim(), startLine));
                currentStatement = "";
            }
        }
        
        // Add any remaining statement
        if (!string.IsNullOrEmpty(currentStatement.Trim()))
        {
            statements.Add((currentStatement.Trim(), startLine));
        }
        
        return statements;
    }

    private bool IsDDLStatement(string statement)
    {
        var ddlKeywords = new[] { "CREATE", "ALTER", "DROP", "TRUNCATE" };
        return ddlKeywords.Any(keyword => 
            statement.TrimStart().StartsWith(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private int GetLineNumber(string content, int index)
    {
        return content.Substring(0, index).Count(c => c == '\n') + 1;
    }
}

public class ValidationResult
{
    public string MigrationId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public DateTime ValidatedAt { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    
    public bool HasCriticalIssues => Errors.Any() || Warnings.Any(w => w.Severity == ValidationSeverity.Critical);
}

public class ValidationError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; }
    public int Line { get; set; }
    public string? SqlState { get; set; }
}

public class ValidationWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; }
    public int Line { get; set; }
}

public class ValidationRule
{
    public string Pattern { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum ValidationSeverity
{
    Low,
    Medium,
    High, 
    Critical
}