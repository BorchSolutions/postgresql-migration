using System.Security.Cryptography;
using System.Text;
using Npgsql;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class ChecksumManager
{
    private readonly string _connectionString;
    private readonly StructuredLogger _logger;

    public ChecksumManager(string connectionString, StructuredLogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<string> CalculateChecksumAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return CalculateChecksum(content);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(LogLevel.Error, "Failed to calculate checksum for file", new 
            { 
                FilePath = filePath,
                Error = ex.Message
            }, ex);
            throw;
        }
    }

    public string CalculateChecksum(string content)
    {
        // Normalize content (remove Windows line endings, trim whitespace)
        var normalizedContent = content
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedContent));
        return Convert.ToBase64String(hash);
    }

    public async Task<ChecksumVerificationResult> VerifyMigrationChecksumAsync(string migrationId, string currentChecksum)
    {
        await _logger.LogAsync(LogLevel.Debug, "Verifying migration checksum", new 
        { 
            MigrationId = migrationId,
            CurrentChecksum = currentChecksum[..8] + "..."
        });

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await CreateChecksumTableIfNotExistsAsync(connection);

        var storedChecksum = await GetStoredChecksumAsync(connection, migrationId);

        var result = new ChecksumVerificationResult
        {
            MigrationId = migrationId,
            CurrentChecksum = currentChecksum,
            StoredChecksum = storedChecksum,
            IsValid = storedChecksum == null || storedChecksum == currentChecksum,
            VerifiedAt = DateTime.UtcNow
        };

        if (storedChecksum == null)
        {
            result.Status = ChecksumStatus.NotFound;
            await _logger.LogAsync(LogLevel.Debug, "No stored checksum found for migration", new { MigrationId = migrationId });
        }
        else if (storedChecksum == currentChecksum)
        {
            result.Status = ChecksumStatus.Valid;
            await _logger.LogAsync(LogLevel.Debug, "Checksum verification passed", new { MigrationId = migrationId });
        }
        else
        {
            result.Status = ChecksumStatus.Mismatch;
            await _logger.LogAsync(LogLevel.Warning, "Checksum mismatch detected", new 
            { 
                MigrationId = migrationId,
                StoredChecksum = storedChecksum[..8] + "...",
                CurrentChecksum = currentChecksum[..8] + "..."
            });
        }

        return result;
    }

    public async Task StoreChecksumAsync(string migrationId, string checksum, string filePath)
    {
        await _logger.LogAsync(LogLevel.Debug, "Storing migration checksum", new 
        { 
            MigrationId = migrationId,
            Checksum = checksum[..8] + "...",
            FilePath = filePath
        });

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await CreateChecksumTableIfNotExistsAsync(connection);

        var sql = @"
            INSERT INTO __dbmigrator_checksums 
            (migration_id, checksum, file_path, created_at, updated_at)
            VALUES (@migrationId, @checksum, @filePath, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (migration_id) 
            DO UPDATE SET 
                checksum = EXCLUDED.checksum,
                file_path = EXCLUDED.file_path,
                updated_at = CURRENT_TIMESTAMP";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migrationId", migrationId);
        command.Parameters.AddWithValue("checksum", checksum);
        command.Parameters.AddWithValue("filePath", filePath);

        await command.ExecuteNonQueryAsync();

        await _logger.LogAsync(LogLevel.Debug, "Migration checksum stored successfully", new { MigrationId = migrationId });
    }

    public async Task<List<ChecksumMismatch>> VerifyAllChecksumsAsync(string migrationsPath)
    {
        await _logger.LogAsync(LogLevel.Info, "Starting comprehensive checksum verification", new { MigrationsPath = migrationsPath });

        var mismatches = new List<ChecksumMismatch>();

        if (!Directory.Exists(migrationsPath))
        {
            await _logger.LogAsync(LogLevel.Warning, "Migrations directory not found", new { MigrationsPath = migrationsPath });
            return mismatches;
        }

        var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("."))
            .OrderBy(f => f);

        foreach (var filePath in migrationFiles)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var migrationId = Path.GetFileNameWithoutExtension(fileName);
                
                var currentChecksum = await CalculateChecksumAsync(filePath);
                var verification = await VerifyMigrationChecksumAsync(migrationId, currentChecksum);

                if (verification.Status == ChecksumStatus.Mismatch)
                {
                    mismatches.Add(new ChecksumMismatch
                    {
                        MigrationId = migrationId,
                        FilePath = filePath,
                        StoredChecksum = verification.StoredChecksum!,
                        CurrentChecksum = verification.CurrentChecksum,
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.LogAsync(LogLevel.Error, "Failed to verify checksum for migration file", new 
                { 
                    FilePath = filePath,
                    Error = ex.Message
                }, ex);

                mismatches.Add(new ChecksumMismatch
                {
                    MigrationId = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath,
                    StoredChecksum = "",
                    CurrentChecksum = "",
                    DetectedAt = DateTime.UtcNow,
                    Error = ex.Message
                });
            }
        }

        await _logger.LogAsync(LogLevel.Info, "Checksum verification completed", new 
        { 
            TotalFiles = migrationFiles.Count(),
            Mismatches = mismatches.Count
        });

        return mismatches;
    }

    public async Task<ChecksumRepairResult> RepairChecksumAsync(string migrationId, bool force = false)
    {
        await _logger.LogAsync(LogLevel.Info, "Starting checksum repair", new 
        { 
            MigrationId = migrationId,
            Force = force
        });

        var result = new ChecksumRepairResult
        {
            MigrationId = migrationId,
            Success = false,
            RepairedAt = DateTime.UtcNow
        };

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if migration was ever applied
            var wasApplied = await WasMigrationAppliedAsync(connection, migrationId);
            
            if (!wasApplied && !force)
            {
                result.Error = "Migration was never applied. Use --force to repair checksum anyway.";
                return result;
            }

            // Find the migration file
            var migrationFile = await FindMigrationFileAsync(migrationId);
            if (migrationFile == null)
            {
                result.Error = $"Migration file for '{migrationId}' not found.";
                return result;
            }

            // Calculate current checksum
            var currentChecksum = await CalculateChecksumAsync(migrationFile);
            
            // Get stored checksum for comparison
            var storedChecksum = await GetStoredChecksumAsync(connection, migrationId);
            
            result.OldChecksum = storedChecksum;
            result.NewChecksum = currentChecksum;

            if (storedChecksum == currentChecksum)
            {
                result.Success = true;
                result.Message = "Checksum is already correct, no repair needed.";
                return result;
            }

            // Update the stored checksum
            await StoreChecksumAsync(migrationId, currentChecksum, migrationFile);

            // Log the repair action
            await LogChecksumRepairAsync(connection, migrationId, storedChecksum, currentChecksum, force);

            result.Success = true;
            result.Message = "Checksum repaired successfully.";

            await _logger.LogAsync(LogLevel.Info, "Checksum repair completed", new 
            { 
                MigrationId = migrationId,
                OldChecksum = storedChecksum?[..8] + "...",
                NewChecksum = currentChecksum[..8] + "..."
            });

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            
            await _logger.LogAsync(LogLevel.Error, "Checksum repair failed", new 
            { 
                MigrationId = migrationId,
                Error = ex.Message
            }, ex);

            return result;
        }
    }

    public async Task<List<ChecksumInfo>> GetAllChecksumsAsync()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await CreateChecksumTableIfNotExistsAsync(connection);

        var sql = @"
            SELECT migration_id, checksum, file_path, created_at, updated_at
            FROM __dbmigrator_checksums
            ORDER BY created_at";

        var checksums = new List<ChecksumInfo>();

        using var command = new NpgsqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            checksums.Add(new ChecksumInfo
            {
                MigrationId = reader.GetString(reader.GetOrdinal("migration_id")),
                Checksum = reader.GetString(reader.GetOrdinal("checksum")),
                FilePath = reader.IsDBNull(reader.GetOrdinal("file_path")) ? null : reader.GetString(reader.GetOrdinal("file_path")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            });
        }

        return checksums;
    }

    private async Task CreateChecksumTableIfNotExistsAsync(NpgsqlConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS __dbmigrator_checksums (
                id SERIAL PRIMARY KEY,
                migration_id VARCHAR(255) UNIQUE NOT NULL,
                checksum VARCHAR(255) NOT NULL,
                file_path VARCHAR(1000),
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_dbmigrator_checksums_migration 
            ON __dbmigrator_checksums(migration_id);

            -- Table for checksum repair history
            CREATE TABLE IF NOT EXISTS __dbmigrator_checksum_repairs (
                id SERIAL PRIMARY KEY,
                migration_id VARCHAR(255) NOT NULL,
                old_checksum VARCHAR(255),
                new_checksum VARCHAR(255) NOT NULL,
                repaired_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                repaired_by VARCHAR(255),
                forced BOOLEAN DEFAULT FALSE,
                reason TEXT
            );";

        using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> GetStoredChecksumAsync(NpgsqlConnection connection, string migrationId)
    {
        var sql = "SELECT checksum FROM __dbmigrator_checksums WHERE migration_id = @migrationId";
        
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migrationId", migrationId);
        
        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    private async Task<bool> WasMigrationAppliedAsync(NpgsqlConnection connection, string migrationId)
    {
        try
        {
            var sql = @"
                SELECT EXISTS (
                    SELECT 1 FROM __dbmigrator_schema_migrations 
                    WHERE migration_id = @migrationId
                )";
            
            using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("migrationId", migrationId);
            
            var result = await command.ExecuteScalarAsync();
            return result != null && (bool)result;
        }
        catch
        {
            // If the migration table doesn't exist, assume not applied
            return false;
        }
    }

    private async Task<string?> FindMigrationFileAsync(string migrationId)
    {
        // Look in common migration directories
        var searchPaths = new[]
        {
            "./migrations",
            "./db/migrations", 
            "./sql/migrations",
            "./database/migrations"
        };

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath)) continue;

            var files = Directory.GetFiles(searchPath, "*.sql", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetFileNameWithoutExtension(f) == migrationId);

            var file = files.FirstOrDefault();
            if (file != null)
                return file;
        }

        return null;
    }

    private async Task LogChecksumRepairAsync(NpgsqlConnection connection, string migrationId, 
        string? oldChecksum, string newChecksum, bool forced)
    {
        var sql = @"
            INSERT INTO __dbmigrator_checksum_repairs 
            (migration_id, old_checksum, new_checksum, repaired_by, forced, reason)
            VALUES (@migrationId, @oldChecksum, @newChecksum, @repairedBy, @forced, @reason)";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migrationId", migrationId);
        command.Parameters.AddWithValue("oldChecksum", oldChecksum ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("newChecksum", newChecksum);
        command.Parameters.AddWithValue("repairedBy", Environment.UserName);
        command.Parameters.AddWithValue("forced", forced);
        command.Parameters.AddWithValue("reason", forced ? "Forced repair" : "Automatic repair");

        await command.ExecuteNonQueryAsync();
    }
}

public class ChecksumVerificationResult
{
    public string MigrationId { get; set; } = string.Empty;
    public string CurrentChecksum { get; set; } = string.Empty;
    public string? StoredChecksum { get; set; }
    public bool IsValid { get; set; }
    public ChecksumStatus Status { get; set; }
    public DateTime VerifiedAt { get; set; }
}

public class ChecksumMismatch
{
    public string MigrationId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string StoredChecksum { get; set; } = string.Empty;
    public string CurrentChecksum { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string? Error { get; set; }
}

public class ChecksumRepairResult
{
    public string MigrationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? OldChecksum { get; set; }
    public string? NewChecksum { get; set; }
    public DateTime RepairedAt { get; set; }
}

public class ChecksumInfo
{
    public string MigrationId { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum ChecksumStatus
{
    Valid,
    Mismatch,
    NotFound
}