namespace DBMigrator.Core.Models.Conflicts;

public class ConflictDetection
{
    public List<MigrationConflict> Conflicts { get; set; } = new();
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public bool HasConflicts => Conflicts.Any();
    public int ConflictCount => Conflicts.Count;

    public List<MigrationConflict> GetConflictsByType(ConflictType type)
    {
        return Conflicts.Where(c => c.Type == type).ToList();
    }

    public List<MigrationConflict> GetCriticalConflicts()
    {
        return Conflicts.Where(c => c.Severity == ConflictSeverity.Critical).ToList();
    }
}

public class MigrationConflict
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ConflictType Type { get; set; }
    public ConflictSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string MigrationId { get; set; } = string.Empty;
    public string MigrationFile { get; set; } = string.Empty;
    public DateTime ConflictTime { get; set; } = DateTime.UtcNow;
    public List<string> AffectedObjects { get; set; } = new();
    public string Resolution { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();

    public override string ToString()
    {
        return $"{Severity} {Type}: {Description} (Migration: {MigrationId})";
    }
}

public enum ConflictType
{
    OutOfOrder,
    DuplicateTimestamp,
    MissingDependency,
    CircularDependency,
    ChecksumMismatch,
    SchemaConflict,
    DataConflict,
    AlreadyApplied
}

public enum ConflictSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public class ConflictResolution
{
    public string ConflictId { get; set; } = string.Empty;
    public ResolutionStrategy Strategy { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool RequiresManualIntervention { get; set; }
    public List<string> Steps { get; set; } = new();
}

public enum ResolutionStrategy
{
    Reorder,
    Rename,
    Merge,
    Skip,
    Abort,
    Force,
    Manual
}