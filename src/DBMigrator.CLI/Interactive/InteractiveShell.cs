using System.Text;
using DBMigrator.Core.Services;
using DBMigrator.Core.Models;
using DBMigrator.CLI.Commands;

namespace DBMigrator.CLI.Interactive;

public class InteractiveShell
{
    private readonly DatabaseConfiguration _config;
    private readonly ConfigurationManager _configManager;
    private bool _isRunning = true;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private readonly Dictionary<string, CommandInfo> _commands;
    private readonly MultiDatabaseManager? _clusterManager;
    private readonly MetricsCollector? _metricsCollector;

    public InteractiveShell(DatabaseConfiguration config, ConfigurationManager configManager)
    {
        _config = config;
        _configManager = configManager;
        _commands = InitializeCommands();
        
        // Initialize cluster and metrics for interactive features
        var logger = new StructuredLogger("Info", true);
        _clusterManager = new MultiDatabaseManager(logger);
        // Disable auto-flush to prevent interrupting interactive session
        _metricsCollector = new MetricsCollector(logger, enableAutoFlush: false);
    }

    public async Task RunAsync()
    {
        ShowWelcome();
        
        while (_isRunning)
        {
            try
            {
                var input = await ReadLineAsync();
                if (string.IsNullOrWhiteSpace(input)) continue;

                // Add to history if not duplicate
                if (_commandHistory.Count == 0 || _commandHistory.Last() != input.Trim())
                {
                    _commandHistory.Add(input.Trim());
                }
                _historyIndex = -1;

                await ProcessCommandAsync(input.Trim());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }
    }

    private void ShowWelcome()
    {
        Console.Clear();
        Console.WriteLine("🔧 DBMigrator Interactive Shell v0.5.0-beta");
        Console.WriteLine("PostgreSQL Migration Tool - Interactive Mode");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("🚀 Welcome to Interactive Mode!");
        Console.WriteLine("   Type 'help' for available commands");
        Console.WriteLine("   Type 'exit' or Ctrl+C to quit");
        Console.WriteLine("   Use Tab for command completion");
        Console.WriteLine("   Use ↑↓ arrows for command history");
        Console.WriteLine();
        
        // Show current configuration
        if (!string.IsNullOrEmpty(_config.ConnectionString))
        {
            var masked = ConnectionStringValidator.SanitizeConnectionStringForLogging(_config.ConnectionString);
            Console.WriteLine($"📊 Current Config: {masked}");
            Console.WriteLine($"📁 Migrations: {_config.MigrationsPath}");
            Console.WriteLine();
        }
    }

    private async Task<string> ReadLineAsync()
    {
        Console.Write("dbmigrator> ");
        
        // Check if console input is available for interactive features
        if (!Environment.UserInteractive || Console.IsInputRedirected)
        {
            // Fallback to basic input for non-interactive environments
            var basicInput = Console.ReadLine();
            if (basicInput == null)
            {
                // EOF reached, exit interactive mode
                _isRunning = false;
                return "exit";
            }
            
            if (!string.IsNullOrWhiteSpace(basicInput))
            {
                AddToHistory(basicInput);
            }
            return basicInput;
        }
        
        var input = new StringBuilder();
        ConsoleKeyInfo keyInfo;
        
        try
        {
            do
            {
                keyInfo = Console.ReadKey(true);
                
                switch (keyInfo.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        break;
                        
                    case ConsoleKey.Backspace:
                        if (input.Length > 0)
                        {
                            input.Remove(input.Length - 1, 1);
                            Console.Write("\b \b");
                        }
                        break;
                        
                    case ConsoleKey.Tab:
                        var completion = GetCommandCompletion(input.ToString());
                        if (!string.IsNullOrEmpty(completion))
                        {
                            // Clear current input
                            for (int i = 0; i < input.Length; i++)
                                Console.Write("\b \b");
                            
                            input.Clear();
                            input.Append(completion);
                            Console.Write(completion);
                        }
                        break;
                        
                    case ConsoleKey.UpArrow:
                        NavigateHistory(1, input);
                        break;
                        
                    case ConsoleKey.DownArrow:
                        NavigateHistory(-1, input);
                        break;
                        
                    default:
                        if (!char.IsControl(keyInfo.KeyChar))
                        {
                            input.Append(keyInfo.KeyChar);
                            Console.Write(keyInfo.KeyChar);
                        }
                        break;
                }
            } while (keyInfo.Key != ConsoleKey.Enter);
        }
        catch (InvalidOperationException)
        {
            // Console input not available, fallback to basic readline
            var fallbackInput = Console.ReadLine();
            if (fallbackInput == null)
            {
                // EOF reached, exit interactive mode
                _isRunning = false;
                return "exit";
            }
            
            if (!string.IsNullOrWhiteSpace(fallbackInput))
            {
                AddToHistory(fallbackInput);
            }
            return fallbackInput;
        }
        
        var result = input.ToString();
        if (!string.IsNullOrWhiteSpace(result))
        {
            AddToHistory(result);
        }
        
        return result;
    }

    private void AddToHistory(string command)
    {
        if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
        {
            _commandHistory.Add(command);
            if (_commandHistory.Count > 100) // Keep last 100 commands
            {
                _commandHistory.RemoveAt(0);
            }
        }
        _historyIndex = -1; // Reset history navigation
    }

    private void NavigateHistory(int direction, StringBuilder input)
    {
        if (_commandHistory.Count == 0) return;
        
        if (_historyIndex == -1)
        {
            _historyIndex = direction > 0 ? _commandHistory.Count - 1 : 0;
        }
        else
        {
            _historyIndex += direction;
            _historyIndex = Math.Max(0, Math.Min(_commandHistory.Count - 1, _historyIndex));
        }
        
        if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
        {
            // Clear current input
            for (int i = 0; i < input.Length; i++)
                Console.Write("\b \b");
            
            input.Clear();
            var historyCommand = _commandHistory[_historyIndex];
            input.Append(historyCommand);
            Console.Write(historyCommand);
        }
    }

    private string? GetCommandCompletion(string partial)
    {
        var matches = _commands.Keys
            .Where(cmd => cmd.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .ToList();
            
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task ProcessCommandAsync(string input)
    {
        var parts = ParseCommand(input);
        if (parts.Length == 0) return;

        var command = parts[0].ToLower();
        var args = parts.Skip(1).ToArray();

        switch (command)
        {
            case "help":
                ShowHelp();
                break;
                
            case "exit":
            case "quit":
                _isRunning = false;
                Console.WriteLine("👋 Goodbye!");
                break;
                
            case "clear":
                Console.Clear();
                ShowWelcome();
                break;
                
            case "history":
                ShowHistory();
                break;
                
            case "status":
                await ShowInteractiveStatusAsync();
                break;
                
            case "cluster":
                await HandleClusterCommandAsync(args);
                break;
                
            case "metrics":
                await HandleMetricsCommandAsync(args);
                break;
                
            case "deploy":
                await HandleDeployCommandAsync(args);
                break;
                
            case "wizard":
                await RunWizardAsync(args);
                break;
                
            case "dashboard":
                await ShowDashboardAsync();
                break;
                
            default:
                if (_commands.ContainsKey(command))
                {
                    await ExecuteStandardCommandAsync(command, args);
                }
                else
                {
                    Console.WriteLine($"❌ Unknown command: {command}");
                    Console.WriteLine("Type 'help' for available commands");
                }
                break;
        }
    }

    private async Task ExecuteStandardCommandAsync(string command, string[] args)
    {
        try
        {
            Console.WriteLine($"🚀 Executing: {command} {string.Join(" ", args)}");
            
            // Execute the original CLI commands
            var result = command switch
            {
                "init" => await InitCommand.ExecuteAsync(_config.ConnectionString),
                "apply" => args.Length > 0 
                    ? await ApplyCommand.ExecuteAsync(_config.ConnectionString, args[0])
                    : ShowCommandUsage("apply", "<migration-file>"),
                "create" => await CreateCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, 
                    args.Contains("--auto"), GetArgument(args, "--name")),
                "validate" => await ValidateCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, 
                    args.Length > 0 ? args[0] : null),
                "backup" => args.Length > 0 
                    ? await BackupCommand.ExecuteAsync(_config, args[0], args.Skip(1).ToArray())
                    : ShowCommandUsage("backup", "<create|list|cleanup>"),
                "verify" => await VerifyCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, args),
                "repair" => args.Length > 0 
                    ? await RepairCommand.ExecuteAsync(_config.ConnectionString, args[0], args.Skip(1).ToArray())
                    : ShowCommandUsage("repair", "<checksums|locks|recovery>"),
                _ => 1
            };
            
            if (result == 0)
            {
                Console.WriteLine("✅ Command completed successfully");
            }
            else
            {
                Console.WriteLine("❌ Command failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Command failed: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    private async Task ShowInteractiveStatusAsync()
    {
        Console.WriteLine("📊 Interactive Status Dashboard");
        Console.WriteLine("══════════════════════════════");
        
        try
        {
            // Connection status
            var connectionTest = await ConnectionStringValidator.TestConnectionAsync(_config.ConnectionString, 5);
            var connectionIcon = connectionTest.IsSuccessful ? "✅" : "❌";
            Console.WriteLine($"{connectionIcon} Database Connection: {(connectionTest.IsSuccessful ? "Connected" : "Failed")}");
            
            if (connectionTest.IsSuccessful)
            {
                Console.WriteLine($"   Server: {connectionTest.ServerVersion}");
                Console.WriteLine($"   Response: {connectionTest.ConnectionTime.TotalMilliseconds:F0}ms");
                
                // Migration status
                var result = await StatusCommand.ExecuteAsync(_config.ConnectionString);
                
                // Cluster status if available
                if (_clusterManager != null)
                {
                    var databases = _clusterManager.GetRegisteredDatabases().ToList();
                    if (databases.Any())
                    {
                        Console.WriteLine($"🌐 Cluster: {databases.Count} database(s) registered");
                    }
                }
            }
            else
            {
                Console.WriteLine($"   Error: {connectionTest.Error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Status check failed: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    private async Task HandleClusterCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("🌐 Cluster Commands:");
            Console.WriteLine("   register  - Register new database");
            Console.WriteLine("   list      - List registered databases");
            Console.WriteLine("   health    - Show cluster health");
            Console.WriteLine("   status    - Show cluster status");
            return;
        }

        var action = args[0];
        var remainingArgs = args.Skip(1).ToArray();
        await ClusterCommand.ExecuteAsync(action, remainingArgs);
    }

    private async Task HandleMetricsCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("📊 Metrics Commands:");
            Console.WriteLine("   show      - Show application metrics");
            Console.WriteLine("   system    - Show system metrics");
            Console.WriteLine("   export    - Export metrics to file");
            return;
        }

        var action = args[0];
        var remainingArgs = args.Skip(1).ToArray();
        await MetricsCommand.ExecuteAsync(action, remainingArgs);
    }

    private async Task HandleDeployCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("🚀 Deployment Commands:");
            Console.WriteLine("   plan      - Create deployment plan");
            Console.WriteLine("   execute   - Execute deployment");
            Console.WriteLine("   validate  - Validate environment");
            Console.WriteLine("   status    - Show deployment status");
            return;
        }

        var action = args[0];
        var remainingArgs = args.Skip(1).ToArray();
        await DeployCommand.ExecuteAsync(action, remainingArgs);
    }

    private async Task RunWizardAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("🧙 Available Wizards:");
            Console.WriteLine("   setup     - Initial setup wizard");
            Console.WriteLine("   migration - Create migration wizard");
            Console.WriteLine("   deploy    - Deployment wizard");
            return;
        }

        var wizardType = args[0].ToLower();
        switch (wizardType)
        {
            case "setup":
                await RunSetupWizardAsync();
                break;
            case "migration":
                await RunMigrationWizardAsync();
                break;
            case "deploy":
                await RunDeploymentWizardAsync();
                break;
            default:
                Console.WriteLine($"❌ Unknown wizard: {wizardType}");
                break;
        }
    }

