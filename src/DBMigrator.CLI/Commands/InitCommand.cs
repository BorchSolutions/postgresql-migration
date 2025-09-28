using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class InitCommand
{
    public static async Task<int> ExecuteAsync(string connectionString)
    {
        try
        {
            var service = new MigrationService(connectionString);
            await service.InitializeAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error initializing: {ex.Message}");
            return 1;
        }
    }
}