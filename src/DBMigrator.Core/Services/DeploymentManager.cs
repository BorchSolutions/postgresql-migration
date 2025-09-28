using System.Text.Json;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class DeploymentManager
{
    private readonly StructuredLogger _logger;
    private readonly MultiDatabaseManager _multiDbManager;
    private readonly MetricsCollector _metrics;
    private readonly BackupManager _backupManager;

    public DeploymentManager(
        StructuredLogger logger,
        MultiDatabaseManager multiDbManager,
        MetricsCollector metrics,
        BackupManager backupManager)
    {
        _logger = logger;
        _multiDbManager = multiDbManager;
        _metrics = metrics;
        _backupManager = backupManager;
    }

    public async Task<DeploymentResult> ExecuteDeploymentAsync(DeploymentPlan plan)
    {
        using var operation = _metrics.StartOperation("deployment", plan.Name);
        
        await _logger.LogAsync(LogLevel.Info, "Starting deployment", new 
        { 
            PlanName = plan.Name,
            Databases = plan.TargetDatabases,
            MigrationCount = plan.Migrations.Count,
            Strategy = plan.Strategy.ToString()
        });

        var result = new DeploymentResult
        {
            PlanName = plan.Name,
            StartedAt = DateTime.UtcNow,
            Strategy = plan.Strategy
        };

        try
        {
            // Pre-deployment validation
            var validationResult = await ValidateDeploymentAsync(plan);
            if (!validationResult.IsValid)
            {
                result.Success = false;
                result.Error = $"Validation failed: {string.Join(", ", validationResult.Errors)}";
                return result;
            }

            // Create pre-deployment backups if required
            if (plan.CreateBackups)
            {
                await CreatePreDeploymentBackupsAsync(plan, result);
            }

            // Execute deployment based on strategy
            switch (plan.Strategy)
            {
                case DeploymentStrategy.Sequential:
                    await ExecuteSequentialDeploymentAsync(plan, result);
                    break;
                case DeploymentStrategy.Parallel:
                    await ExecuteParallelDeploymentAsync(plan, result);
                    break;
                case DeploymentStrategy.BlueGreen:
                    await ExecuteBlueGreenDeploymentAsync(plan, result);
                    break;
                case DeploymentStrategy.Canary:
                    await ExecuteCanaryDeploymentAsync(plan, result);
                    break;
                default:
                    throw new ArgumentException($"Unsupported deployment strategy: {plan.Strategy}");
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt.Value - result.StartedAt;

            await _logger.LogAsync(LogLevel.Info, "Deployment completed", new 
            { 
                PlanName = plan.Name,
                Success = result.Success,
                Duration = result.Duration?.TotalSeconds,
                DatabaseResults = result.DatabaseResults.Count
            });

            _metrics.RecordEvent("deployment_completed", 1.0, new Dictionary<string, object>
            {
                ["plan_name"] = plan.Name,
                ["success"] = result.Success,
                ["duration_ms"] = result.Duration?.TotalMilliseconds ?? 0,
                ["strategy"] = plan.Strategy.ToString()
            });

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;

            await _logger.LogAsync(LogLevel.Error, "Deployment failed", new 
            { 
                PlanName = plan.Name,
                Error = ex.Message
            }, ex);

            return result;
        }
    }

    public async Task<DeploymentPlan> CreateDeploymentPlanAsync(
        string name,
        IEnumerable<string> targetDatabases,
        string migrationsPath,
        DeploymentStrategy strategy = DeploymentStrategy.Sequential)
    {
        await _logger.LogAsync(LogLevel.Info, "Creating deployment plan", new 
        { 
            Name = name,
            TargetDatabases = targetDatabases,
            Strategy = strategy.ToString()
        });

        var plan = new DeploymentPlan
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            TargetDatabases = targetDatabases.ToList(),
            Strategy = strategy,
            CreateBackups = true
        };

        // Discover pending migrations
        var pendingMigrations = await DiscoverPendingMigrationsAsync(targetDatabases.First(), migrationsPath);
        plan.Migrations = pendingMigrations;

        // Calculate deployment requirements
        await CalculateDeploymentRequirementsAsync(plan);

        await _logger.LogAsync(LogLevel.Info, "Deployment plan created", new 
        { 
            Name = name,
            MigrationCount = plan.Migrations.Count,
            EstimatedDuration = plan.EstimatedDurationMinutes
        });

        return plan;
    }

    public async Task<bool> ValidateDeploymentEnvironmentAsync(string environment)
    {
        await _logger.LogAsync(LogLevel.Info, "Validating deployment environment", new { Environment = environment });

        try
        {
            // Check if all required databases are registered
            var requiredDatabases = GetDatabasesForEnvironment(environment);
            var registeredDatabases = _multiDbManager.GetRegisteredDatabases().ToHashSet();

            var missingDatabases = requiredDatabases.Except(registeredDatabases).ToList();
            if (missingDatabases.Any())
            {
                await _logger.LogAsync(LogLevel.Error, "Missing database registrations", new 
                { 
                    Environment = environment,
                    MissingDatabases = missingDatabases
                });
                return false;
            }

            // Check database health
            var healthReport = await _multiDbManager.GetHealthReportAsync();
            if (healthReport.UnhealthyDatabases > 0)
            {
                await _logger.LogAsync(LogLevel.Warning, "Unhealthy databases detected", new 
                { 
                    Environment = environment,
                    UnhealthyCount = healthReport.UnhealthyDatabases
                });
                // Continue but warn
            }

            return true;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(LogLevel.Error, "Environment validation failed", new 
            { 
                Environment = environment,
                Error = ex.Message
            }, ex);
            return false;
        }
    }

    public async Task<string> GenerateDeploymentReportAsync(DeploymentResult result)
    {
        var report = new
        {
            Deployment = new
            {
                result.PlanName,
                result.Strategy,
                result.Success,
                result.StartedAt,
                result.CompletedAt,
                result.Duration,
                result.Error
            },
            DatabaseResults = result.DatabaseResults.Select(dr => new
            {
                dr.DatabaseName,
                dr.Success,
                dr.MigrationsApplied,
                dr.Duration,
                dr.Error,
                dr.BackupCreated
            }),
            Summary = new
            {
                TotalDatabases = result.DatabaseResults.Count,
                SuccessfulDatabases = result.DatabaseResults.Count(dr => dr.Success),
                FailedDatabases = result.DatabaseResults.Count(dr => !dr.Success),
                TotalMigrations = result.DatabaseResults.Sum(dr => dr.MigrationsApplied),
                TotalDuration = result.Duration?.TotalMinutes
            }
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await _logger.LogAsync(LogLevel.Info, "Deployment report generated", new { ReportSize = json.Length });

        return json;
    }

    private async Task<DeploymentValidationResult> ValidateDeploymentAsync(DeploymentPlan plan)
    {
        var validation = new DeploymentValidationResult { IsValid = true };

        // Validate target databases exist and are healthy
        foreach (var dbName in plan.TargetDatabases)
        {
            var config = _multiDbManager.GetDatabaseConfiguration(dbName);
            if (config == null)
            {
                validation.Errors.Add($"Database '{dbName}' is not registered");
                validation.IsValid = false;
            }
        }

        // Validate migrations exist and are valid
        foreach (var migration in plan.Migrations)
        {
            if (!File.Exists(migration.FilePath))
            {
                validation.Errors.Add($"Migration file not found: {migration.FilePath}");
                validation.IsValid = false;
            }
        }

        return validation;
    }

    private async Task CreatePreDeploymentBackupsAsync(DeploymentPlan plan, DeploymentResult result)
    {
        await _logger.LogAsync(LogLevel.Info, "Creating pre-deployment backups", new 
        { 
            PlanName = plan.Name,
            DatabaseCount = plan.TargetDatabases.Count
        });

        foreach (var dbName in plan.TargetDatabases)
        {
            try
            {
                var config = _multiDbManager.GetDatabaseConfiguration(dbName);
                if (config != null)
                {
                    var backupManager = new BackupManager(config, _logger);
                    var backupResult = await backupManager.CreateBackupAsync($"pre_deploy_{plan.Name}");
                    
                    await _logger.LogAsync(LogLevel.Info, "Pre-deployment backup created", new 
                    { 
                        DatabaseName = dbName,
                        BackupId = backupResult.BackupId
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.LogAsync(LogLevel.Error, "Failed to create pre-deployment backup", new 
                { 
                    DatabaseName = dbName,
                    Error = ex.Message
                }, ex);
                throw;
            }
        }
    }

    private async Task ExecuteSequentialDeploymentAsync(DeploymentPlan plan, DeploymentResult result)
    {
        foreach (var dbName in plan.TargetDatabases)
        {
            var dbResult = await ExecuteDatabaseDeploymentAsync(dbName, plan);
            result.DatabaseResults.Add(dbResult);

            if (!dbResult.Success && plan.StopOnFirstFailure)
            {
                result.Success = false;
                break;
            }
        }

        result.Success = result.DatabaseResults.All(dr => dr.Success);
    }

    private async Task ExecuteParallelDeploymentAsync(DeploymentPlan plan, DeploymentResult result)
    {
        var tasks = plan.TargetDatabases.Select(dbName => ExecuteDatabaseDeploymentAsync(dbName, plan));
        var results = await Task.WhenAll(tasks);
        
        result.DatabaseResults.AddRange(results);
        result.Success = results.All(r => r.Success);
    }

    private async Task ExecuteBlueGreenDeploymentAsync(DeploymentPlan plan, DeploymentResult result)
    {
        // Blue-Green deployment: Deploy to inactive environment first, then switch
        await _logger.LogAsync(LogLevel.Info, "Starting Blue-Green deployment", new { PlanName = plan.Name });
        
        // For now, implement as sequential with enhanced logging
        // In a real implementation, this would involve environment switching
        await ExecuteSequentialDeploymentAsync(plan, result);
    }

    private async Task ExecuteCanaryDeploymentAsync(DeploymentPlan plan, DeploymentResult result)
    {
        // Canary deployment: Deploy to subset first, then full rollout
        await _logger.LogAsync(LogLevel.Info, "Starting Canary deployment", new { PlanName = plan.Name });
        
        // Deploy to first database as canary
        if (plan.TargetDatabases.Any())
        {
            var canaryDb = plan.TargetDatabases.First();
            var canaryResult = await ExecuteDatabaseDeploymentAsync(canaryDb, plan);
            result.DatabaseResults.Add(canaryResult);

            if (canaryResult.Success)
            {
                // Deploy to remaining databases
                var remainingDbs = plan.TargetDatabases.Skip(1);
                var remainingTasks = remainingDbs.Select(dbName => ExecuteDatabaseDeploymentAsync(dbName, plan));
                var remainingResults = await Task.WhenAll(remainingTasks);
                result.DatabaseResults.AddRange(remainingResults);
            }
        }

        result.Success = result.DatabaseResults.All(dr => dr.Success);
    }

    private async Task<DatabaseDeploymentResult> ExecuteDatabaseDeploymentAsync(string dbName, DeploymentPlan plan)
    {
        var result = new DatabaseDeploymentResult
        {
            DatabaseName = dbName,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            var engine = _multiDbManager.GetMigrationService(dbName);
            if (engine == null)
            {
                result.Success = false;
                result.Error = $"Migration engine not found for database: {dbName}";
                return result;
            }

            // Apply migrations
            foreach (var migration in plan.Migrations)
            {
                await engine.ApplyMigrationAsync(migration.FilePath);
                result.MigrationsApplied++;
            }

            result.Success = true;
            result.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
        }

        return result;
    }

    private async Task<List<MigrationInfo>> DiscoverPendingMigrationsAsync(string databaseName, string migrationsPath)
    {
        // This would typically compare database state with migration files
        // For now, return all migrations in the path
        var migrations = new List<MigrationInfo>();

        if (Directory.Exists(migrationsPath))
        {
            var files = Directory.GetFiles(migrationsPath, "*.sql")
                .Where(f => !Path.GetFileName(f).StartsWith("."))
                .OrderBy(f => f);

            foreach (var file in files)
            {
                migrations.Add(new MigrationInfo
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    CreatedAt = File.GetCreationTime(file)
                });
            }
        }

        return migrations;
    }

    private async Task CalculateDeploymentRequirementsAsync(DeploymentPlan plan)
    {
        // Estimate deployment time based on migration count and historical data
        plan.EstimatedDurationMinutes = plan.Migrations.Count * 2; // 2 minutes per migration average
        
        // Adjust for strategy
        if (plan.Strategy == DeploymentStrategy.Parallel)
        {
            plan.EstimatedDurationMinutes = (int)(plan.EstimatedDurationMinutes * 0.6); // 40% faster parallel
        }
    }

    private IEnumerable<string> GetDatabasesForEnvironment(string environment)
    {
        // This would typically come from configuration
        // For now, return all registered databases
        return _multiDbManager.GetRegisteredDatabases();
    }
}

public class DeploymentPlan
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<string> TargetDatabases { get; set; } = new();
    public List<MigrationInfo> Migrations { get; set; } = new();
    public DeploymentStrategy Strategy { get; set; }
    public bool CreateBackups { get; set; } = true;
    public bool StopOnFirstFailure { get; set; } = true;
    public int EstimatedDurationMinutes { get; set; }
}

public class DeploymentResult
{
    public string PlanName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public DeploymentStrategy Strategy { get; set; }
    public string? Error { get; set; }
    public List<DatabaseDeploymentResult> DatabaseResults { get; set; } = new();
}

public class DatabaseDeploymentResult
{
    public string DatabaseName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : TimeSpan.Zero;
    public int MigrationsApplied { get; set; }
    public string? Error { get; set; }
    public bool BackupCreated { get; set; }
}

public class MigrationInfo
{
    public string Id { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DeploymentValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public enum DeploymentStrategy
{
    Sequential,
    Parallel,
    BlueGreen,
    Canary
}