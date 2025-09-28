using Npgsql;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class TransactionManager : IDisposable
{
    private readonly string _connectionString;
    private readonly StructuredLogger _logger;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private readonly Stack<string> _savepoints = new();
    private readonly string _migrationId;
    private bool _disposed = false;

    public TransactionManager(string connectionString, string migrationId, StructuredLogger logger)
    {
        _connectionString = connectionString;
        _migrationId = migrationId;
        _logger = logger;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action)
    {
        await EnsureConnectionAsync();
        
        try
        {
            await _logger.LogAsync(LogLevel.Info, "Starting transaction", new { MigrationId = _migrationId });
            
            _transaction = await _connection!.BeginTransactionAsync();
            
            var result = await action(_connection, _transaction);
            
            await _transaction.CommitAsync();
            await _logger.LogAsync(LogLevel.Info, "Transaction committed successfully", new { MigrationId = _migrationId });
            
            return result;
        }
        catch (Exception ex)
        {
            await _logger.LogMigrationFailedAsync(_migrationId, ex, "Transaction execution");
            
            if (_transaction != null)
            {
                await _logger.LogAsync(LogLevel.Warning, "Rolling back transaction", new { MigrationId = _migrationId });
                await _transaction.RollbackAsync();
            }
            
            await CreateRecoveryPointAsync(ex);
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
    {
        await ExecuteInTransactionAsync<object>(async (conn, trans) =>
        {
            await action(conn, trans);
            return null!;
        });
    }

    public async Task<string> CreateSavepointAsync(string? name = null)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint creation");

        name ??= $"sp_{Guid.NewGuid():N}";
        
        // Validate savepoint name to prevent SQL injection
        ValidateSavepointName(name);
        
        var command = new NpgsqlCommand($"SAVEPOINT \"{name}\"", _connection, _transaction);
        await command.ExecuteNonQueryAsync();
        
        _savepoints.Push(name);
        
        await _logger.LogAsync(LogLevel.Debug, "Savepoint created", new { 
            SavepointName = name, 
            MigrationId = _migrationId 
        });
        
        return name;
    }

    public async Task RollbackToSavepointAsync(string? name = null)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint rollback");

        name ??= _savepoints.Count > 0 ? _savepoints.Peek() : 
                 throw new InvalidOperationException("No savepoints available");

        // Validate savepoint name to prevent SQL injection
        ValidateSavepointName(name);

        var command = new NpgsqlCommand($"ROLLBACK TO SAVEPOINT \"{name}\"", _connection, _transaction);
        await command.ExecuteNonQueryAsync();
        
        // Remove savepoints after the one we rolled back to
        while (_savepoints.Count > 0 && _savepoints.Peek() != name)
        {
            _savepoints.Pop();
        }
        
        await _logger.LogAsync(LogLevel.Warning, "Rolled back to savepoint", new { 
            SavepointName = name, 
            MigrationId = _migrationId 
        });
    }

    public async Task ReleaseSavepointAsync(string? name = null)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint release");

        name ??= _savepoints.Count > 0 ? _savepoints.Pop() : 
                 throw new InvalidOperationException("No savepoints available");

        // Validate savepoint name to prevent SQL injection
        ValidateSavepointName(name);

        var command = new NpgsqlCommand($"RELEASE SAVEPOINT \"{name}\"", _connection, _transaction);
        await command.ExecuteNonQueryAsync();
        
        await _logger.LogAsync(LogLevel.Debug, "Savepoint released", new { 
            SavepointName = name, 
            MigrationId = _migrationId 
        });
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection == null)
        {
            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync();
            
            await _logger.LogConnectionEventAsync("Opened", _connectionString);
        }
        else if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
            await _logger.LogConnectionEventAsync("Reopened", _connectionString);
        }
    }

    private async Task CreateRecoveryPointAsync(Exception exception)
    {
        try
        {
            var recoveryInfo = new
            {
                MigrationId = _migrationId,
                Timestamp = DateTime.UtcNow,
                Error = exception.Message,
                StackTrace = exception.StackTrace,
                SavepointsCount = _savepoints.Count,
                ActiveSavepoints = _savepoints.ToList()
            };

            await _logger.LogAsync(LogLevel.Error, "Creating recovery point", recoveryInfo);
            
            // Store recovery information in a separate table for later repair
            if (_connection?.State == System.Data.ConnectionState.Open)
            {
                await StoreRecoveryInfoAsync(recoveryInfo);
            }
        }
        catch (Exception recoveryEx)
        {
            await _logger.LogAsync(LogLevel.Critical, "Failed to create recovery point", new 
            { 
                OriginalError = exception.Message,
                RecoveryError = recoveryEx.Message 
            });
        }
    }

    private async Task StoreRecoveryInfoAsync(object recoveryInfo)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(recoveryInfo);
            
            // Create recovery table if not exists (in a separate transaction)
            using var tempConnection = new NpgsqlConnection(_connectionString);
            await tempConnection.OpenAsync();
            
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS __dbmigrator_recovery_log (
                    id SERIAL PRIMARY KEY,
                    migration_id VARCHAR(255),
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    recovery_data JSONB,
                    resolved BOOLEAN DEFAULT FALSE
                )";
            
            using var createCmd = new NpgsqlCommand(createTableSql, tempConnection);
            await createCmd.ExecuteNonQueryAsync();
            
            var insertSql = @"
                INSERT INTO __dbmigrator_recovery_log (migration_id, recovery_data) 
                VALUES (@migrationId, @recoveryData::jsonb)";
            
            using var insertCmd = new NpgsqlCommand(insertSql, tempConnection);
            insertCmd.Parameters.AddWithValue("migrationId", _migrationId);
            insertCmd.Parameters.AddWithValue("recoveryData", json);
            
            await insertCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(LogLevel.Error, "Failed to store recovery info", new { Error = ex.Message });
        }
    }

    private static void ValidateSavepointName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Savepoint name cannot be null or empty", nameof(name));
        
        // PostgreSQL savepoint names must be valid identifiers
        // Allow alphanumeric characters, underscores, and GUIDs (without hyphens)
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException($"Invalid savepoint name '{name}'. Must be a valid PostgreSQL identifier.", nameof(name));
        
        // Prevent excessively long names
        if (name.Length > 63) // PostgreSQL identifier limit
            throw new ArgumentException($"Savepoint name '{name}' is too long. Maximum length is 63 characters.", nameof(name));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _transaction?.Dispose();
            _connection?.Dispose();
            _disposed = true;
        }
    }
}