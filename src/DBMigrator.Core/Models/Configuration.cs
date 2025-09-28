namespace DBMigrator.Core.Models;

public class DatabaseConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Environment { get; set; } = "development";
    public string MigrationsPath { get; set; } = "./migrations";
    public string SchemaTable { get; set; } = "__dbmigrator_schema_migrations";
    public int CommandTimeout { get; set; } = 30;
    public bool AutoCreateMigrationTable { get; set; } = true;
    public LoggingConfiguration Logging { get; set; } = new();
    public BackupConfiguration Backup { get; set; } = new();
    public ValidationConfiguration Validation { get; set; } = new();
}

public class LoggingConfiguration
{
    public string Level { get; set; } = "Info";
    public bool EnableConsoleOutput { get; set; } = true;
    public bool EnableFileOutput { get; set; } = false;
    public string? LogFilePath { get; set; }
    public bool LogPerformanceMetrics { get; set; } = true;
    public bool LogMigrationDetails { get; set; } = true;
}

public class BackupConfiguration
{
    public bool AutoBackupBeforeMigration { get; set; } = false;
    public string BackupPath { get; set; } = "./backups";
    public int RetentionDays { get; set; } = 30;
    public bool CompressBackups { get; set; } = true;
}

public class ValidationConfiguration
{
    public bool ValidateBeforeApply { get; set; } = true;
    public bool RequireDryRunForDestructive { get; set; } = true;
    public bool CheckConflictsBeforeApply { get; set; } = true;
    public bool AllowOutOfOrderMigrations { get; set; } = false;
    public List<string> RequiredApprovals { get; set; } = new();
}

public class EnvironmentConfiguration
{
    public Dictionary<string, DatabaseConfiguration> Environments { get; set; } = new();
    public string DefaultEnvironment { get; set; } = "development";
    public GlobalConfiguration Global { get; set; } = new();
}

public class GlobalConfiguration
{
    public string TeamName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ConfigVersion { get; set; } = "1.0";
    public ConflictResolutionPolicy ConflictResolution { get; set; } = new();
    public NotificationConfiguration Notifications { get; set; } = new();
}

public class ConflictResolutionPolicy
{
    public string DefaultStrategy { get; set; } = "manual";
    public bool AutoResolveTimestampConflicts { get; set; } = false;
    public bool AllowForceApply { get; set; } = false;
    public List<string> RequireApprovalFor { get; set; } = new() { "destructive", "outoforder" };
}

public class NotificationConfiguration
{
    public bool EnableSlackNotifications { get; set; } = false;
    public string? SlackWebhookUrl { get; set; }
    public bool EnableEmailNotifications { get; set; } = false;
    public List<string> NotificationRecipients { get; set; } = new();
    public List<string> NotifyOnEvents { get; set; } = new() { "migration_failed", "conflicts_detected" };
}