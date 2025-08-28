using BorchSolutions.PostgreSQL.Migration.Models;
using BorchSolutions.PostgreSQL.Migration.Services;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace BorchSolutions.PostgreSQL.Migration.Core;

public interface IBaselineGenerator
{
    Task<bool> GenerateBaselineAsync(string? connectionName = null, string? outputPath = null, bool markAsExecuted = false);
    Task<bool> CreateBaselineFromExistingDatabaseAsync(string? connectionName = null, string? outputPath = null);
    Task<MigrationScript?> GetBaselineScriptAsync(string? connectionName = null);
}

public class BaselineGenerator : IBaselineGenerator
{
    private const string BASELINE_VERSION = "V000_001";
    private const string BASELINE_DESCRIPTION = "Initial_Baseline";
    
    private readonly IConnectionManager _connectionManager;
    private readonly ISchemaInspector _schemaInspector;
    private readonly IDataExtractor _dataExtractor;
    private readonly IMigrationEngine _migrationEngine;
    private readonly ILogger<BaselineGenerator> _logger;
    private readonly MigrationConfig _config;

    public BaselineGenerator(
        IConnectionManager connectionManager,
        ISchemaInspector schemaInspector,
        IDataExtractor dataExtractor,
        IMigrationEngine migrationEngine,
        ILogger<BaselineGenerator> logger,
        MigrationConfig config)
    {
        _connectionManager = connectionManager;
        _schemaInspector = schemaInspector;
        _dataExtractor = dataExtractor;
        _migrationEngine = migrationEngine;
        _logger = logger;
        _config = config;
    }

    public async Task<bool> GenerateBaselineAsync(string? connectionName = null, string? outputPath = null, bool markAsExecuted = false)
    {
        try
        {
            _logger.LogInformation("🔄 Iniciando generación de baseline para conexión: {ConnectionName}", connectionName ?? "Default");

            // 1. Verificar conexión
            if (!await _connectionManager.TestConnectionAsync(connectionName))
            {
                _logger.LogError("❌ No se puede conectar a la base de datos: {ConnectionName}", connectionName ?? "Default");
                return false;
            }

            // 2. Obtener información de la base de datos
            var dbInfo = await _connectionManager.GetDatabaseInfoAsync(connectionName);
            _logger.LogInformation("📊 Base de datos: {DatabaseName} - Tablas: {TableCount}, Funciones: {FunctionCount}", 
                dbInfo.DatabaseName, dbInfo.TableCount, dbInfo.FunctionCount);

            // 3. Verificar si ya existe baseline
            var existingBaseline = await _migrationEngine.GetExecutedMigrationAsync(BASELINE_VERSION, connectionName);
            if (existingBaseline != null)
            {
                _logger.LogWarning("⚠️  Ya existe un baseline registrado: {Version}", BASELINE_VERSION);
                Console.Write("¿Desea regenerarlo? (y/N): ");
                var response = Console.ReadLine()?.ToLower();
                
                if (response != "y" && response != "yes")
                {
                    _logger.LogInformation("❌ Operación cancelada por el usuario");
                    return false;
                }
            }

            // 4. Generar script de baseline
            var baselineScript = await GenerateBaselineScriptAsync(connectionName);
            if (baselineScript == null)
            {
                _logger.LogError("❌ No se pudo generar el script baseline");
                return false;
            }

            // 5. Guardar archivo si se especifica ruta
            if (!string.IsNullOrEmpty(outputPath))
            {
                await SaveBaselineToFileAsync(baselineScript, outputPath);
                _logger.LogInformation("💾 Baseline guardado en: {OutputPath}", outputPath);
            }

            // 6. Marcar como ejecutado si se solicita
            if (markAsExecuted)
            {
                var markResult = await _migrationEngine.MarkMigrationAsExecutedAsync(baselineScript, connectionName);
                if (markResult)
                {
                    _logger.LogInformation("✅ Baseline marcado como ejecutado");
                }
                else
                {
                    _logger.LogWarning("⚠️  Error marcando baseline como ejecutado");
                    return false;
                }
            }

            _logger.LogInformation("🎉 Baseline generado exitosamente!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generando baseline");
            return false;
        }
    }

