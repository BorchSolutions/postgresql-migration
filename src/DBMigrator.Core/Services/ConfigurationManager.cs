using DBMigrator.Core.Models;
using System.Text.Json;

namespace DBMigrator.Core.Services;

public class ConfigurationManager
{
    private const string DefaultConfigFileName = "dbmigrator.json";
    private const string EnvironmentConfigPattern = "dbmigrator.{0}.json";
    
    private EnvironmentConfiguration? _configuration;
    private readonly string _configPath;

    public ConfigurationManager(string? configPath = null)
    {
        _configPath = configPath ?? Environment.CurrentDirectory;
    }

    public async Task<DatabaseConfiguration> LoadConfigurationAsync(string? environment = null)
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        environment ??= envConfig.DefaultEnvironment;

        if (envConfig.Environments.TryGetValue(environment, out var config))
        {
            return config;
        }

        // Fallback to environment-specific config file
        var envConfigFile = Path.Combine(_configPath, string.Format(EnvironmentConfigPattern, environment));
        if (File.Exists(envConfigFile))
        {
            var envSpecificConfig = await LoadConfigFromFileAsync<DatabaseConfiguration>(envConfigFile);
            return envSpecificConfig ?? CreateDefaultConfiguration(environment);
        }

        return CreateDefaultConfiguration(environment);
    }

    public async Task<EnvironmentConfiguration> LoadEnvironmentConfigurationAsync()
    {
        if (_configuration != null)
            return _configuration;

        var configFile = Path.Combine(_configPath, DefaultConfigFileName);
        
        if (File.Exists(configFile))
        {
            _configuration = await LoadConfigFromFileAsync<EnvironmentConfiguration>(configFile);
        }

        _configuration ??= CreateDefaultEnvironmentConfiguration();
        return _configuration;
    }

    public async Task SaveConfigurationAsync(EnvironmentConfiguration configuration)
    {
        var configFile = Path.Combine(_configPath, DefaultConfigFileName);
        await SaveConfigToFileAsync(configFile, configuration);
        _configuration = configuration;
    }

    public async Task InitializeConfigurationAsync(string environment = "development")
    {
        var configFile = Path.Combine(_configPath, DefaultConfigFileName);
        
        if (File.Exists(configFile))
        {
            throw new InvalidOperationException($"Configuration file already exists: {configFile}");
        }

        var config = CreateDefaultEnvironmentConfiguration();
        config.DefaultEnvironment = environment;
        config.Environments[environment] = CreateDefaultConfiguration(environment);

        await SaveConfigurationAsync(config);
    }

    public async Task AddEnvironmentAsync(string environment, DatabaseConfiguration? configuration = null)
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        
        if (envConfig.Environments.ContainsKey(environment))
        {
            throw new InvalidOperationException($"Environment '{environment}' already exists");
        }

        configuration ??= CreateDefaultConfiguration(environment);
        envConfig.Environments[environment] = configuration;
        
        await SaveConfigurationAsync(envConfig);
    }

    public async Task<List<string>> GetEnvironmentsAsync()
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        return envConfig.Environments.Keys.ToList();
    }

    public async Task<bool> EnvironmentExistsAsync(string environment)
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        return envConfig.Environments.ContainsKey(environment);
    }

    public async Task UpdateEnvironmentConfigurationAsync(string environment, DatabaseConfiguration configuration)
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        envConfig.Environments[environment] = configuration;
        await SaveConfigurationAsync(envConfig);
    }

    public async Task RemoveEnvironmentAsync(string environment)
    {
        var envConfig = await LoadEnvironmentConfigurationAsync();
        
        if (envConfig.DefaultEnvironment == environment)
        {
            throw new InvalidOperationException($"Cannot remove default environment '{environment}'");
        }

        if (!envConfig.Environments.Remove(environment))
        {
            throw new InvalidOperationException($"Environment '{environment}' does not exist");
        }

        await SaveConfigurationAsync(envConfig);
    }

    public string GetConfigurationPath()
    {
        return Path.Combine(_configPath, DefaultConfigFileName);
    }

    public async Task<DatabaseConfiguration> MergeWithEnvironmentVariablesAsync(DatabaseConfiguration config)
    {
        // Override configuration with environment variables
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION");
        if (!string.IsNullOrEmpty(connectionString))
        {
            config.ConnectionString = connectionString;
        }

        var migrationsPath = Environment.GetEnvironmentVariable("MIGRATIONS_PATH");
        if (!string.IsNullOrEmpty(migrationsPath))
        {
            config.MigrationsPath = migrationsPath;
        }

        var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
        if (!string.IsNullOrEmpty(logLevel))
        {
            config.Logging.Level = logLevel;
        }

        var autoBackup = Environment.GetEnvironmentVariable("AUTO_BACKUP");
        if (bool.TryParse(autoBackup, out var autoBackupValue))
        {
            config.Backup.AutoBackupBeforeMigration = autoBackupValue;
        }

        return config;
    }

    private async Task<T?> LoadConfigFromFileAsync<T>(string filePath) where T : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load configuration from {filePath}: {ex.Message}", ex);
        }
    }

    private async Task SaveConfigToFileAsync<T>(string filePath, T configuration)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json);
    }

    private DatabaseConfiguration CreateDefaultConfiguration(string environment)
    {
        return new DatabaseConfiguration
        {
            Environment = environment,
            ConnectionString = GetDefaultConnectionString(environment),
            MigrationsPath = "./migrations",
            SchemaTable = "__dbmigrator_schema_migrations",
            CommandTimeout = 30,
            AutoCreateMigrationTable = true,
            Logging = new LoggingConfiguration
            {
                Level = environment == "production" ? "Warning" : "Info",
                EnableConsoleOutput = true,
                EnableFileOutput = environment == "production",
                LogFilePath = environment == "production" ? $"./logs/dbmigrator-{environment}.log" : null
            },
            Backup = new BackupConfiguration
            {
                AutoBackupBeforeMigration = environment == "production",
                BackupPath = "./backups",
                RetentionDays = environment == "production" ? 90 : 7
            },
            Validation = new ValidationConfiguration
            {
                ValidateBeforeApply = true,
                RequireDryRunForDestructive = environment == "production",
                CheckConflictsBeforeApply = true,
                AllowOutOfOrderMigrations = environment == "development"
            }
        };
    }

    private EnvironmentConfiguration CreateDefaultEnvironmentConfiguration()
    {
        return new EnvironmentConfiguration
        {
            DefaultEnvironment = "development",
            Environments = new Dictionary<string, DatabaseConfiguration>(),
            Global = new GlobalConfiguration
            {
                ConfigVersion = "1.0",
                ConflictResolution = new ConflictResolutionPolicy
                {
                    DefaultStrategy = "manual",
                    AutoResolveTimestampConflicts = false,
                    AllowForceApply = false
                }
            }
        };
    }

    private string GetDefaultConnectionString(string environment)
    {
        return environment switch
        {
            "production" => "Host=localhost;Database=myapp_prod;Username=postgres;Password=",
            "staging" => "Host=localhost;Database=myapp_staging;Username=postgres;Password=",
            "testing" => "Host=localhost;Database=myapp_test;Username=postgres;Password=",
            _ => "Host=localhost;Database=myapp_dev;Username=postgres;Password="
        };
    }
}