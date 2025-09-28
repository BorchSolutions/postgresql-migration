using DBMigrator.Core.Database;
using DBMigrator.Core.Services;

namespace DBMigrator.CLI.Commands;

public class BaselineCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string migrationsPath, string action, string? baselineName = null)
    {
        try
        {
            var connectionManager = new ConnectionManager(connectionString);
            var schemaAnalyzer = new SchemaAnalyzer(connectionManager);
            var configService = new ConfigurationService();

            switch (action.ToLower())
            {
                case "create":
                    return await CreateBaselineAsync(schemaAnalyzer, configService, migrationsPath, baselineName);
                
                case "show":
                    return await ShowBaselineAsync(configService, migrationsPath);
                
                default:
                    Console.WriteLine("❌ Unknown baseline action. Use: create, show");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error with baseline: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CreateBaselineAsync(SchemaAnalyzer analyzer, ConfigurationService configService, string migrationsPath, string? name)
    {
        Console.WriteLine("📸 Creating baseline snapshot...");
        
        var schema = await analyzer.GetCurrentSchemaAsync();
        await configService.SaveBaselineAsync(schema, migrationsPath);
        
        var tableCount = schema.Tables.Count;
        var columnCount = schema.Tables.Sum(t => t.Columns.Count);
        var indexCount = schema.Tables.Sum(t => t.Indexes.Count);
        
        Console.WriteLine($"✅ Baseline created successfully!");
        Console.WriteLine($"   📊 Captured: {tableCount} tables, {columnCount} columns, {indexCount} indexes");
        Console.WriteLine($"   📁 Saved to: {Path.Combine(migrationsPath, ".baseline.json")}");
        Console.WriteLine($"   ⏰ Timestamp: {schema.CapturedAt:yyyy-MM-dd HH:mm:ss} UTC");
        
        return 0;
    }

    private static async Task<int> ShowBaselineAsync(ConfigurationService configService, string migrationsPath)
    {
        var baseline = await configService.LoadBaselineAsync(migrationsPath);
        
        if (baseline == null)
        {
            Console.WriteLine("❌ No baseline found. Create one with 'dbmigrator baseline create'");
            return 1;
        }

        Console.WriteLine("📸 Current Baseline:");
        Console.WriteLine($"   📅 Created: {baseline.CapturedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"   🗄️  Schema: {baseline.SchemaName}");
        Console.WriteLine($"   📊 Tables: {baseline.Tables.Count}");
        Console.WriteLine();

        if (baseline.Tables.Any())
        {
            Console.WriteLine("📋 Tables in baseline:");
            foreach (var table in baseline.Tables.OrderBy(t => t.Name))
            {
                var columnCount = table.Columns.Count;
                var indexCount = table.Indexes.Count(i => !i.IsPrimary);
                var pkCount = table.Indexes.Count(i => i.IsPrimary);
                
                Console.WriteLine($"   • {table.Name} ({columnCount} columns, {indexCount} indexes{(pkCount > 0 ? ", PK" : "")})");
            }
        }

        return 0;
    }
}