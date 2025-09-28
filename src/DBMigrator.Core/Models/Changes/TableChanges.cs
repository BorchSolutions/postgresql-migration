using DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Models.Changes;

public class TableChanges
{
    public string TableName { get; set; } = string.Empty;
    public List<Column> NewColumns { get; set; } = new();
    public List<Column> DeletedColumns { get; set; } = new();
    public List<DetailedColumnChange> ModifiedColumns { get; set; } = new();
    public List<Schema.Index> NewIndexes { get; set; } = new();
    public List<Schema.Index> DeletedIndexes { get; set; } = new();

    public int TotalChanges => NewColumns.Count + DeletedColumns.Count + 
                              ModifiedColumns.Count + NewIndexes.Count + DeletedIndexes.Count;

    public bool HasChanges => TotalChanges > 0;

    public override string ToString()
    {
        if (!HasChanges) return $"No changes in table {TableName}";
        
        var parts = new List<string>();
        if (NewColumns.Any()) parts.Add($"{NewColumns.Count} new column(s)");
        if (DeletedColumns.Any()) parts.Add($"{DeletedColumns.Count} deleted column(s)");
        if (ModifiedColumns.Any()) parts.Add($"{ModifiedColumns.Count} modified column(s)");
        if (NewIndexes.Any()) parts.Add($"{NewIndexes.Count} new index(es)");
        if (DeletedIndexes.Any()) parts.Add($"{DeletedIndexes.Count} deleted index(es)");
        
        return $"Table {TableName}: {string.Join(", ", parts)}";
    }
}

public class DetailedColumnChange
{
    public Column OldColumn { get; set; } = new();
    public Column NewColumn { get; set; } = new();
    public List<ColumnModification> Changes { get; set; } = new();
    
    public bool HasDestructiveChanges => Changes.Any(c => c.IsDestructive);
    public bool RequiresDataMigration => HasDestructiveChanges || Changes.Any(c => 
        c.Type == ColumnModificationType.DataTypeChanged || 
        c.Type == ColumnModificationType.NullabilityChanged);

    public override string ToString()
    {
        return $"Column {NewColumn.Name}: {string.Join(", ", Changes.Select(c => c.Description))}";
    }
}