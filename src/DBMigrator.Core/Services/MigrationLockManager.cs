using Npgsql;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class MigrationLockManager
{
    private readonly string _connectionString;
    private readonly StructuredLogger _logger;
    private readonly int _lockTimeoutSeconds;
    private string? _currentLockId;

    public MigrationLockManager(string connectionString, StructuredLogger logger, int lockTimeoutSeconds = 300)
    {
        _connectionString = connectionString;
        _logger = logger;
        _lockTimeoutSeconds = lockTimeoutSeconds;
    }

    public async Task<MigrationLock> AcquireLockAsync(string migrationId, string acquiredBy, bool force = false)
    {
        var lockId = Guid.NewGuid().ToString();
        
        await _logger.LogAsync(LogLevel.Info, "Attempting to acquire migration lock", new 
        { 
            LockId = lockId,
            MigrationId = migrationId,
            AcquiredBy = acquiredBy,
            Force = force
        });

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        // Ensure lock table exists
        await CreateLockTableIfNotExistsAsync(connection);
        
        // Clean up expired locks first
        await CleanupExpiredLocksAsync(connection);
        
        // Check for existing locks
        var existingLock = await GetExistingLockAsync(connection);
        
        if (existingLock != null && !force)
        {
            throw new MigrationLockException($"Migration lock already acquired by {existingLock.AcquiredBy} at {existingLock.AcquiredAt}. Use --force to override.");
        }
        
        if (existingLock != null && force)
        {
            await _logger.LogAsync(LogLevel.Warning, "Force releasing existing lock", new 
            { 
                ExistingLock = existingLock.LockId,
                ExistingOwner = existingLock.AcquiredBy
            });
            
            await ForceReleaseLockAsync(connection, existingLock.LockId);
        }
        
        // Acquire new lock
        var migrationLock = new MigrationLock
        {
            LockId = lockId,
            MigrationId = migrationId,
            AcquiredBy = acquiredBy,
            AcquiredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(_lockTimeoutSeconds),
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId
        };
        
        await InsertLockAsync(connection, migrationLock);
        
        _currentLockId = lockId;
        
        await _logger.LogAsync(LogLevel.Info, "Migration lock acquired successfully", new 
        { 
            LockId = lockId,
            ExpiresAt = migrationLock.ExpiresAt
        });
        
        return migrationLock;
    }

    public async Task ReleaseLockAsync(string lockId)
    {
        await _logger.LogAsync(LogLevel.Info, "Releasing migration lock", new { LockId = lockId });
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var sql = @"
            UPDATE __dbmigrator_locks 
            SET released_at = CURRENT_TIMESTAMP,
                status = 'RELEASED'
            WHERE lock_id = @lockId AND released_at IS NULL";
        
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("lockId", lockId);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        
        if (rowsAffected > 0)
        {
            await _logger.LogAsync(LogLevel.Info, "Migration lock released successfully", new { LockId = lockId });
        }
        else
        {
            await _logger.LogAsync(LogLevel.Warning, "Lock not found or already released", new { LockId = lockId });
        }
        
        if (_currentLockId == lockId)
        {
            _currentLockId = null;
        }
    }

    public async Task<MigrationLock?> GetCurrentLockAsync()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await CreateLockTableIfNotExistsAsync(connection);
        
        return await GetExistingLockAsync(connection);
    }

    public async Task ForceReleaseAllLocksAsync(string releasedBy)
    {
        await _logger.LogAsync(LogLevel.Warning, "Force releasing all migration locks", new { ReleasedBy = releasedBy });
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var sql = @"
            UPDATE __dbmigrator_locks 
            SET released_at = CURRENT_TIMESTAMP,
                status = 'FORCE_RELEASED',
                released_by = @releasedBy
            WHERE released_at IS NULL";
        
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("releasedBy", releasedBy);
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        
        await _logger.LogAsync(LogLevel.Warning, "Force released migration locks", new 
        { 
            LocksReleased = rowsAffected,
            ReleasedBy = releasedBy
        });
    }

    private async Task CreateLockTableIfNotExistsAsync(NpgsqlConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS __dbmigrator_locks (
                lock_id VARCHAR(50) PRIMARY KEY,
                migration_id VARCHAR(255),
                acquired_by VARCHAR(255) NOT NULL,
                acquired_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                expires_at TIMESTAMP NOT NULL,
                released_at TIMESTAMP NULL,
                released_by VARCHAR(255) NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
                machine_name VARCHAR(255),
                process_id INTEGER,
                metadata JSONB
            );

            CREATE INDEX IF NOT EXISTS idx_dbmigrator_locks_status 
            ON __dbmigrator_locks(status) 
            WHERE released_at IS NULL;

            CREATE INDEX IF NOT EXISTS idx_dbmigrator_locks_expires 
            ON __dbmigrator_locks(expires_at) 
            WHERE released_at IS NULL;";

        using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<MigrationLock?> GetExistingLockAsync(NpgsqlConnection connection)
    {
        var sql = @"
            SELECT lock_id, migration_id, acquired_by, acquired_at, expires_at, 
                   machine_name, process_id
            FROM __dbmigrator_locks 
            WHERE released_at IS NULL 
              AND expires_at > CURRENT_TIMESTAMP
            ORDER BY acquired_at DESC
            LIMIT 1";

        using var command = new NpgsqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return new MigrationLock
            {
                LockId = reader.GetString(reader.GetOrdinal("lock_id")),
                MigrationId = reader.IsDBNull(reader.GetOrdinal("migration_id")) ? null : reader.GetString(reader.GetOrdinal("migration_id")),
                AcquiredBy = reader.GetString(reader.GetOrdinal("acquired_by")),
                AcquiredAt = reader.GetDateTime(reader.GetOrdinal("acquired_at")),
                ExpiresAt = reader.GetDateTime(reader.GetOrdinal("expires_at")),
                MachineName = reader.IsDBNull(reader.GetOrdinal("machine_name")) ? null : reader.GetString(reader.GetOrdinal("machine_name")),
                ProcessId = reader.IsDBNull(reader.GetOrdinal("process_id")) ? null : reader.GetInt32(reader.GetOrdinal("process_id"))
            };
        }
        
        return null;
    }

    private async Task CleanupExpiredLocksAsync(NpgsqlConnection connection)
    {
        var sql = @"
            UPDATE __dbmigrator_locks 
            SET released_at = CURRENT_TIMESTAMP,
                status = 'EXPIRED',
                released_by = 'SYSTEM_CLEANUP'
            WHERE released_at IS NULL 
              AND expires_at <= CURRENT_TIMESTAMP";

        using var command = new NpgsqlCommand(sql, connection);
        var expiredLocks = await command.ExecuteNonQueryAsync();
        
        if (expiredLocks > 0)
        {
            await _logger.LogAsync(LogLevel.Info, "Cleaned up expired locks", new { ExpiredLocks = expiredLocks });
        }
    }

    private async Task InsertLockAsync(NpgsqlConnection connection, MigrationLock migrationLock)
    {
        var sql = @"
            INSERT INTO __dbmigrator_locks 
            (lock_id, migration_id, acquired_by, acquired_at, expires_at, machine_name, process_id, status)
            VALUES (@lockId, @migrationId, @acquiredBy, @acquiredAt, @expiresAt, @machineName, @processId, 'ACTIVE')";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("lockId", migrationLock.LockId);
        command.Parameters.AddWithValue("migrationId", migrationLock.MigrationId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("acquiredBy", migrationLock.AcquiredBy);
        command.Parameters.AddWithValue("acquiredAt", migrationLock.AcquiredAt);
        command.Parameters.AddWithValue("expiresAt", migrationLock.ExpiresAt);
        command.Parameters.AddWithValue("machineName", migrationLock.MachineName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("processId", migrationLock.ProcessId ?? (object)DBNull.Value);
        
        await command.ExecuteNonQueryAsync();
    }

    private async Task ForceReleaseLockAsync(NpgsqlConnection connection, string lockId)
    {
        var sql = @"
            UPDATE __dbmigrator_locks 
            SET released_at = CURRENT_TIMESTAMP,
                status = 'FORCE_RELEASED',
                released_by = 'FORCE_OVERRIDE'
            WHERE lock_id = @lockId";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("lockId", lockId);
        
        await command.ExecuteNonQueryAsync();
    }
}

public class MigrationLock
{
    public string LockId { get; set; } = string.Empty;
    public string? MigrationId { get; set; }
    public string AcquiredBy { get; set; } = string.Empty;
    public DateTime AcquiredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? MachineName { get; set; }
    public int? ProcessId { get; set; }
}

public class MigrationLockException : Exception
{
    public MigrationLockException(string message) : base(message) { }
    public MigrationLockException(string message, Exception innerException) : base(message, innerException) { }
}