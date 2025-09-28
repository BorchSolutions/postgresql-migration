using System.Collections.Concurrent;
using Npgsql;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class MultiDatabaseManager
{
    private readonly StructuredLogger _logger;
    private readonly ConcurrentDictionary<string, DatabaseConfiguration> _databases;
    private readonly ConcurrentDictionary<string, MigrationService> _engines;

    public MultiDatabaseManager(StructuredLogger logger)
    {
        _logger = logger;
        _databases = new ConcurrentDictionary<string, DatabaseConfiguration>();
        _engines = new ConcurrentDictionary<string, MigrationService>();
    }

    public async Task RegisterDatabaseAsync(string name, DatabaseConfiguration config)
    {
        // Validate connection string format first
        var validation = ConnectionStringValidator.Validate(config.ConnectionString);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors);
            await _logger.LogAsync(LogLevel.Error, "Invalid connection string", new 
            { 
                DatabaseName = name,
                Errors = errors
            });
            throw new ArgumentException($"Invalid connection string for database '{name}': {errors}");
        }

        await _logger.LogAsync(LogLevel.Info, "Registering database", new 
        { 
            DatabaseName = name,
            Host = validation.ParsedHost,
            Database = validation.ParsedDatabase,
            Port = validation.ParsedPort
        });

        _databases.AddOrUpdate(name, config, (_, _) => config);
        
        var engine = new MigrationService(config.ConnectionString);
        _engines.AddOrUpdate(name, engine, (_, _) => engine);

        await ValidateDatabaseConnectionAsync(name, config);
    }

    public async Task<MultiDatabaseOperationResult> ExecuteOnAllAsync(
        Func<string, MigrationService, Task<bool>> operation,
        bool continueOnError = false)
    {
        await _logger.LogAsync(LogLevel.Info, "Starting multi-database operation", new 
        { 
            DatabaseCount = _databases.Count,
            ContinueOnError = continueOnError
        });

        var result = new MultiDatabaseOperationResult
        {
            StartedAt = DateTime.UtcNow,
            TotalDatabases = _databases.Count
        };

        var tasks = new List<Task<DatabaseOperationResult>>();

        foreach (var (name, engine) in _engines)
        {
            tasks.Add(ExecuteOnDatabaseAsync(name, engine, operation));
        }

        var results = await Task.WhenAll(tasks);
        
        result.Results = results.ToList();
        result.CompletedAt = DateTime.UtcNow;
        result.Duration = result.CompletedAt - result.StartedAt;
        result.SuccessfulDatabases = results.Count(r => r.Success);
        result.FailedDatabases = results.Count(r => !r.Success);

        await _logger.LogAsync(LogLevel.Info, "Multi-database operation completed", new 
        { 
            Successful = result.SuccessfulDatabases,
            Failed = result.FailedDatabases,
            Duration = result.Duration.TotalSeconds
        });

        return result;
    }

    public async Task<MultiDatabaseOperationResult> ExecuteOnSelectedAsync(
        IEnumerable<string> databaseNames,
        Func<string, MigrationService, Task<bool>> operation)
    {
        var selectedEngines = _engines
            .Where(kvp => databaseNames.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        await _logger.LogAsync(LogLevel.Info, "Starting selective multi-database operation", new 
        { 
            SelectedDatabases = selectedEngines.Keys,
            DatabaseCount = selectedEngines.Count
        });

        var result = new MultiDatabaseOperationResult
        {
            StartedAt = DateTime.UtcNow,
            TotalDatabases = selectedEngines.Count
        };

        var tasks = selectedEngines.Select(kvp => 
            ExecuteOnDatabaseAsync(kvp.Key, kvp.Value, operation)).ToList();

        var results = await Task.WhenAll(tasks);
        
        result.Results = results.ToList();
        result.CompletedAt = DateTime.UtcNow;
        result.Duration = result.CompletedAt - result.StartedAt;
        result.SuccessfulDatabases = results.Count(r => r.Success);
        result.FailedDatabases = results.Count(r => !r.Success);

        return result;
    }

    public async Task<DatabaseHealthReport> GetHealthReportAsync()
    {
        await _logger.LogAsync(LogLevel.Info, "Generating database health report", new 
        { 
            DatabaseCount = _databases.Count
        });

        var report = new DatabaseHealthReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalDatabases = _databases.Count
        };

        var healthTasks = _databases.Select(async kvp =>
        {
            var (name, config) = kvp;
            return await CheckDatabaseHealthAsync(name, config);
        });

        var healthResults = await Task.WhenAll(healthTasks);
        report.DatabaseHealth = healthResults.ToList();

        report.HealthyDatabases = healthResults.Count(h => h.IsHealthy);
        report.UnhealthyDatabases = healthResults.Count(h => !h.IsHealthy);

        return report;
    }

    public IEnumerable<string> GetRegisteredDatabases()
    {
        return _databases.Keys;
    }

    public DatabaseConfiguration? GetDatabaseConfiguration(string name)
    {
        return _databases.TryGetValue(name, out var config) ? config : null;
    }

    public MigrationService? GetMigrationService(string name)
    {
        return _engines.TryGetValue(name, out var engine) ? engine : null;
    }

    private async Task<DatabaseOperationResult> ExecuteOnDatabaseAsync(
        string name, 
        MigrationService engine, 
        Func<string, MigrationService, Task<bool>> operation)
    {
        var result = new DatabaseOperationResult
        {
            DatabaseName = name,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            result.Success = await operation(name, engine);
            result.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;

            await _logger.LogAsync(LogLevel.Error, "Database operation failed", new 
            { 
                DatabaseName = name,
                Error = ex.Message
            }, ex);
        }

        return result;
    }

    private async Task<DatabaseHealth> CheckDatabaseHealthAsync(string name, DatabaseConfiguration config)
    {
        var health = new DatabaseHealth
        {
            DatabaseName = name,
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            stopwatch.Stop();

            health.IsHealthy = true;
            health.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            health.Version = connection.ServerVersion;

            // Check migration table status
            try
            {
                using var migrationCheck = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM __dbmigrator_schema_migrations", connection);
                var migrationCount = await migrationCheck.ExecuteScalarAsync();
                health.MigrationCount = Convert.ToInt32(migrationCount);
            }
            catch
            {
                health.MigrationCount = 0;
                health.Notes = "Migration tables not initialized";
            }
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Error = ex.Message;
        }

        return health;
    }

    private async Task ValidateDatabaseConnectionAsync(string name, DatabaseConfiguration config)
    {
        try
        {
            var testResult = await ConnectionStringValidator.TestConnectionAsync(config.ConnectionString, 10);
            
            if (testResult.IsSuccessful)
            {
                await _logger.LogAsync(LogLevel.Info, "Database connection validated", new 
                { 
                    DatabaseName = name,
                    ServerVersion = testResult.ServerVersion,
                    ConnectionTime = testResult.ConnectionTime.TotalMilliseconds
                });
            }
            else
            {
                await _logger.LogAsync(LogLevel.Error, "Database connection test failed", new 
                { 
                    DatabaseName = name,
                    Error = testResult.Error
                });
                throw new InvalidOperationException($"Connection test failed for database '{name}': {testResult.Error}");
            }
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            await _logger.LogAsync(LogLevel.Error, "Database connection validation error", new 
            { 
                DatabaseName = name,
                Error = ex.Message
            }, ex);
            throw;
        }
    }

    private static ConnectionInfo? ParseConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return new ConnectionInfo
            {
                Host = builder.Host ?? "localhost",
                Port = builder.Port == 0 ? 5432 : builder.Port,
                Database = builder.Database ?? "",
                Username = builder.Username ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    private record ConnectionInfo
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
        public string Database { get; init; } = "";
        public string Username { get; init; } = "";
    }
}

public class MultiDatabaseOperationResult
{
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalDatabases { get; set; }
    public int SuccessfulDatabases { get; set; }
    public int FailedDatabases { get; set; }
    public List<DatabaseOperationResult> Results { get; set; } = new();
}

public class DatabaseOperationResult
{
    public string DatabaseName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;
}

public class DatabaseHealthReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalDatabases { get; set; }
    public int HealthyDatabases { get; set; }
    public int UnhealthyDatabases { get; set; }
    public List<DatabaseHealth> DatabaseHealth { get; set; } = new();
}

public class DatabaseHealth
{
    public string DatabaseName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public DateTime CheckedAt { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? Version { get; set; }
    public int MigrationCount { get; set; }
    public string? Error { get; set; }
    public string? Notes { get; set; }
}