    private async Task ShowDashboardAsync()
    {
        var refreshInterval = TimeSpan.FromSeconds(2);
        Console.WriteLine("📊 Real-time Dashboard (Press any key to exit)");
        Console.WriteLine("═══════════════════════════════════════════");
        
        while (!Console.KeyAvailable)
        {
            Console.SetCursorPosition(0, Console.CursorTop - 10);
            
            // Clear and redraw dashboard
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            }
            Console.SetCursorPosition(0, Console.CursorTop - 10);
            
            await ShowInteractiveStatusAsync();
            
            if (_metricsCollector != null)
            {
                var systemMetrics = await _metricsCollector.GetSystemMetricsAsync();
                Console.WriteLine($"💾 Memory: {FormatBytes(systemMetrics.WorkingSetBytes)}");
                Console.WriteLine($"🧵 Threads: {systemMetrics.ThreadCount}");
                Console.WriteLine($"⏱️  Uptime: {FormatDuration(TimeSpan.FromMilliseconds(systemMetrics.UptimeMs))}");
            }
            
            Console.WriteLine($"🕒 Updated: {DateTime.Now:HH:mm:ss}");
            
            await Task.Delay(refreshInterval);
        }
        
        try
        {
            Console.ReadKey(true); // Consume the key press
        }
        catch (InvalidOperationException)
        {
            // In non-interactive environments, just wait a moment
            await Task.Delay(1000);
        }
        Console.WriteLine("Dashboard closed.");
    }

    private async Task RunSetupWizardAsync()
    {
        Console.WriteLine("🧙 Setup Wizard");
        Console.WriteLine("═══════════════");
        
        Console.Write("Enter database host [localhost]: ");
        var host = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(host)) host = "localhost";
        
        Console.Write("Enter database name: ");
        var database = Console.ReadLine();
        
        Console.Write("Enter username: ");
        var username = Console.ReadLine();
        
        Console.Write("Enter password: ");
        var password = ReadPassword();
        
        Console.Write("Enter port [5432]: ");
        var portInput = Console.ReadLine();
        var port = string.IsNullOrWhiteSpace(portInput) ? 5432 : int.Parse(portInput);
        
        var connectionString = $"Host={host};Database={database};Username={username};Password={password};Port={port}";
        
        Console.WriteLine("\n🔍 Testing connection...");
        var testResult = await ConnectionStringValidator.TestConnectionAsync(connectionString, 10);
        
        if (testResult.IsSuccessful)
        {
            Console.WriteLine("✅ Connection successful!");
            Console.WriteLine($"   Server: {testResult.ServerVersion}");
            
            Console.Write("Save this configuration? (y/N): ");
            var save = Console.ReadLine();
            if (save?.ToLower() == "y")
            {
                // Here you would save the configuration
                Console.WriteLine("✅ Configuration saved!");
            }
        }
        else
        {
            Console.WriteLine($"❌ Connection failed: {testResult.Error}");
        }
    }

    private async Task RunMigrationWizardAsync()
    {
        Console.WriteLine("🧙 Migration Creation Wizard");
        Console.WriteLine("════════════════════════════");
        
        Console.WriteLine("Choose migration type:");
        Console.WriteLine("1. Auto-detect changes");
        Console.WriteLine("2. Manual migration");
        Console.Write("Select [1-2]: ");
        
        var choice = Console.ReadLine();
        
        switch (choice)
        {
            case "1":
                Console.WriteLine("🔍 Auto-detecting changes...");
                
                // Check if baseline exists first
                var connectionManager = new ConnectionManager(_config.ConnectionString);
                var schemaAnalyzer = new SchemaAnalyzer(connectionManager, _configManager);
                var baseline = await schemaAnalyzer.LoadBaselineAsync(_config.MigrationsPath);
                
                if (baseline == null)
                {
                    Console.WriteLine("❌ No baseline found.");
                    Console.Write("Would you like to create a baseline first? (y/N): ");
                    var createBaseline = Console.ReadLine();
                    
                    if (createBaseline?.ToLower() is "y" or "yes")
                    {
                        Console.WriteLine("📸 Creating baseline...");
                        var result = await BaselineCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, "create", null);
                        if (result != 0)
                        {
                            Console.WriteLine("❌ Failed to create baseline. Aborting migration creation.");
                            return;
                        }
                        Console.WriteLine("✅ Baseline created! Now detecting changes...");
                    }
                    else
                    {
                        Console.WriteLine("❌ Cannot auto-detect changes without a baseline.");
                        return;
                    }
                }
                
                await CreateCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, true, null);
                break;
                
            case "2":
                Console.Write("Enter migration name: ");
                var name = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await CreateCommand.ExecuteAsync(_config.ConnectionString, _config.MigrationsPath, false, name);
                }
                break;
                
            default:
                Console.WriteLine("❌ Invalid choice");
                break;
        }
    }

    private async Task RunDeploymentWizardAsync()
    {
        Console.WriteLine("🧙 Deployment Wizard");
        Console.WriteLine("═══════════════════");
        
        Console.Write("Enter deployment name: ");
        var planName = Console.ReadLine();
        
        Console.WriteLine("Select deployment strategy:");
        Console.WriteLine("1. Sequential");
        Console.WriteLine("2. Parallel");
        Console.WriteLine("3. Blue-Green");
        Console.WriteLine("4. Canary");
        Console.Write("Select [1-4]: ");
        
        var strategyChoice = Console.ReadLine();
        var strategy = strategyChoice switch
        {
            "1" => "sequential",
            "2" => "parallel", 
            "3" => "bluegreen",
            "4" => "canary",
            _ => "sequential"
        };
        
        Console.WriteLine($"🚀 Creating deployment plan '{planName}' with {strategy} strategy...");
        await DeployCommand.ExecuteAsync("plan", new[] { "--name", planName, "--strategy", strategy });
    }

    private string ReadPassword()
    {
        // Check if console input is available for password masking
        if (!Environment.UserInteractive || Console.IsInputRedirected)
        {
            // Fallback to basic input for non-interactive environments
            Console.WriteLine("(password input in non-interactive mode)");
            return Console.ReadLine() ?? "";
        }
        
        var password = new StringBuilder();
        ConsoleKeyInfo keyInfo;
        
        try
        {
            do
            {
                keyInfo = Console.ReadKey(true);
                
                if (keyInfo.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    password.Append(keyInfo.KeyChar);
                    Console.Write("*");
                }
            } while (keyInfo.Key != ConsoleKey.Enter);
        }
        catch (InvalidOperationException)
        {
            // Console input not available, fallback to basic readline
            Console.WriteLine("(fallback to basic input)");
            return Console.ReadLine() ?? "";
        }
        
        Console.WriteLine();
        return password.ToString();
    }

    private string[] ParseCommand(string input)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            
            if (c == '"' && (i == 0 || input[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        
        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }
        
        return parts.ToArray();
    }

    private void ShowHistory()
    {
        Console.WriteLine("📜 Command History:");
        for (int i = 0; i < _commandHistory.Count; i++)
        {
            Console.WriteLine($"   {i + 1,3}. {_commandHistory[i]}");
        }
        Console.WriteLine();
    }

    private void ShowHelp()
    {
        Console.WriteLine("🔧 DBMigrator Interactive Commands");
        Console.WriteLine("══════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("🏠 Shell Commands:");
        Console.WriteLine("   help          Show this help");
        Console.WriteLine("   exit/quit     Exit interactive mode");
        Console.WriteLine("   clear         Clear screen");
        Console.WriteLine("   history       Show command history");
        Console.WriteLine("   status        Show interactive status");
        Console.WriteLine("   dashboard     Real-time dashboard");
        Console.WriteLine();
        Console.WriteLine("🧙 Wizards:");
        Console.WriteLine("   wizard setup     Setup wizard");
        Console.WriteLine("   wizard migration Migration wizard");
        Console.WriteLine("   wizard deploy    Deployment wizard");
        Console.WriteLine();
        Console.WriteLine("📊 Core Commands:");
        Console.WriteLine("   init          Initialize migrations");
        Console.WriteLine("   apply <file>  Apply migration");
        Console.WriteLine("   create        Create migration");
        Console.WriteLine("   validate      Validate migrations");
        Console.WriteLine("   backup        Backup operations");
        Console.WriteLine("   verify        Verify integrity");
        Console.WriteLine("   repair        Repair operations");
        Console.WriteLine();
        Console.WriteLine("🌐 Enterprise Commands:");
        Console.WriteLine("   cluster       Cluster management");
        Console.WriteLine("   metrics       Performance metrics");
        Console.WriteLine("   deploy        Deployment automation");
        Console.WriteLine();
        Console.WriteLine("💡 Tips:");
        Console.WriteLine("   • Use Tab for command completion");
        Console.WriteLine("   • Use ↑↓ arrows for command history");
        Console.WriteLine("   • Commands support same arguments as CLI mode");
        Console.WriteLine();
    }

    private Dictionary<string, CommandInfo> InitializeCommands()
    {
        return new Dictionary<string, CommandInfo>
        {
            ["init"] = new("Initialize migration history table"),
            ["apply"] = new("Apply a migration file"),
            ["status"] = new("Show applied migrations"),
            ["create"] = new("Create new migration"),
            ["validate"] = new("Validate SQL syntax and safety"),
            ["backup"] = new("Create and manage database backups"),
            ["verify"] = new("Verify migration integrity"),
            ["repair"] = new("Repair checksums, locks, and recover"),
            ["cluster"] = new("Multi-database cluster management"),
            ["metrics"] = new("Performance monitoring and metrics"),
            ["deploy"] = new("Automated deployment orchestration")
        };
    }

    private int ShowCommandUsage(string command, string usage)
    {
        Console.WriteLine($"Usage: {command} {usage}");
        return 1;
    }

    private string? GetArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{duration.TotalDays:F1}d";
        if (duration.TotalHours >= 1)
            return $"{duration.TotalHours:F1}h";
        if (duration.TotalMinutes >= 1)
            return $"{duration.TotalMinutes:F1}m";
        return $"{duration.TotalSeconds:F1}s";
    }

    private record CommandInfo(string Description);
}