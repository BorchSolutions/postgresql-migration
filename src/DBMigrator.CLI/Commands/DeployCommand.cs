using DBMigrator.Core.Services;
using DBMigrator.Core.Models;

namespace DBMigrator.CLI.Commands;

public static class DeployCommand
{
    public static async Task<int> ExecuteAsync(string action, string[] args)
    {
        try
        {
            Console.WriteLine("🚀 DBMigrator Deployment Automation");
            Console.WriteLine();

            var logger = new StructuredLogger("Info", true);
            var multiDbManager = new MultiDatabaseManager(logger);
            var metrics = new MetricsCollector(logger);
            
            // For this demo, we'll create a basic backup manager
            var tempConfig = new DatabaseConfiguration { ConnectionString = "dummy", MigrationsPath = "./migrations" };
            var backupManager = new BackupManager(tempConfig, logger);
            
            var deploymentManager = new DeploymentManager(logger, multiDbManager, metrics, backupManager);

            return action.ToLower() switch
            {
                "plan" => await CreateDeploymentPlanAsync(deploymentManager, args),
                "execute" => await ExecuteDeploymentAsync(deploymentManager, args),
                "validate" => await ValidateDeploymentAsync(deploymentManager, args),
                "status" => await ShowDeploymentStatusAsync(args),
                "rollback" => await RollbackDeploymentAsync(args),
                _ => ShowDeployHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Deployment operation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CreateDeploymentPlanAsync(DeploymentManager deploymentManager, string[] args)
    {
        Console.WriteLine("📋 Creating Deployment Plan");
        Console.WriteLine();

        var planName = GetArgument(args, "--name") ?? $"deploy_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var environment = GetArgument(args, "--environment") ?? "development";
        var databases = GetArgument(args, "--databases")?.Split(',') ?? new[] { "default" };
        var migrationsPath = GetArgument(args, "--migrations") ?? "./migrations";
        var strategyArg = GetArgument(args, "--strategy") ?? "sequential";

        if (!Enum.TryParse<DeploymentStrategy>(strategyArg, true, out var strategy))
        {
            Console.WriteLine($"❌ Invalid strategy: {strategyArg}");
            Console.WriteLine("Valid strategies: sequential, parallel, bluegreen, canary");
            return 1;
        }

        Console.WriteLine($"📝 Plan Name: {planName}");
        Console.WriteLine($"🌍 Environment: {environment}");
        Console.WriteLine($"🎯 Target Databases: {string.Join(", ", databases)}");
        Console.WriteLine($"📁 Migrations Path: {migrationsPath}");
        Console.WriteLine($"🏗️  Strategy: {strategy}");
        Console.WriteLine();

        try
        {
            var plan = await deploymentManager.CreateDeploymentPlanAsync(
                planName, databases, migrationsPath, strategy);

            Console.WriteLine("✅ Deployment Plan Created");
            Console.WriteLine("═══════════════════════════");
            Console.WriteLine($"   📋 Plan: {plan.Name}");
            Console.WriteLine($"   📅 Created: {plan.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   🗄️  Databases: {plan.TargetDatabases.Count}");
            Console.WriteLine($"   📄 Migrations: {plan.Migrations.Count}");
            Console.WriteLine($"   ⏱️  Estimated Duration: {plan.EstimatedDurationMinutes} minutes");
            Console.WriteLine($"   🛡️  Create Backups: {(plan.CreateBackups ? "Yes" : "No")}");
            Console.WriteLine();

            if (plan.Migrations.Any())
            {
                Console.WriteLine("📄 Migrations to Deploy:");
                foreach (var migration in plan.Migrations.Take(10))
                {
                    Console.WriteLine($"   • {migration.Id}");
                }
                
                if (plan.Migrations.Count > 10)
                {
                    Console.WriteLine($"   ... and {plan.Migrations.Count - 10} more");
                }
                Console.WriteLine();
            }

            // Save plan to file for later execution
            var planPath = $"{planName}.json";
            await SaveDeploymentPlanAsync(plan, planPath);
            Console.WriteLine($"💾 Plan saved to: {planPath}");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create deployment plan: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteDeploymentAsync(DeploymentManager deploymentManager, string[] args)
    {
        Console.WriteLine("🚀 Executing Deployment");
        Console.WriteLine();

        var planName = GetArgument(args, "--plan");
        var dryRun = args.Contains("--dry-run");

        if (string.IsNullOrEmpty(planName))
        {
            Console.WriteLine("❌ Missing deployment plan");
            Console.WriteLine("Usage: dbmigrator deploy execute --plan <plan-name> [--dry-run]");
            return 1;
        }

        var planPath = $"{planName}.json";
        if (!File.Exists(planPath))
        {
            Console.WriteLine($"❌ Deployment plan not found: {planPath}");
            Console.WriteLine("Create a plan first with: dbmigrator deploy plan");
            return 1;
        }

        try
        {
            var plan = await LoadDeploymentPlanAsync(planPath);
            
            Console.WriteLine($"📋 Plan: {plan.Name}");
            Console.WriteLine($"🎯 Strategy: {plan.Strategy}");
            Console.WriteLine($"🗄️  Databases: {string.Join(", ", plan.TargetDatabases)}");
            Console.WriteLine($"📄 Migrations: {plan.Migrations.Count}");
            
            if (dryRun)
            {
                Console.WriteLine("🧪 DRY RUN MODE - No changes will be made");
            }
            Console.WriteLine();

            if (!dryRun)
            {
                Console.Write("Continue with deployment? (y/N): ");
                var confirmation = Console.ReadLine();
                if (confirmation?.ToLower() != "y" && confirmation?.ToLower() != "yes")
                {
                    Console.WriteLine("❌ Deployment cancelled");
                    return 1;
                }
                Console.WriteLine();
            }

            if (dryRun)
            {
                Console.WriteLine("✅ Dry run completed - deployment plan is valid");
                return 0;
            }

            var result = await deploymentManager.ExecuteDeploymentAsync(plan);

            Console.WriteLine("📈 Deployment Results:");
            Console.WriteLine("═══════════════════════");
            Console.WriteLine($"   📋 Plan: {result.PlanName}");
            Console.WriteLine($"   🏗️  Strategy: {result.Strategy}");
            Console.WriteLine($"   ✅ Success: {result.Success}");
            Console.WriteLine($"   ⏱️  Duration: {result.Duration?.TotalMinutes:F1} minutes");
            Console.WriteLine($"   🗄️  Databases Processed: {result.DatabaseResults.Count}");
            Console.WriteLine($"   ✅ Successful: {result.DatabaseResults.Count(r => r.Success)}");
            Console.WriteLine($"   ❌ Failed: {result.DatabaseResults.Count(r => !r.Success)}");
            Console.WriteLine();

            if (result.DatabaseResults.Any(r => !r.Success))
            {
                Console.WriteLine("❌ Failed Databases:");
                foreach (var dbResult in result.DatabaseResults.Where(r => !r.Success))
                {
                    Console.WriteLine($"   • {dbResult.DatabaseName}: {dbResult.Error}");
                }
                Console.WriteLine();
            }

            // Generate detailed report
            var reportPath = $"deployment_report_{result.PlanName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var report = await deploymentManager.GenerateDeploymentReportAsync(result);
            await File.WriteAllTextAsync(reportPath, report);
            Console.WriteLine($"📊 Detailed report saved to: {reportPath}");

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Deployment execution failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ValidateDeploymentAsync(DeploymentManager deploymentManager, string[] args)
    {
        Console.WriteLine("🔍 Validating Deployment Environment");
        Console.WriteLine();

        var environment = GetArgument(args, "--environment") ?? "development";

        Console.WriteLine($"🌍 Environment: {environment}");
        Console.WriteLine();

        try
        {
            var isValid = await deploymentManager.ValidateDeploymentEnvironmentAsync(environment);

            if (isValid)
            {
                Console.WriteLine("✅ Deployment environment is valid");
                Console.WriteLine("   All required databases are accessible");
                Console.WriteLine("   System is ready for deployment");
            }
            else
            {
                Console.WriteLine("❌ Deployment environment validation failed");
                Console.WriteLine("   Check logs for specific issues");
            }

            return isValid ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Validation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ShowDeploymentStatusAsync(string[] args)
    {
        Console.WriteLine("📊 Deployment Status");
        Console.WriteLine();

        // Look for recent deployment reports
        var reportFiles = Directory.GetFiles(".", "deployment_report_*.json")
            .OrderByDescending(f => File.GetCreationTime(f))
            .Take(5);

        if (!reportFiles.Any())
        {
            Console.WriteLine("   No recent deployments found");
            return 0;
        }

        Console.WriteLine("📋 Recent Deployments:");
        Console.WriteLine("═══════════════════════");

        foreach (var reportFile in reportFiles)
        {
            try
            {
                var reportJson = await File.ReadAllTextAsync(reportFile);
                var report = System.Text.Json.JsonSerializer.Deserialize<dynamic>(reportJson);
                
                var fileName = Path.GetFileName(reportFile);
                var createdAt = File.GetCreationTime(reportFile);
                
                Console.WriteLine($"   📄 {fileName}");
                Console.WriteLine($"      Created: {createdAt:yyyy-MM-dd HH:mm:ss}");
                // Would need proper deserialization for detailed info
                Console.WriteLine();
            }
            catch
            {
                // Skip invalid report files
            }
        }

        return 0;
    }

    private static async Task<int> RollbackDeploymentAsync(string[] args)
    {
        Console.WriteLine("⏪ Deployment Rollback");
        Console.WriteLine();

        var deploymentId = GetArgument(args, "--deployment");
        
        if (string.IsNullOrEmpty(deploymentId))
        {
            Console.WriteLine("❌ Missing deployment ID");
            Console.WriteLine("Usage: dbmigrator deploy rollback --deployment <deployment-id>");
            return 1;
        }

        Console.WriteLine("⚠️  Rollback functionality not implemented in current version");
        Console.WriteLine("   For manual rollback:");
        Console.WriteLine("   1. Use backup files created before deployment");
        Console.WriteLine("   2. Run 'dbmigrator down' commands to reverse migrations");
        Console.WriteLine("   3. Restore from backup if necessary");
        Console.WriteLine();

        return 1;
    }

    private static async Task SaveDeploymentPlanAsync(DeploymentPlan plan, string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task<DeploymentPlan> LoadDeploymentPlanAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var plan = System.Text.Json.JsonSerializer.Deserialize<DeploymentPlan>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        
        return plan ?? throw new InvalidOperationException("Failed to deserialize deployment plan");
    }

    private static string? GetArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ShowDeployHelp()
    {
        Console.WriteLine("Usage: dbmigrator deploy <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  plan                   Create a deployment plan");
        Console.WriteLine("  execute                Execute a deployment plan");
        Console.WriteLine("  validate               Validate deployment environment");
        Console.WriteLine("  status                 Show deployment status");
        Console.WriteLine("  rollback               Rollback a deployment");
        Console.WriteLine();
        Console.WriteLine("Plan Options:");
        Console.WriteLine("  --name <name>          Deployment plan name");
        Console.WriteLine("  --environment <env>    Target environment");
        Console.WriteLine("  --databases <list>     Comma-separated database list");
        Console.WriteLine("  --migrations <path>    Migrations directory");
        Console.WriteLine("  --strategy <strategy>  Deployment strategy (sequential, parallel, bluegreen, canary)");
        Console.WriteLine();
        Console.WriteLine("Execute Options:");
        Console.WriteLine("  --plan <name>          Deployment plan name");
        Console.WriteLine("  --dry-run              Validate without executing");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # Create deployment plan");
        Console.WriteLine("  dbmigrator deploy plan --name release-v1.2 --environment production --strategy parallel");
        Console.WriteLine();
        Console.WriteLine("  # Execute deployment");
        Console.WriteLine("  dbmigrator deploy execute --plan release-v1.2");
        Console.WriteLine();
        Console.WriteLine("  # Dry run");
        Console.WriteLine("  dbmigrator deploy execute --plan release-v1.2 --dry-run");
        
        return 1;
    }
}