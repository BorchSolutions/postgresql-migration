using DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Models.Changes;

public class DatabaseChanges
{
    public List<Table> NewTables { get; set; } = new();
    public List<Table> DeletedTables { get; set; } = new();
    public List<TableChanges> ModifiedTables { get; set; } = new();
    public List<Function> NewFunctions { get; set; } = new();
    public List<Function> DeletedFunctions { get; set; } = new();
    public List<Function> ModifiedFunctions { get; set; } = new();
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public bool HasChanges => NewTables.Any() || DeletedTables.Any() || ModifiedTables.Any() ||
                             NewFunctions.Any() || DeletedFunctions.Any() || ModifiedFunctions.Any();

    public int TotalChanges => NewTables.Count + DeletedTables.Count + 
                              ModifiedTables.Sum(t => t.TotalChanges) +
                              NewFunctions.Count + DeletedFunctions.Count + ModifiedFunctions.Count;

    public override string ToString()
    {
        if (!HasChanges) return "No changes detected";
        
        var parts = new List<string>();
        if (NewTables.Any()) parts.Add($"{NewTables.Count} new table(s)");
        if (DeletedTables.Any()) parts.Add($"{DeletedTables.Count} deleted table(s)");
        if (ModifiedTables.Any()) parts.Add($"{ModifiedTables.Count} modified table(s)");
        if (NewFunctions.Any()) parts.Add($"{NewFunctions.Count} new function(s)");
        if (DeletedFunctions.Any()) parts.Add($"{DeletedFunctions.Count} deleted function(s)");
        if (ModifiedFunctions.Any()) parts.Add($"{ModifiedFunctions.Count} modified function(s)");
        
        return string.Join(", ", parts);
    }
}