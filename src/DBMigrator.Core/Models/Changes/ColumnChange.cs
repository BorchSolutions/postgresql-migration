using DBMigrator.Core.Models.Schema;
using DBMigrator.Core.Services;

namespace DBMigrator.Core.Models.Changes;

public class ColumnModification
{
    public ColumnModificationType Type { get; set; }
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDestructive { get; set; }
}

public enum ColumnModificationType
{
    DataTypeChanged,
    NullabilityChanged,
    DefaultValueChanged,
    LengthChanged,
    PrecisionChanged,
    ScaleChanged
}