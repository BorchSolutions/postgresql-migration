using DBMigrator.Core.Services;
using DBMigrator.Core.Models;
using System.Text.Json;

namespace DBMigrator.CLI.Commands;

public static class ConfigCommand
{
    public static async Task<int> ExecuteAsync(ConfigurationManager configManager, string action, string[] args)
    {
        try
        {
            return action.ToLower() switch
            {
                "init" => await InitializeConfig(configManager, args),
                "show" => await ShowConfig(configManager, args),
                "env" => await ManageEnvironments(configManager, args),
                _ => ShowConfigHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Configuration command failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> InitializeConfig(ConfigurationManager configManager, string[] args)
    {
        Console.WriteLine("🔧 Initializing configuration...");
        
        var environment = "development";
        var envIndex = Array.IndexOf(args, "--env");
        if (envIndex >= 0 && envIndex + 1 < args.Length)
        {
            environment = args[envIndex + 1];
        }

        try
        {
            await configManager.InitializeConfigurationAsync(environment);
            
            var configPath = configManager.GetConfigurationPath();
            Console.WriteLine($"✅ Configuration initialized: {configPath}");
            Console.WriteLine($"   Default environment: {environment}");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("1. Edit the configuration file to set your connection strings");
            Console.WriteLine("2. Add additional environments using 'dbmigrator config env add'");
            Console.WriteLine("3. Use '--env <name>' to specify environment for commands");
            
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            Console.WriteLine("Use 'dbmigrator config show' to view existing configuration");
            return 1;
        }
    }

    private static async Task<int> ShowConfig(ConfigurationManager configManager, string[] args)
    {
        var environment = args.Length > 2 ? args[2] : null;
        
        try
        {
            var envConfig = await configManager.LoadEnvironmentConfigurationAsync();
            
            Console.WriteLine("📋 Configuration Overview:");
            Console.WriteLine($"   Config file: {configManager.GetConfigurationPath()}");
            Console.WriteLine($"   Default environment: {envConfig.DefaultEnvironment}");
            Console.WriteLine($"   Available environments: {string.Join(", ", envConfig.Environments.Keys)}");
            Console.WriteLine();

            if (environment != null)
            {
                if (envConfig.Environments.TryGetValue(environment, out var config))
                {
                    await ShowEnvironmentConfig(environment, config);
                }
                else
                {
                    Console.WriteLine($"❌ Environment '{environment}' not found");
                    return 1;
                }
            }
            else
            {
                // Show all environments
                foreach (var env in envConfig.Environments)
                {
                    await ShowEnvironmentConfig(env.Key, env.Value);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to load configuration: {ex.Message}");
            Console.WriteLine("Use 'dbmigrator config init' to create a new configuration");
            return 1;
        }
    }

    private static async Task ShowEnvironmentConfig(string environmentName, DatabaseConfiguration config)
    {
        Console.WriteLine($"🌍 Environment: {environmentName}");
        Console.WriteLine($"   Connection: {SanitizeConnectionString(config.ConnectionString)}");
        Console.WriteLine($"   Migrations Path: {config.MigrationsPath}");
        Console.WriteLine($"   Schema Table: {config.SchemaTable}");
        Console.WriteLine($"   Command Timeout: {config.CommandTimeout}s");
        Console.WriteLine();
        
        Console.WriteLine($"   Logging:");
        Console.WriteLine($"     Level: {config.Logging.Level}");
        Console.WriteLine($"     Console Output: {config.Logging.EnableConsoleOutput}");
        Console.WriteLine($"     File Output: {config.Logging.EnableFileOutput}");
        if (!string.IsNullOrEmpty(config.Logging.LogFilePath))
        {
            Console.WriteLine($"     Log File: {config.Logging.LogFilePath}");
        }
        Console.WriteLine();
        
        Console.WriteLine($"   Validation:");
        Console.WriteLine($"     Validate Before Apply: {config.Validation.ValidateBeforeApply}");
        Console.WriteLine($"     Require Dry Run for Destructive: {config.Validation.RequireDryRunForDestructive}");
        Console.WriteLine($"     Check Conflicts: {config.Validation.CheckConflictsBeforeApply}");
        Console.WriteLine($"     Allow Out of Order: {config.Validation.AllowOutOfOrderMigrations}");
        Console.WriteLine();
        
        Console.WriteLine($"   Backup:");
        Console.WriteLine($"     Auto Backup: {config.Backup.AutoBackupBeforeMigration}");
        Console.WriteLine($"     Backup Path: {config.Backup.BackupPath}");
        Console.WriteLine($"     Retention Days: {config.Backup.RetentionDays}");
        Console.WriteLine();
    }

    private static async Task<int> ManageEnvironments(ConfigurationManager configManager, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("❌ Missing environment action. Use: add, remove, list");
            return 1;
        }

        var envAction = args[2].ToLower();
        
        return envAction switch
        {
            "add" => await AddEnvironment(configManager, args),
            "remove" => await RemoveEnvironment(configManager, args),
            "list" => await ListEnvironments(configManager),
            _ => ShowEnvironmentHelp()
        };
    }

    private static async Task<int> AddEnvironment(ConfigurationManager configManager, string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("❌ Missing environment name");
            Console.WriteLine("Usage: dbmigrator config env add <environment-name>");
            return 1;
        }

        var environmentName = args[3];
        
        try
        {
            await configManager.AddEnvironmentAsync(environmentName);
            Console.WriteLine($"✅ Environment '{environmentName}' added successfully");
            Console.WriteLine($"💡 Edit the configuration file to set connection string and other settings");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RemoveEnvironment(ConfigurationManager configManager, string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("❌ Missing environment name");
            Console.WriteLine("Usage: dbmigrator config env remove <environment-name>");
            return 1;
        }

        var environmentName = args[3];
        
        try
        {
            await configManager.RemoveEnvironmentAsync(environmentName);
            Console.WriteLine($"✅ Environment '{environmentName}' removed successfully");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ListEnvironments(ConfigurationManager configManager)
    {
        try
        {
            var environments = await configManager.GetEnvironmentsAsync();
            var envConfig = await configManager.LoadEnvironmentConfigurationAsync();
            
            Console.WriteLine("🌍 Available Environments:");
            
            if (!environments.Any())
            {
                Console.WriteLine("   No environments configured");
            }
            else
            {
                foreach (var env in environments)
                {
                    var isDefault = env == envConfig.DefaultEnvironment;
                    var defaultMarker = isDefault ? " (default)" : "";
                    Console.WriteLine($"   • {env}{defaultMarker}");
                }
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to list environments: {ex.Message}");
            return 1;
        }
    }

    private static int ShowConfigHelp()
    {
        Console.WriteLine("Usage: dbmigrator config <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  init [--env <name>]     Initialize configuration file");
        Console.WriteLine("  show [environment]      Show configuration (all or specific environment)");
        Console.WriteLine("  env add <name>          Add new environment");
        Console.WriteLine("  env remove <name>       Remove environment");
        Console.WriteLine("  env list               List all environments");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator config init");
        Console.WriteLine("  dbmigrator config init --env production");
        Console.WriteLine("  dbmigrator config show");
        Console.WriteLine("  dbmigrator config show production");
        Console.WriteLine("  dbmigrator config env add staging");
        Console.WriteLine("  dbmigrator config env list");
        
        return 1;
    }

    private static int ShowEnvironmentHelp()
    {
        Console.WriteLine("Usage: dbmigrator config env <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  add <name>              Add new environment");
        Console.WriteLine("  remove <name>           Remove environment");
        Console.WriteLine("  list                    List all environments");
        
        return 1;
    }

    private static string SanitizeConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "[Not configured]";

        // Remove sensitive information for display
        var sanitized = connectionString;
        var sensitiveKeys = new[] { "password", "pwd" };

        foreach (var key in sensitiveKeys)
        {
            var pattern = $@"{key}=[^;]*;?";
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, 
                pattern, 
                $"{key}=***;", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        return sanitized;
    }
}