using DBMigrator.CLI.Commands;
using DBMigrator.Core.Services;

namespace DBMigrator.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("🔧 DBMigrator CLI v0.5.0-beta");
        Console.WriteLine("PostgreSQL Migration Tool - MVP 5 (Enterprise Ready)");
        Console.WriteLine();

        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var command = args[0].ToLower();

        // Help command doesn't need connection string
        if (command is "help" or "--help" or "-h")
        {
            return ShowHelp();
        }

        // Load configuration (with environment variable override)
        var configManager = new ConfigurationManager();
        
        // Check for environment parameter
        string? environment = null;
        var envIndex = Array.IndexOf(args, "--env");
        if (envIndex >= 0 && envIndex + 1 < args.Length)
        {
            environment = args[envIndex + 1];
        }
        
        var config = await configManager.LoadConfigurationAsync(environment);
        config = await configManager.MergeWithEnvironmentVariablesAsync(config);

        if (string.IsNullOrEmpty(config.ConnectionString))
        {
            Console.WriteLine("❌ No connection string found.");
            Console.WriteLine("Either set DB_CONNECTION environment variable or create dbmigrator.json");
            Console.WriteLine("Example: export DB_CONNECTION=\"Host=localhost;Database=myapp;Username=dev;Password=mypass\"");
            return 1;
        }

        try
        {
            return command switch
            {
                "init" => await InitCommand.ExecuteAsync(config.ConnectionString),
                "apply" => args.Length > 1 
                    ? await ApplyCommand.ExecuteAsync(config.ConnectionString, args[1])
                    : HandleMissingArgument("apply", "filename"),
                "status" => await StatusCommand.ExecuteAsync(config.ConnectionString),
                "create" => await HandleCreateCommand(args, config),
                "baseline" => await HandleBaselineCommand(args, config),
                "diff" => await DiffCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath),
                "down" => await HandleDownCommand(args, config),
                
                // MVP 3 commands
                "dry-run" => await HandleDryRunCommand(args, config),
                "check-conflicts" => await HandleCheckConflictsCommand(args, config),
                "list" => await HandleListCommand(args, config),
                "config" => await HandleConfigCommand(args, configManager),
                
                // MVP 4 commands (Production Ready)
                "validate" => await HandleValidateCommand(args, config),
                "backup" => await HandleBackupCommand(args, config),
                "repair" => await HandleRepairCommand(args, config),
                "verify" => await HandleVerifyCommand(args, config),
                
                // MVP 5 commands (Enterprise Ready)
                "cluster" => await HandleClusterCommand(args),
                "metrics" => await HandleMetricsCommand(args),
                "deploy" => await HandleDeployCommand(args),
                
                // Interactive Mode
                "interactive" => await HandleInteractiveCommand(config, configManager),
                "shell" => await HandleInteractiveCommand(config, configManager),
                
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCreateCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        var autoDetect = args.Contains("--auto");
        string? migrationName = null;
        
        var nameIndex = Array.IndexOf(args, "--name");
        if (nameIndex >= 0 && nameIndex + 1 < args.Length)
        {
            migrationName = args[nameIndex + 1];
        }

        return await CreateCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, autoDetect, migrationName);
    }

    private static async Task<int> HandleBaselineCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing baseline action. Use: create, show");
            return 1;
        }

        var action = args[1];
        string? baselineName = null;
        
        var nameIndex = Array.IndexOf(args, "--name");
        if (nameIndex >= 0 && nameIndex + 1 < args.Length)
        {
            baselineName = args[nameIndex + 1];
        }

        return await BaselineCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, action, baselineName);
    }

    private static async Task<int> HandleDownCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        var count = 1;
        var countIndex = Array.IndexOf(args, "--count");
        if (countIndex >= 0 && countIndex + 1 < args.Length)
        {
            if (int.TryParse(args[countIndex + 1], out var parsedCount))
            {
                count = parsedCount;
            }
        }

        return await DownCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, count);
    }

    private static async Task<int> HandleDryRunCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing migration file for dry run");
            Console.WriteLine("Usage: dbmigrator dry-run <migration-file>");
            return 1;
        }

        var migrationFile = args[1];
        return await DryRunCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, migrationFile);
    }

    private static async Task<int> HandleCheckConflictsCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        return await CheckConflictsCommand.ExecuteAsync(config.MigrationsPath);
    }

    private static async Task<int> HandleListCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        var showApplied = args.Contains("--applied");
        var showPending = args.Contains("--pending");
        var showAll = !showApplied && !showPending;

        return await ListCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, showApplied, showPending, showAll);
    }

    private static async Task<int> HandleConfigCommand(string[] args, ConfigurationManager configManager)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing config action. Use: init, show, env");
            return 1;
        }

        var action = args[1];
        return await ConfigCommand.ExecuteAsync(configManager, action, args);
    }

    private static async Task<int> HandleValidateCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        string? migrationFile = null;
        
        if (args.Length > 1 && !args[1].StartsWith("--"))
        {
            migrationFile = args[1];
        }

        return await ValidateCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, migrationFile);
    }

    private static async Task<int> HandleBackupCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing backup action. Use: create, list, cleanup");
            return 1;
        }

        var action = args[1];
        var remainingArgs = args.Skip(2).ToArray();
        
        return await BackupCommand.ExecuteAsync(config, action, remainingArgs);
    }

    private static async Task<int> HandleRepairCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing repair action. Use: checksums, locks, recovery");
            return 1;
        }

        var action = args[1];
        var remainingArgs = args.Skip(2).ToArray();
        
        return await RepairCommand.ExecuteAsync(config.ConnectionString, action, remainingArgs);
    }

    private static async Task<int> HandleVerifyCommand(string[] args, DBMigrator.Core.Models.DatabaseConfiguration config)
    {
        var remainingArgs = args.Skip(1).ToArray();
        return await VerifyCommand.ExecuteAsync(config.ConnectionString, config.MigrationsPath, remainingArgs);
    }

    private static async Task<int> HandleClusterCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing cluster action. Use: register, list, health, apply, status");
            return 1;
        }

        var action = args[1];
        var remainingArgs = args.Skip(2).ToArray();
        
        return await ClusterCommand.ExecuteAsync(action, remainingArgs);
    }

    private static async Task<int> HandleMetricsCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing metrics action. Use: show, system, export, clear");
            return 1;
        }

        var action = args[1];
        var remainingArgs = args.Skip(2).ToArray();
        
        return await MetricsCommand.ExecuteAsync(action, remainingArgs);
    }

    private static async Task<int> HandleDeployCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("❌ Missing deploy action. Use: plan, execute, validate, status, rollback");
            return 1;
        }

        var action = args[1];
        var remainingArgs = args.Skip(2).ToArray();
        
        return await DeployCommand.ExecuteAsync(action, remainingArgs);
    }

    private static async Task<int> HandleInteractiveCommand(DBMigrator.Core.Models.DatabaseConfiguration config, ConfigurationManager configManager)
    {
        try
        {
            var shell = new DBMigrator.CLI.Interactive.InteractiveShell(config, configManager);
            await shell.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Interactive mode failed: {ex.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("Usage: dbmigrator <command> [options] [--env <environment>]");
        Console.WriteLine();
        Console.WriteLine("Core Commands:");
        Console.WriteLine("  init                    Initialize migration history table");
        Console.WriteLine("  apply <file.sql>        Apply a migration file");
        Console.WriteLine("  status                  Show applied migrations");
        Console.WriteLine("  create [--auto] [--name <name>]  Create migration");
        Console.WriteLine("  baseline <action>       Manage baseline (create, show)");
        Console.WriteLine("  diff                    Show differences from baseline");
        Console.WriteLine("  down [--count <n>]      Rollback n migrations (default: 1)");
        Console.WriteLine();
        Console.WriteLine("Team Collaboration (MVP 3):");
        Console.WriteLine("  dry-run <file.sql>      Simulate migration without applying");
        Console.WriteLine("  check-conflicts         Detect migration conflicts");
        Console.WriteLine("  list [--applied|--pending]  List migrations");
        Console.WriteLine("  config <action>         Manage configuration (init, show, env)");
        Console.WriteLine();
        Console.WriteLine("Production Ready (MVP 4):");
        Console.WriteLine("  validate [file.sql]     Validate SQL syntax and safety");
        Console.WriteLine("  backup <action>         Create and manage database backups");
        Console.WriteLine("  repair <action>         Repair checksums, locks, and recover");
        Console.WriteLine("  verify <action>         Verify migration integrity");
        Console.WriteLine();
        Console.WriteLine("Enterprise Ready (MVP 5):");
        Console.WriteLine("  cluster <action>        Multi-database cluster management");
        Console.WriteLine("  metrics <action>        Performance monitoring and metrics");
        Console.WriteLine("  deploy <action>         Automated deployment orchestration");
        Console.WriteLine("  interactive             Start interactive shell mode");
        Console.WriteLine("  shell                   Alias for interactive mode");
        Console.WriteLine();
        Console.WriteLine("Global Options:");
        Console.WriteLine("  --env <environment>     Use specific environment configuration");
        Console.WriteLine("  --help, -h              Show this help message");
        Console.WriteLine();
        Console.WriteLine("Configuration:");
        Console.WriteLine("  DB_CONNECTION           PostgreSQL connection string");
        Console.WriteLine("  dbmigrator.json         Multi-environment configuration file");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # Basic setup");
        Console.WriteLine("  export DB_CONNECTION=\"Host=localhost;Database=myapp;Username=dev\"");
        Console.WriteLine("  dbmigrator init");
        Console.WriteLine("  dbmigrator baseline create");
        Console.WriteLine();
        Console.WriteLine("  # Auto-detect changes and create migration");
        Console.WriteLine("  dbmigrator create --auto");
        Console.WriteLine();
        Console.WriteLine("  # Manual migration");
        Console.WriteLine("  dbmigrator create --name \"add_user_table\"");
        Console.WriteLine("  dbmigrator apply 20241201120000_manual_add_user_table.sql");
        Console.WriteLine();
        Console.WriteLine("  # Show differences");
        Console.WriteLine("  dbmigrator diff");
        Console.WriteLine();
        Console.WriteLine("  # Rollback");
        Console.WriteLine("  dbmigrator down --count 2");
        Console.WriteLine();
        Console.WriteLine("  # Production features (MVP 4)");
        Console.WriteLine("  dbmigrator validate");
        Console.WriteLine("  dbmigrator backup create --type full");
        Console.WriteLine("  dbmigrator verify checksums");
        Console.WriteLine("  dbmigrator repair locks --force");
        Console.WriteLine();
        Console.WriteLine("  # Enterprise features (MVP 5)");
        Console.WriteLine("  dbmigrator cluster register --name prod-db --connection \"Host=prod;Database=app\"");
        Console.WriteLine("  dbmigrator cluster apply migration.sql --databases prod-db,staging-db");
        Console.WriteLine("  dbmigrator metrics show");
        Console.WriteLine("  dbmigrator deploy plan --name release-v1.2 --strategy parallel");
        Console.WriteLine("  dbmigrator deploy execute --plan release-v1.2");
        Console.WriteLine();
        Console.WriteLine("  # Interactive mode");
        Console.WriteLine("  dbmigrator interactive");
        return 0;
    }

    private static int HandleMissingArgument(string command, string argument)
    {
        Console.WriteLine($"❌ Missing argument: {argument}");
        Console.WriteLine($"Usage: dbmigrator {command} <{argument}>");
        return 1;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.WriteLine($"❌ Unknown command: {command}");
        Console.WriteLine("Run 'dbmigrator help' for available commands");
        return 1;
    }
}