    public async Task<bool> CreateBaselineFromExistingDatabaseAsync(string? connectionName = null, string? outputPath = null)
    {
        try
        {
            _logger.LogInformation("🔄 Creando baseline desde base de datos existente");

            // Determinar ruta de salida
            if (string.IsNullOrEmpty(outputPath))
            {
                var schemaPath = Path.Combine(_config.MigrationsPath, _config.SchemaPath);
                Directory.CreateDirectory(schemaPath);
                outputPath = Path.Combine(schemaPath, $"{BASELINE_VERSION}__{BASELINE_DESCRIPTION}.sql");
            }

            // Generar y guardar baseline
            return await GenerateBaselineAsync(connectionName, outputPath, markAsExecuted: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creando baseline desde base de datos existente");
            return false;
        }
    }

    public async Task<MigrationScript?> GetBaselineScriptAsync(string? connectionName = null)
    {
        return await GenerateBaselineScriptAsync(connectionName);
    }

    private async Task<MigrationScript?> GenerateBaselineScriptAsync(string? connectionName = null)
    {
        try
        {
            var script = new StringBuilder();
            
            // Header del script
            script.AppendLine("-- ========================================");
            script.AppendLine("-- BASELINE GENERADO POR BORCHSOLUTIONS");
            script.AppendLine($"-- Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            script.AppendLine($"-- Conexión: {connectionName ?? "Default"}");
            script.AppendLine("-- ========================================");
            script.AppendLine();

            var dbInfo = await _connectionManager.GetDatabaseInfoAsync(connectionName);
            script.AppendLine($"-- Base de datos: {dbInfo.DatabaseName}");
            script.AppendLine($"-- Versión PostgreSQL: {dbInfo.ServerVersion}");
            script.AppendLine($"-- Tablas: {dbInfo.TableCount}, Funciones: {dbInfo.FunctionCount}");
            script.AppendLine();

            // 1. Esquemas
            _logger.LogDebug("🔍 Obteniendo definiciones de esquema...");
            var schemaDefinitions = await _schemaInspector.GetSchemaDefinitionsAsync(connectionName);
            
            if (schemaDefinitions.Tables.Any())
            {
                script.AppendLine("-- ========================================");
                script.AppendLine("-- TABLAS");
                script.AppendLine("-- ========================================");
                foreach (var table in schemaDefinitions.Tables)
                {
                    script.AppendLine($"-- Tabla: {table.Name}");
                    script.AppendLine(table.CreateScript);
                    script.AppendLine();
                }
            }

            if (schemaDefinitions.Indexes.Any())
            {
                script.AppendLine("-- ========================================");
                script.AppendLine("-- ÍNDICES");
                script.AppendLine("-- ========================================");
                foreach (var index in schemaDefinitions.Indexes)
                {
                    script.AppendLine($"-- Índice: {index.Name}");
                    script.AppendLine(index.CreateScript);
                    script.AppendLine();
                }
            }

            if (schemaDefinitions.Functions.Any())
            {
                script.AppendLine("-- ========================================");
                script.AppendLine("-- FUNCIONES Y PROCEDIMIENTOS");
                script.AppendLine("-- ========================================");
                foreach (var function in schemaDefinitions.Functions)
                {
                    script.AppendLine($"-- Función: {function.Name}");
                    script.AppendLine(function.CreateScript);
                    script.AppendLine();
                }
            }

            if (schemaDefinitions.Triggers.Any())
            {
                script.AppendLine("-- ========================================");
                script.AppendLine("-- TRIGGERS");
                script.AppendLine("-- ========================================");
                foreach (var trigger in schemaDefinitions.Triggers)
                {
                    script.AppendLine($"-- Trigger: {trigger.Name}");
                    script.AppendLine(trigger.CreateScript);
                    script.AppendLine();
                }
            }

            script.AppendLine("-- ========================================");
            script.AppendLine("-- FIN DEL BASELINE");
            script.AppendLine("-- ========================================");

            var content = script.ToString();
            var checksum = CalculateChecksum(content);

            var migrationScript = new MigrationScript
            {
                Version = BASELINE_VERSION,
                Name = $"{BASELINE_VERSION}__{BASELINE_DESCRIPTION}",
                Description = BASELINE_DESCRIPTION,
                Content = content,
                Type = MigrationScriptType.Baseline,
                Checksum = checksum,
                CreatedAt = DateTime.UtcNow,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
            };

            _logger.LogInformation("✅ Script baseline generado - Tamaño: {Size} KB, Checksum: {Checksum}", 
                Encoding.UTF8.GetByteCount(content) / 1024.0, checksum[..8]);

            return migrationScript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generando script baseline");
            return null;
        }
    }

    private async Task SaveBaselineToFileAsync(MigrationScript script, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, script.Content);
        script.FilePath = filePath;
        
        _logger.LogDebug("📁 Baseline guardado en: {FilePath} ({Size} bytes)", 
            filePath, Encoding.UTF8.GetByteCount(script.Content));
    }

    private static string CalculateChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}