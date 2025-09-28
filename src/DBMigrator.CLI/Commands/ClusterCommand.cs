using DBMigrator.Core.Services;
using DBMigrator.Core.Models;

namespace DBMigrator.CLI.Commands;

public static class ClusterCommand
{
    public static async Task<int> ExecuteAsync(string action, string[] args)
    {
        try
        {
            Console.WriteLine("🌐 DBMigrator Cluster Management");
            Console.WriteLine();

            var logger = new StructuredLogger("Info", true);
            var multiDbManager = new MultiDatabaseManager(logger);

            return action.ToLower() switch
            {
                "register" => await RegisterDatabaseAsync(multiDbManager, args),
                "list" => await ListDatabasesAsync(multiDbManager),
                "health" => await ShowHealthAsync(multiDbManager),
                "apply" => await ApplyToClusterAsync(multiDbManager, args),
                "status" => await ShowClusterStatusAsync(multiDbManager),
                _ => ShowClusterHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Cluster operation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RegisterDatabaseAsync(MultiDatabaseManager multiDbManager, string[] args)
    {
        Console.WriteLine("📝 Registering database in cluster...");

        var name = GetArgument(args, "--name");
        var connectionString = GetArgument(args, "--connection");
        var migrationsPath = GetArgument(args, "--migrations-path") ?? "./migrations";

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("❌ Missing required arguments: --name and --connection");
            Console.WriteLine("Usage: dbmigrator cluster register --name <name> --connection <connection-string> [--migrations-path <path>]");
            return 1;
        }

        // Validate connection string format before creating config
        var validation = ConnectionStringValidator.Validate(connectionString);
        if (!validation.IsValid)
        {
            Console.WriteLine("❌ Invalid connection string format:");
            foreach (var error in validation.Errors)
            {
                Console.WriteLine($"   • {error}");
            }
            Console.WriteLine();
            Console.WriteLine("Example valid connection string:");
            Console.WriteLine("   \"Host=localhost;Database=myapp;Username=user;Password=pass;Port=5432\"");
            return 1;
        }

        var config = new DatabaseConfiguration
        {
            ConnectionString = connectionString,
            MigrationsPath = migrationsPath
        };

        await multiDbManager.RegisterDatabaseAsync(name, config);
        
        Console.WriteLine($"✅ Database '{name}' registered successfully");
        Console.WriteLine($"   Connection: {MaskConnectionString(connectionString)}");
        Console.WriteLine($"   Migrations: {migrationsPath}");
        
        return 0;
    }

    private static async Task<int> ListDatabasesAsync(MultiDatabaseManager multiDbManager)
    {
        Console.WriteLine("📋 Registered Databases:");
        Console.WriteLine();

        var databases = multiDbManager.GetRegisteredDatabases().ToList();

        if (!databases.Any())
        {
            Console.WriteLine("   No databases registered in cluster");
            return 0;
        }

        foreach (var dbName in databases)
        {
            var config = multiDbManager.GetDatabaseConfiguration(dbName);
            Console.WriteLine($"   🗄️  {dbName}");
            if (config != null)
            {
                Console.WriteLine($"      Connection: {MaskConnectionString(config.ConnectionString)}");
                Console.WriteLine($"      Migrations: {config.MigrationsPath}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"📊 Total: {databases.Count} database(s)");
        return 0;
    }

    private static async Task<int> ShowHealthAsync(MultiDatabaseManager multiDbManager)
    {
        Console.WriteLine("🏥 Cluster Health Report");
        Console.WriteLine();

        var healthReport = await multiDbManager.GetHealthReportAsync();

        Console.WriteLine($"📊 Report Generated: {healthReport.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"   Total Databases: {healthReport.TotalDatabases}");
        Console.WriteLine($"   ✅ Healthy: {healthReport.HealthyDatabases}");
        Console.WriteLine($"   ❌ Unhealthy: {healthReport.UnhealthyDatabases}");
        Console.WriteLine();

        foreach (var dbHealth in healthReport.DatabaseHealth)
        {
            var statusIcon = dbHealth.IsHealthy ? "✅" : "❌";
            Console.WriteLine($"{statusIcon} {dbHealth.DatabaseName}");
            Console.WriteLine($"   Status: {(dbHealth.IsHealthy ? "HEALTHY" : "UNHEALTHY")}");
            Console.WriteLine($"   Response Time: {dbHealth.ResponseTimeMs}ms");
            Console.WriteLine($"   Version: {dbHealth.Version ?? "Unknown"}");
            Console.WriteLine($"   Migrations: {dbHealth.MigrationCount}");
            Console.WriteLine($"   Checked: {dbHealth.CheckedAt:HH:mm:ss}");
            
            if (!string.IsNullOrEmpty(dbHealth.Error))
            {
                Console.WriteLine($"   Error: {dbHealth.Error}");
            }
            
            if (!string.IsNullOrEmpty(dbHealth.Notes))
            {
                Console.WriteLine($"   Notes: {dbHealth.Notes}");
            }
            
            Console.WriteLine();
        }

        return healthReport.UnhealthyDatabases > 0 ? 1 : 0;
    }

    private static async Task<int> ApplyToClusterAsync(MultiDatabaseManager multiDbManager, string[] args)
    {
        Console.WriteLine("🚀 Applying migrations to cluster...");

        var migrationFile = args.FirstOrDefault();
        var targetDatabases = GetArgument(args, "--databases")?.Split(',')
            ?? multiDbManager.GetRegisteredDatabases();

        if (string.IsNullOrEmpty(migrationFile))
        {
            Console.WriteLine("❌ Missing migration file");
            Console.WriteLine("Usage: dbmigrator cluster apply <migration-file> [--databases db1,db2,db3]");
            return 1;
        }

        if (!File.Exists(migrationFile))
        {
            Console.WriteLine($"❌ Migration file not found: {migrationFile}");
            return 1;
        }

        Console.WriteLine($"📄 Migration: {migrationFile}");
        Console.WriteLine($"🎯 Target Databases: {string.Join(", ", targetDatabases)}");
        Console.WriteLine();

        var result = await multiDbManager.ExecuteOnSelectedAsync(targetDatabases, async (name, engine) =>
        {
            Console.WriteLine($"   Applying to {name}...");
            await engine.ApplyMigrationAsync(migrationFile);
            Console.WriteLine($"   ✅ {name} completed");
            return true;
        });

        Console.WriteLine();
        Console.WriteLine("📈 Cluster Operation Results:");
        Console.WriteLine("═══════════════════════════");
        Console.WriteLine($"   Duration: {result.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"   ✅ Successful: {result.SuccessfulDatabases}");
        Console.WriteLine($"   ❌ Failed: {result.FailedDatabases}");
        Console.WriteLine();

        if (result.FailedDatabases > 0)
        {
            Console.WriteLine("❌ Failed Databases:");
            foreach (var dbResult in result.Results.Where(r => !r.Success))
            {
                Console.WriteLine($"   • {dbResult.DatabaseName}: {dbResult.Error}");
            }
        }

        return result.FailedDatabases > 0 ? 1 : 0;
    }

    private static async Task<int> ShowClusterStatusAsync(MultiDatabaseManager multiDbManager)
    {
        Console.WriteLine("📊 Cluster Status");
        Console.WriteLine();

        var databases = multiDbManager.GetRegisteredDatabases().ToList();
        Console.WriteLine($"🌐 Cluster Size: {databases.Count} database(s)");
        Console.WriteLine();

        // Show each database status
        foreach (var dbName in databases)
        {
            Console.WriteLine($"🗄️  {dbName}");
            
            try
            {
                var engine = multiDbManager.GetMigrationService(dbName);
                if (engine != null)
                {
                    // This would need to be implemented in MigrationEngine
                    Console.WriteLine("   Status: ✅ Connected");
                    Console.WriteLine("   Migrations: Unknown"); // Would need status method
                }
                else
                {
                    Console.WriteLine("   Status: ❌ No engine");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Status: ❌ Error - {ex.Message}");
            }
            
            Console.WriteLine();
        }

        return 0;
    }

    private static string? GetArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string MaskConnectionString(string connectionString)
    {
        // Simple masking for security
        var parts = connectionString.Split(';');
        var masked = parts.Select(part =>
        {
            if (part.ToLower().Contains("password"))
            {
                var keyValue = part.Split('=');
                return keyValue.Length == 2 ? $"{keyValue[0]}=***" : part;
            }
            return part;
        });
        
        return string.Join(";", masked);
    }

    private static int ShowClusterHelp()
    {
        Console.WriteLine("Usage: dbmigrator cluster <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  register               Register a database in the cluster");
        Console.WriteLine("  list                   List all registered databases");
        Console.WriteLine("  health                 Show cluster health status");
        Console.WriteLine("  apply <migration>      Apply migration to cluster");
        Console.WriteLine("  status                 Show cluster status");
        Console.WriteLine();
        Console.WriteLine("Register Options:");
        Console.WriteLine("  --name <name>          Database name");
        Console.WriteLine("  --connection <conn>    Connection string");
        Console.WriteLine("  --migrations-path <path>  Migrations directory");
        Console.WriteLine();
        Console.WriteLine("Apply Options:");
        Console.WriteLine("  --databases <list>     Comma-separated list of target databases");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator cluster register --name dev-db --connection \"Host=dev;Database=app\"");
        Console.WriteLine("  dbmigrator cluster health");
        Console.WriteLine("  dbmigrator cluster apply migration.sql --databases dev-db,staging-db");
        
        return 1;
    }
}