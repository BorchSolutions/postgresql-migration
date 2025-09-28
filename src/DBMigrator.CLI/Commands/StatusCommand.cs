using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class StatusCommand
{
    public static async Task<int> ExecuteAsync(string connectionString)
    {
        try
        {
            var service = new MigrationService(connectionString);
            await service.ShowStatusAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error showing status: {ex.Message}");
            return 1;
        }
    }
}