using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class ApplyCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string filename)
    {
        try
        {
            var service = new MigrationService(connectionString);
            await service.ApplyMigrationAsync(filename);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error applying migration: {ex.Message}");
            return 1;
        }
    }
}