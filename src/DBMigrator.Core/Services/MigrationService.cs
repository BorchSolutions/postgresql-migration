using System.Security.Cryptography;
using System.Text;
using DBMigrator.Core.Database;
using DBMigrator.Core.Models;
using Npgsql;

namespace DBMigrator.Core.Services;

public class MigrationService
{
    private readonly ConnectionManager _connectionManager;
    private readonly StructuredLogger _logger;

    public MigrationService(string connectionString)
    {
        _connectionManager = new ConnectionManager(connectionString);
        _logger = new StructuredLogger("Info", true); // Default logger
    }

    public MigrationService(string connectionString, StructuredLogger logger)
    {
        _connectionManager = new ConnectionManager(connectionString);
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS __migrations (
                id SERIAL PRIMARY KEY,
                migration_id VARCHAR(255) NOT NULL UNIQUE,
                filename VARCHAR(255) NOT NULL,
                checksum VARCHAR(64),
                applied_at TIMESTAMP NOT NULL DEFAULT NOW(),
                applied_by VARCHAR(100)
            )";

        using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        
        using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();

        Console.WriteLine("✅ Migration history table created successfully");
    }

    public async Task ApplyMigrationAsync(string filename)
    {
        if (!File.Exists(filename))
        {
            throw new FileNotFoundException($"Migration file not found: {filename}");
        }

        var content = await File.ReadAllTextAsync(filename);
        var checksum = CalculateChecksum(content);
        var migrationId = Path.GetFileNameWithoutExtension(filename);

        using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            // Check if already applied
            var checkSql = "SELECT COUNT(*) FROM __migrations WHERE migration_id = @migrationId";
            using var checkCommand = new NpgsqlCommand(checkSql, connection, transaction);
            checkCommand.Parameters.AddWithValue("migrationId", migrationId);
            
            var count = (long)(await checkCommand.ExecuteScalarAsync() ?? 0L);
            if (count > 0)
            {
                Console.WriteLine($"⚠️  Migration {migrationId} already applied");
                return;
            }

            // Validate SQL content before execution
            await ValidateSqlContentAsync(content, filename);

            // Execute migration SQL
            using var migrationCommand = new NpgsqlCommand(content, connection, transaction);
            var affectedRows = await migrationCommand.ExecuteNonQueryAsync();

            // Record in history
            var recordSql = @"
                INSERT INTO __migrations (migration_id, filename, checksum, applied_by) 
                VALUES (@migrationId, @filename, @checksum, @appliedBy)";
            
            using var recordCommand = new NpgsqlCommand(recordSql, connection, transaction);
            recordCommand.Parameters.AddWithValue("migrationId", migrationId);
            recordCommand.Parameters.AddWithValue("filename", filename);
            recordCommand.Parameters.AddWithValue("checksum", checksum);
            recordCommand.Parameters.AddWithValue("appliedBy", Environment.UserName);

            await recordCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            Console.WriteLine($"✅ Migration {migrationId} applied successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException($"Failed to apply migration {migrationId}: {ex.Message}", ex);
        }
    }

    public async Task ShowStatusAsync()
    {
        using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT migration_id, filename, applied_at, applied_by 
            FROM __migrations 
            ORDER BY applied_at";

        using var command = new NpgsqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        Console.WriteLine("\n📊 Applied Migrations:");
        Console.WriteLine("----------------------------------------");

        var hasResults = false;
        while (await reader.ReadAsync())
        {
            hasResults = true;
            var migrationId = reader.GetString(0);
            var filename = reader.GetString(1);
            var appliedAt = reader.GetDateTime(2);
            var appliedBy = reader.GetString(3);

            Console.WriteLine($"- {migrationId}");
            Console.WriteLine($"  File: {filename}");
            Console.WriteLine($"  Applied: {appliedAt:yyyy-MM-dd HH:mm:ss} by {appliedBy}");
            Console.WriteLine();
        }

        if (!hasResults)
        {
            Console.WriteLine("No migrations applied yet.");
        }
    }

    private async Task ValidateSqlContentAsync(string content, string filename)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"Migration file '{filename}' is empty or contains only whitespace");

        // Basic SQL injection prevention - check for dangerous patterns
        var dangerousPatterns = new[]
        {
            // Command execution attempts
            @"\b(xp_cmdshell|sp_execute|exec|execute)\s*\(",
            // File system operations
            @"\b(bulk\s+insert|openrowset|opendatasource)\b",
            // System function calls
            @"\b(sys\.|information_schema\.)",
            // Multiple statement separators that might indicate injection
            @";\s*(drop|delete|truncate|update|insert)\s+",
            // SQL comments that might hide malicious code
            @"/\*.*?(drop|delete|truncate).*?\*/",
            @"--.*?(drop|delete|truncate)"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(content, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | 
                System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                await _logger.LogAsync(LogLevel.Warning, 
                    $"Potentially dangerous SQL pattern detected in migration '{filename}'. Pattern: {pattern}");
                
                // In production, you might want to throw an exception here
                // throw new SecurityException($"Dangerous SQL pattern detected in migration '{filename}'");
            }
        }

        // Check for basic syntax issues
        if (content.Count(c => c == '(') != content.Count(c => c == ')'))
        {
            throw new InvalidOperationException($"Migration '{filename}' has unmatched parentheses");
        }

        if (content.Count(c => c == '\'') % 2 != 0)
        {
            throw new InvalidOperationException($"Migration '{filename}' has unmatched single quotes");
        }

        await _logger.LogAsync(LogLevel.Debug, $"SQL content validation passed for migration '{filename}'");
    }

    private static string CalculateChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }
}