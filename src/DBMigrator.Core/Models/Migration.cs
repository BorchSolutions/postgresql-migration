namespace DBMigrator.Core.Models;

public class Migration
{
    public string Id { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public string AppliedBy { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public bool IsApplied { get; set; }
}