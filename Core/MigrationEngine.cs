using BorchSolutions.PostgreSQL.Migration.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BorchSolutions.PostgreSQL.Migration.Core;

public interface IMigrationEngine
{
    Task<bool> InitializeAsync(string? connectionName = null);
    Task<List<MigrationScript>> GetPendingMigrationsAsync(string? connectionName = null);
    Task<List<MigrationScript>> GetExecutedMigrationsAsync(string? connectionName = null);
    Task<MigrationScript?> GetExecutedMigrationAsync(string version, string? connectionName = null);
    Task<bool> ExecuteMigrationAsync(MigrationScript migration, string? connectionName = null, bool dryRun = false);
    Task<bool> ExecuteMigrationsAsync(List<MigrationScript> migrations, string? connectionName = null, bool dryRun = false);
    Task<bool> MarkMigrationAsExecutedAsync(MigrationScript migration, string? connectionName = null);
    Task<MigrationSummary> GetMigrationSummaryAsync(string? connectionName = null);
    Task<bool> ValidateMigrationIntegrityAsync(string? connectionName = null);
}

public class MigrationEngine : IMigrationEngine
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<MigrationEngine> _logger;
    private readonly MigrationConfig _config;

    public MigrationEngine(
        IConnectionManager connectionManager,
        ILogger<MigrationEngine> logger,
        MigrationConfig config)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _config = config;
    }

    public async Task<bool> InitializeAsync(string? connectionName = null)
    {
        try
        {
            _logger.LogInformation("🔧 Inicializando motor de migraciones para: {ConnectionName}", connectionName ?? "Default");

            using var connection = await _connectionManager.GetConnectionAsync(connectionName);

            // Crear tabla de migraciones de esquema
            await CreateSchemaMigrationsTableAsync(connection);
            
            // Crear tabla de migraciones de datos
            await CreateDataMigrationsTableAsync(connection);

            _logger.LogInformation("✅ Motor de migraciones inicializado correctamente");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error inicializando motor de migraciones");
            return false;
        }
    }

    public async Task<List<MigrationScript>> GetPendingMigrationsAsync(string? connectionName = null)
    {
        var allMigrations = await LoadMigrationScriptsAsync();
        var executedMigrations = await GetExecutedMigrationsAsync(connectionName);
        var executedVersions = new HashSet<string>(executedMigrations.Select(m => m.Version));

        var pendingMigrations = allMigrations
            .Where(m => !executedVersions.Contains(m.Version))
            .OrderBy(m => m.Version)
            .ToList();

        _logger.LogDebug("📋 Migraciones pendientes: {Count}", pendingMigrations.Count);
        return pendingMigrations;
    }

    public async Task<List<MigrationScript>> GetExecutedMigrationsAsync(string? connectionName = null)
    {
        var migrations = new List<MigrationScript>();

        using var connection = await _connectionManager.GetConnectionAsync(connectionName);

        // Obtener migraciones de esquema
        var schemaMigrations = await GetExecutedSchemaMigrationsAsync(connection);
        migrations.AddRange(schemaMigrations);

        // Obtener migraciones de datos
        var dataMigrations = await GetExecutedDataMigrationsAsync(connection);
        migrations.AddRange(dataMigrations);

        return migrations.OrderBy(m => m.ExecutedAt).ToList();
    }

    public async Task<MigrationScript?> GetExecutedMigrationAsync(string version, string? connectionName = null)
    {
        var executedMigrations = await GetExecutedMigrationsAsync(connectionName);
        return executedMigrations.FirstOrDefault(m => m.Version == version);
    }

    public async Task<bool> ExecuteMigrationAsync(MigrationScript migration, string? connectionName = null, bool dryRun = false)
    {
        if (dryRun)
        {
            _logger.LogInformation("🔍 [DRY RUN] Migración: {Name}", migration.Name);
            return true;
        }

        var stopwatch = Stopwatch.StartNew();
        migration.Status = MigrationStatus.InProgress;

        try
        {
            _logger.LogInformation("🚀 Ejecutando migración: {Name}", migration.Name);

            using var connection = await _connectionManager.GetConnectionAsync(connectionName);
            
            if (_config.EnableTransactions)
            {
                using var transaction = await connection.BeginTransactionAsync();
                
                try
                {
                    var affectedRows = await ExecuteSqlScriptAsync(connection, migration.Content, transaction);
                    await RegisterMigrationAsync(connection, migration, stopwatch.ElapsedMilliseconds, affectedRows, transaction);
                    await transaction.CommitAsync();
                    
                    migration.MarkAsCompleted((int)stopwatch.ElapsedMilliseconds, affectedRows);
                    _logger.LogInformation("✅ Migración completada: {Name} ({Time}ms, {Rows} filas)", 
                        migration.Name, stopwatch.ElapsedMilliseconds, affectedRows);
                    
                    return true;
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            else
            {
                var affectedRows = await ExecuteSqlScriptAsync(connection, migration.Content);
                await RegisterMigrationAsync(connection, migration, stopwatch.ElapsedMilliseconds, affectedRows);
                
                migration.MarkAsCompleted((int)stopwatch.ElapsedMilliseconds, affectedRows);
                _logger.LogInformation("✅ Migración completada: {Name} ({Time}ms, {Rows} filas)", 
                    migration.Name, stopwatch.ElapsedMilliseconds, affectedRows);
                
                return true;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            migration.MarkAsFailed(ex.Message, (int)stopwatch.ElapsedMilliseconds);
            
            _logger.LogError(ex, "❌ Error ejecutando migración: {Name}", migration.Name);
            return false;
        }
    }

    public async Task<bool> ExecuteMigrationsAsync(List<MigrationScript> migrations, string? connectionName = null, bool dryRun = false)
    {
        if (!migrations.Any())
        {
            _logger.LogInformation("✅ No hay migraciones pendientes");
            return true;
        }

        _logger.LogInformation("🚀 Ejecutando {Count} migraciones...", migrations.Count);

        var successCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var migration in migrations)
        {
            var success = await ExecuteMigrationAsync(migration, connectionName, dryRun);
            if (success)
            {
                successCount++;
            }
            else if (!dryRun)
            {
                _logger.LogError("❌ Deteniendo ejecución debido a error en migración: {Name}", migration.Name);
                break;
            }
        }

        totalStopwatch.Stop();
        
        if (dryRun)
        {
            _logger.LogInformation("🔍 [DRY RUN] Completado - {Success}/{Total} migraciones validadas", 
                successCount, migrations.Count);
        }
        else
        {
            _logger.LogInformation("✅ Ejecución completada - {Success}/{Total} migraciones ejecutadas en {Time}ms", 
                successCount, migrations.Count, totalStopwatch.ElapsedMilliseconds);
        }

        return successCount == migrations.Count;
    }

    public async Task<bool> MarkMigrationAsExecutedAsync(MigrationScript migration, string? connectionName = null)
    {
        try
        {
            using var connection = await _connectionManager.GetConnectionAsync(connectionName);
            await RegisterMigrationAsync(connection, migration, 0, 0);
            
            migration.MarkAsCompleted(0, 0);
            _logger.LogInformation("✅ Migración marcada como ejecutada: {Name}", migration.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error marcando migración como ejecutada: {Name}", migration.Name);
            return false;
        }
    }

    public async Task<MigrationSummary> GetMigrationSummaryAsync(string? connectionName = null)
    {
        var allMigrations = await LoadMigrationScriptsAsync();
        var executedMigrations = await GetExecutedMigrationsAsync(connectionName);
        var executedVersions = new HashSet<string>(executedMigrations.Select(m => m.Version));

        var pendingMigrations = allMigrations.Where(m => !executedVersions.Contains(m.Version)).ToList();
        var failedMigrations = executedMigrations.Where(m => m.HasFailed).ToList();

        return new MigrationSummary
        {
            TotalScripts = allMigrations.Count,
            CompletedScripts = executedMigrations.Count(m => m.IsExecuted),
            FailedScripts = failedMigrations.Count,
            PendingScripts = pendingMigrations.Count,
            TotalExecutionTime = executedMigrations.Sum(m => m.ExecutionTimeMs),
            TotalAffectedRows = executedMigrations.Sum(m => m.AffectedRows),
            LastExecution = executedMigrations.Max(m => m.ExecutedAt),
            Scripts = allMigrations
        };
    }

    public async Task<bool> ValidateMigrationIntegrityAsync(string? connectionName = null)
    {
        try
        {
            _logger.LogInformation("🔍 Validando integridad de migraciones...");

            var executedMigrations = await GetExecutedMigrationsAsync(connectionName);
            var migrationFiles = await LoadMigrationScriptsAsync();

            // Verificar checksums
            var integrityIssues = 0;

            foreach (var executedMigration in executedMigrations)
            {
                var fileVersion = migrationFiles.FirstOrDefault(f => f.Version == executedMigration.Version);
                if (fileVersion != null && fileVersion.Checksum != executedMigration.Checksum)
                {
                    _logger.LogWarning("⚠️  Checksum no coincide para migración: {Version}", executedMigration.Version);
                    integrityIssues++;
                }
            }

            // Verificar versiones duplicadas
            var duplicateVersions = executedMigrations
                .GroupBy(m => m.Version)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateVersions.Any())
            {
                _logger.LogWarning("⚠️  Versiones duplicadas encontradas: {Versions}", string.Join(", ", duplicateVersions));
                integrityIssues += duplicateVersions.Count;
            }

            if (integrityIssues == 0)
            {
                _logger.LogInformation("✅ Validación de integridad completada - Sin problemas");
                return true;
            }
            else
            {
                _logger.LogWarning("⚠️  Validación completada - {Issues} problemas encontrados", integrityIssues);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error validando integridad de migraciones");
            return false;
        }
    }

    private async Task<List<MigrationScript>> LoadMigrationScriptsAsync()
    {
        var migrations = new List<MigrationScript>();

        // Cargar scripts de esquema
        var schemaPath = Path.Combine(_config.MigrationsPath, _config.SchemaPath);
        if (Directory.Exists(schemaPath))
        {
            var schemaFiles = Directory.GetFiles(schemaPath, "*.sql", SearchOption.AllDirectories)
                .OrderBy(f => f);

            foreach (var file in schemaFiles)
            {
                var migration = await LoadMigrationScriptFromFileAsync(file, MigrationScriptType.Schema);
                if (migration != null)
                {
                    migrations.Add(migration);
                }
            }
        }

        // Cargar scripts de datos
        var dataPath = Path.Combine(_config.MigrationsPath, _config.DataPath);
        if (Directory.Exists(dataPath))
        {
            var dataFiles = Directory.GetFiles(dataPath, "*.sql", SearchOption.AllDirectories)
                .OrderBy(f => f);

            foreach (var file in dataFiles)
            {
                var migration = await LoadMigrationScriptFromFileAsync(file, MigrationScriptType.Data);
                if (migration != null)
                {
                    migrations.Add(migration);
                }
            }
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    private async Task<MigrationScript?> LoadMigrationScriptFromFileAsync(string filePath, MigrationScriptType type)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var versionMatch = Regex.Match(fileName, @"^([VD]\d{3}_\d{3})__(.+)$");
            
            if (!versionMatch.Success)
            {
                _logger.LogWarning("⚠️  Nombre de archivo no válido: {FileName}", fileName);
                return null;
            }

            var version = versionMatch.Groups[1].Value;
            var description = versionMatch.Groups[2].Value.Replace('_', ' ');
            var content = await File.ReadAllTextAsync(filePath);
            var checksum = CalculateChecksum(content);

            return new MigrationScript
            {
                Version = version,
                Name = fileName,
                Description = description,
                FilePath = filePath,
                Content = content,
                Type = type,
                Checksum = checksum,
                CreatedAt = File.GetCreationTimeUtc(filePath),
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error cargando script de migración: {FilePath}", filePath);
            return null;
        }
    }

    private async Task<int> ExecuteSqlScriptAsync(NpgsqlConnection connection, string script, NpgsqlTransaction? transaction = null)
    {
        var totalAffectedRows = 0;
        
        // Dividir script en comandos individuales
        var commands = SplitSqlScript(script);
        
        foreach (var commandText in commands)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                continue;

            using var command = new NpgsqlCommand(commandText, connection, transaction);
            command.CommandTimeout = _config.CommandTimeout;
            
            var result = await command.ExecuteNonQueryAsync();
            if (result > 0)
            {
                totalAffectedRows += result;
            }
        }

        return totalAffectedRows;
    }

    private List<string> SplitSqlScript(string script)
    {
        // Dividir por punto y coma, pero respetando strings y comentarios
        var commands = new List<string>();
        var currentCommand = new StringBuilder();
        var inString = false;
        
        var lines = script.Split('\n');
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Ignorar líneas vacías y comentarios
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("--"))
                continue;

            currentCommand.AppendLine(line);
            
            // Si la línea termina con ; y no estamos en un string, es fin de comando
            if (trimmedLine.EndsWith(";") && !inString)
            {
                commands.Add(currentCommand.ToString());
                currentCommand.Clear();
            }
        }

        // Agregar último comando si existe
        if (currentCommand.Length > 0)
        {
            commands.Add(currentCommand.ToString());
        }

        return commands.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    private async Task RegisterMigrationAsync(
        NpgsqlConnection connection, 
        MigrationScript migration, 
        long executionTime, 
        int affectedRows, 
        NpgsqlTransaction? transaction = null)
    {
        var tableName = migration.Type == MigrationScriptType.Schema || migration.Type == MigrationScriptType.Baseline ? 
            _config.SchemaTable : _config.DataTable;

        var query = $@"
            INSERT INTO {tableName} (version, name, description, checksum, executed_at, execution_time_ms, affected_rows, environment, success)
            VALUES (@version, @name, @description, @checksum, @executedAt, @executionTime, @affectedRows, @environment, @success)
            ON CONFLICT (version) DO UPDATE SET
                executed_at = EXCLUDED.executed_at,
                execution_time_ms = EXCLUDED.execution_time_ms,
                affected_rows = EXCLUDED.affected_rows,
                success = EXCLUDED.success";

        using var command = new NpgsqlCommand(query, connection, transaction);
        
        command.Parameters.AddWithValue("@version", migration.Version);
        command.Parameters.AddWithValue("@name", migration.Name);
        command.Parameters.AddWithValue("@description", migration.Description);
        command.Parameters.AddWithValue("@checksum", migration.Checksum);
        command.Parameters.AddWithValue("@executedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@executionTime", executionTime);
        command.Parameters.AddWithValue("@affectedRows", affectedRows);
        command.Parameters.AddWithValue("@environment", migration.Environment);
        command.Parameters.AddWithValue("@success", migration.Status != MigrationStatus.Failed);

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateSchemaMigrationsTableAsync(NpgsqlConnection connection)
    {
        var createTableQuery = $@"
            CREATE TABLE IF NOT EXISTS {_config.SchemaTable} (
                id SERIAL PRIMARY KEY,
                version VARCHAR(50) NOT NULL UNIQUE,
                name VARCHAR(255) NOT NULL,
                description VARCHAR(500) NOT NULL,
                checksum VARCHAR(100) NOT NULL,
                executed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                execution_time_ms BIGINT NOT NULL DEFAULT 0,
                affected_rows INTEGER NOT NULL DEFAULT 0,
                environment VARCHAR(50) NOT NULL DEFAULT 'Development',
                success BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_{_config.SchemaTable.Replace("borchsolutions_", "")}_version ON {_config.SchemaTable}(version);
            CREATE INDEX IF NOT EXISTS idx_{_config.SchemaTable.Replace("borchsolutions_", "")}_executed_at ON {_config.SchemaTable}(executed_at);
            CREATE INDEX IF NOT EXISTS idx_{_config.SchemaTable.Replace("borchsolutions_", "")}_environment ON {_config.SchemaTable}(environment);";

        using var command = new NpgsqlCommand(createTableQuery, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateDataMigrationsTableAsync(NpgsqlConnection connection)
    {
        var createTableQuery = $@"
            CREATE TABLE IF NOT EXISTS {_config.DataTable} (
                id SERIAL PRIMARY KEY,
                version VARCHAR(50) NOT NULL UNIQUE,
                name VARCHAR(255) NOT NULL,
                description VARCHAR(500) NOT NULL,
                checksum VARCHAR(100) NOT NULL,
                executed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                execution_time_ms BIGINT NOT NULL DEFAULT 0,
                affected_rows INTEGER NOT NULL DEFAULT 0,
                environment VARCHAR(50) NOT NULL DEFAULT 'Development',
                success BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_{_config.DataTable.Replace("borchsolutions_", "")}_version ON {_config.DataTable}(version);
            CREATE INDEX IF NOT EXISTS idx_{_config.DataTable.Replace("borchsolutions_", "")}_executed_at ON {_config.DataTable}(executed_at);
            CREATE INDEX IF NOT EXISTS idx_{_config.DataTable.Replace("borchsolutions_", "")}_environment ON {_config.DataTable}(environment);";

        using var command = new NpgsqlCommand(createTableQuery, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<MigrationScript>> GetExecutedSchemaMigrationsAsync(NpgsqlConnection connection)
    {
        // Obtener todas las migraciones de la tabla de esquema (incluye Schema y Baseline)
        return await GetAllMigrationsFromTableAsync(connection, _config.SchemaTable);
    }

    private async Task<List<MigrationScript>> GetExecutedDataMigrationsAsync(NpgsqlConnection connection)
    {
        return await GetExecutedMigrationsFromTableAsync(connection, _config.DataTable, MigrationScriptType.Data);
    }

    private async Task<List<MigrationScript>> GetExecutedMigrationsFromTableAsync(
        NpgsqlConnection connection, 
        string tableName, 
        MigrationScriptType type)
    {
        var migrations = new List<MigrationScript>();

        // Verificar si la tabla existe
        var tableExistsQuery = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = @tableName
            )";

        using var existsCommand = new NpgsqlCommand(tableExistsQuery, connection);
        existsCommand.Parameters.AddWithValue("@tableName", tableName);
        
        var tableExists = (bool)(await existsCommand.ExecuteScalarAsync() ?? false);
        if (!tableExists)
        {
            return migrations;
        }

        var query = $@"
            SELECT version, name, description, checksum, executed_at, execution_time_ms, affected_rows, environment, success
            FROM {tableName}
            ORDER BY executed_at";

        using var command = new NpgsqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var migration = new MigrationScript
            {
                Version = reader["version"].ToString(),
                Name = reader["name"].ToString(),
                Description = reader["description"].ToString(),
                Type = type,
                Checksum = reader["checksum"].ToString(),
                ExecutedAt = Convert.ToDateTime(reader["executed_at"]),
                ExecutionTimeMs = Convert.ToInt32(reader["execution_time_ms"]),
                AffectedRows = Convert.ToInt32(reader["affected_rows"]),
                Environment = reader["environment"].ToString(),
                Status = Convert.ToBoolean(reader["success"]) ? MigrationStatus.Completed : MigrationStatus.Failed
            };

            migrations.Add(migration);
        }

        return migrations;
    }

    private async Task<List<MigrationScript>> GetAllMigrationsFromTableAsync(NpgsqlConnection connection, string tableName)
    {
        var migrations = new List<MigrationScript>();

        // Verificar si la tabla existe
        var tableExistsQuery = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = @tableName
            )";

        using var existsCommand = new NpgsqlCommand(tableExistsQuery, connection);
        existsCommand.Parameters.AddWithValue("@tableName", tableName);
        
        var tableExists = (bool)(await existsCommand.ExecuteScalarAsync() ?? false);
        if (!tableExists)
        {
            return migrations;
        }

        var query = $@"
            SELECT version, name, description, checksum, executed_at, execution_time_ms, affected_rows, environment, success
            FROM {tableName}
            ORDER BY executed_at";

        using var command = new NpgsqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var version = reader["version"].ToString();
            var name = reader["name"].ToString();
            
            // Determinar el tipo basándose en la versión/nombre
            var type = MigrationScriptType.Schema; // Default
            if (version?.StartsWith("V000_001") == true || name?.Contains("Baseline") == true)
            {
                type = MigrationScriptType.Baseline;
            }

            var migration = new MigrationScript
            {
                Version = version,
                Name = name,
                Description = reader["description"].ToString(),
                Type = type,
                Checksum = reader["checksum"].ToString(),
                ExecutedAt = Convert.ToDateTime(reader["executed_at"]),
                ExecutionTimeMs = Convert.ToInt32(reader["execution_time_ms"]),
                AffectedRows = Convert.ToInt32(reader["affected_rows"]),
                Environment = reader["environment"].ToString(),
                Status = Convert.ToBoolean(reader["success"]) ? MigrationStatus.Completed : MigrationStatus.Failed
            };

            migrations.Add(migration);
        }

        return migrations;
    }

    private static string CalculateChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}