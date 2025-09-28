using System.Text.Json;
using DBMigrator.Core.Models.Configuration;
using DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "dbmigrator.json";
    private const string BaselineFileName = ".baseline.json";

    public async Task<MigratorConfig> LoadConfigAsync(string? configPath = null)
    {
        var path = configPath ?? ConfigFileName;
        
        if (!File.Exists(path))
        {
            return MigratorConfig.Default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var config = JsonSerializer.Deserialize<MigratorConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            return config ?? MigratorConfig.Default;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load configuration from {path}: {ex.Message}", ex);
        }
    }

    public async Task SaveConfigAsync(MigratorConfig config, string? configPath = null)
    {
        var path = configPath ?? ConfigFileName;
        
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration to {path}: {ex.Message}", ex);
        }
    }

    public async Task SaveBaselineAsync(DatabaseSchema schema, string? migrationsPath = null)
    {
        var basePath = migrationsPath ?? "./migrations";
        var baselinePath = Path.Combine(basePath, BaselineFileName);
        
        // Ensure directory exists
        Directory.CreateDirectory(basePath);

        try
        {
            var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            await File.WriteAllTextAsync(baselinePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save baseline to {baselinePath}: {ex.Message}", ex);
        }
    }

    public async Task<DatabaseSchema?> LoadBaselineAsync(string? migrationsPath = null)
    {
        var basePath = migrationsPath ?? "./migrations";
        var baselinePath = Path.Combine(basePath, BaselineFileName);
        
        if (!File.Exists(baselinePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(baselinePath);
            return JsonSerializer.Deserialize<DatabaseSchema>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load baseline from {baselinePath}: {ex.Message}", ex);
        }
    }

    public MigratorConfig MergeWithEnvironment(MigratorConfig config)
    {
        // Override with environment variables if present
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION");
        if (!string.IsNullOrEmpty(connectionString))
        {
            config.ConnectionString = connectionString;
        }

        var environment = Environment.GetEnvironmentVariable("MIGRATOR_ENVIRONMENT");
        if (!string.IsNullOrEmpty(environment))
        {
            config.Environment = environment;
        }

        var migrationsPath = Environment.GetEnvironmentVariable("MIGRATOR_MIGRATIONS_PATH");
        if (!string.IsNullOrEmpty(migrationsPath))
        {
            config.MigrationsPath = migrationsPath;
        }

        var schema = Environment.GetEnvironmentVariable("MIGRATOR_SCHEMA");
        if (!string.IsNullOrEmpty(schema))
        {
            config.Schema = schema;
        }

        return config;
    }
}