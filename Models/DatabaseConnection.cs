namespace BorchSolutions.PostgreSQL.Migration.Models;

public class DatabaseConnection
{
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MigrationConfig
{
    public string SchemaTable { get; set; } = "borchsolutions_schema_migrations";
    public string DataTable { get; set; } = "borchsolutions_data_migrations";
    public string MigrationsPath { get; set; } = "Migrations";
    public string SchemaPath { get; set; } = "Schema";
    public string DataPath { get; set; } = "Data";
    public string BackupPath { get; set; } = "Backups";
    public bool EnableTransactions { get; set; } = true;
    public bool EnableBackups { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public int CommandTimeout { get; set; } = 300;
}

public class DatabaseInfo
{
    public string ServerVersion { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "public";
    public int TableCount { get; set; }
    public int FunctionCount { get; set; }
    public int IndexCount { get; set; }
    public int TriggerCount { get; set; }
    public DateTime InspectedAt { get; set; } = DateTime.UtcNow;
}