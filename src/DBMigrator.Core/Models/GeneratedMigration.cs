namespace DBMigrator.Core.Models;

public class GeneratedMigration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UpScript { get; set; } = string.Empty;
    public string DownScript { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();

    public string GenerateFilename(string type = "auto")
    {
        var timestamp = CreatedAt.ToString("yyyyMMddHHmmss");
        var safeName = Name.Replace(" ", "_").ToLowerInvariant();
        return $"{timestamp}_{type}_{safeName}";
    }

    public string UpFilename => $"{GenerateFilename()}.up.sql";
    public string DownFilename => $"{GenerateFilename()}.down.sql";
}