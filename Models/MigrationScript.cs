namespace BorchSolutions.PostgreSQL.Migration.Models;

public enum MigrationScriptType
{
    Schema,
    Data,
    Baseline
}

public enum MigrationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped,
    RolledBack
}

public class MigrationScript
{
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public MigrationScriptType Type { get; set; }
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;
    public string Checksum { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public int ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public int AffectedRows { get; set; }
    public string Environment { get; set; } = string.Empty;

    public bool IsExecuted => Status == MigrationStatus.Completed;
    public bool HasFailed => Status == MigrationStatus.Failed;
    
    public string GetDisplayName() => $"{Version}__{Description}";
    
    public void MarkAsCompleted(int executionTime, int affectedRows = 0)
    {
        Status = MigrationStatus.Completed;
        ExecutedAt = DateTime.UtcNow;
        ExecutionTimeMs = executionTime;
        AffectedRows = affectedRows;
        ErrorMessage = null;
    }
    
    public void MarkAsFailed(string errorMessage, int executionTime = 0)
    {
        Status = MigrationStatus.Failed;
        ExecutedAt = DateTime.UtcNow;
        ExecutionTimeMs = executionTime;
        ErrorMessage = errorMessage;
    }
}

public class MigrationSummary
{
    public int TotalScripts { get; set; }
    public int CompletedScripts { get; set; }
    public int FailedScripts { get; set; }
    public int PendingScripts { get; set; }
    public int TotalExecutionTime { get; set; }
    public int TotalAffectedRows { get; set; }
    public DateTime? LastExecution { get; set; }
    public List<MigrationScript> Scripts { get; set; } = new();
    
    public double SuccessRate => TotalScripts > 0 ? (double)CompletedScripts / TotalScripts * 100 : 0;
    public bool HasFailures => FailedScripts > 0;
    public bool IsComplete => PendingScripts == 0 && !HasFailures;
}