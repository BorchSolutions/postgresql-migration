namespace DBMigrator.Core.Models.DryRun;

public class DryRunResult
{
    public string MigrationId { get; set; } = string.Empty;
    public string MigrationFile { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public long EstimatedRowsAffected { get; set; }
    public List<DryRunStep> Steps { get; set; } = new();
    public List<DryRunWarning> Warnings { get; set; } = new();
    public List<DryRunError> Errors { get; set; } = new();
    public ImpactAnalysis Impact { get; set; } = new();
    public DateTime SimulatedAt { get; set; } = DateTime.UtcNow;

    public bool HasWarnings => Warnings.Any();
    public bool HasErrors => Errors.Any();
    public bool CanProceed => IsValid && !HasErrors;
}

public class DryRunStep
{
    public int Order { get; set; }
    public string SqlStatement { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public StepType Type { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public long EstimatedRowsAffected { get; set; }
    public List<string> AffectedObjects { get; set; } = new();
    public bool RequiresLock { get; set; }
    public LockType LockType { get; set; }
}

public class DryRunWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public WarningSeverity Severity { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public List<string> AffectedObjects { get; set; } = new();
}

public class DryRunError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SqlStatement { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<string> AffectedObjects { get; set; } = new();
}

public class ImpactAnalysis
{
    public List<string> AffectedTables { get; set; } = new();
    public List<string> AffectedIndexes { get; set; } = new();
    public List<string> AffectedConstraints { get; set; } = new();
    public long TotalEstimatedRowsAffected { get; set; }
    public TimeSpan TotalEstimatedDuration { get; set; }
    public bool RequiresDowntime { get; set; }
    public string DowntimeReason { get; set; } = string.Empty;
    public DataRisk DataRisk { get; set; } = DataRisk.Low;
    public List<string> BackupRecommendations { get; set; } = new();
}

public enum StepType
{
    CreateTable,
    DropTable,
    AlterTable,
    CreateIndex,
    DropIndex,
    CreateConstraint,
    DropConstraint,
    InsertData,
    UpdateData,
    DeleteData,
    CreateFunction,
    DropFunction,
    CreateView,
    DropView,
    Custom
}

public enum LockType
{
    None,
    Shared,
    Exclusive,
    AccessExclusive
}

public enum WarningSeverity
{
    Info,
    Low,
    Medium,
    High
}

public enum DataRisk
{
    None,
    Low,
    Medium,
    High,
    Critical
}