using DBMigrator.Core.Services;
using System.Text.Json;

namespace DBMigrator.CLI.Commands;

public static class MetricsCommand
{
    public static async Task<int> ExecuteAsync(string action, string[] args)
    {
        try
        {
            Console.WriteLine("📊 DBMigrator Metrics & Monitoring");
            Console.WriteLine();

            var logger = new StructuredLogger("Info", true);
            var metrics = new MetricsCollector(logger);

            return action.ToLower() switch
            {
                "show" => await ShowMetricsAsync(metrics),
                "system" => await ShowSystemMetricsAsync(metrics),
                "export" => await ExportMetricsAsync(metrics, args),
                "clear" => await ClearMetricsAsync(metrics),
                _ => ShowMetricsHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Metrics operation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ShowMetricsAsync(MetricsCollector metrics)
    {
        Console.WriteLine("📈 Application Metrics");
        Console.WriteLine();

        var snapshot = await metrics.GetSnapshotAsync();

        Console.WriteLine($"📊 Snapshot Generated: {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"🏷️  Instance: {snapshot.InstanceId}");
        Console.WriteLine();

        if (snapshot.Counters.Any())
        {
            Console.WriteLine("📊 Performance Counters:");
            Console.WriteLine("═══════════════════════");
            
            foreach (var (name, counter) in snapshot.Counters.OrderBy(c => c.Key))
            {
                Console.WriteLine($"   📈 {name}");
                Console.WriteLine($"      Count: {counter.Count:N0}");
                Console.WriteLine($"      Sum: {counter.Sum:N2}");
                
                if (counter.Count > 0)
                {
                    Console.WriteLine($"      Average: {counter.Average:N2}");
                    Console.WriteLine($"      Min: {counter.Min:N2}");
                    Console.WriteLine($"      Max: {counter.Max:N2}");
                }
                
                if (counter.Value != 0)
                {
                    Console.WriteLine($"      Current Value: {counter.Value:N2}");
                }
                
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("   No performance counters recorded yet");
            Console.WriteLine();
        }

        if (snapshot.RecentEvents.Any())
        {
            Console.WriteLine("🕒 Recent Events (Last 10):");
            Console.WriteLine("═══════════════════════════");
            
            var recentEvents = snapshot.RecentEvents
                .OrderByDescending(e => e.Timestamp)
                .Take(10);
            
            foreach (var evt in recentEvents)
            {
                Console.WriteLine($"   ⚡ {evt.Name}");
                Console.WriteLine($"      Value: {evt.Value:N2}");
                Console.WriteLine($"      Time: {evt.Timestamp:HH:mm:ss}");
                
                if (evt.Tags.Any())
                {
                    Console.WriteLine($"      Tags: {string.Join(", ", evt.Tags.Select(t => $"{t.Key}={t.Value}"))}");
                }
                
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("   No recent events recorded");
            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> ShowSystemMetricsAsync(MetricsCollector metrics)
    {
        Console.WriteLine("🖥️  System Metrics");
        Console.WriteLine();

        var systemMetrics = await metrics.GetSystemMetricsAsync();

        Console.WriteLine($"📊 Collected: {systemMetrics.CollectedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"🏷️  Process ID: {systemMetrics.ProcessId}");
        Console.WriteLine($"💻 Machine: {systemMetrics.MachineName}");
        Console.WriteLine();

        Console.WriteLine("📊 Process Information:");
        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"   ⏱️  Uptime: {FormatDuration(TimeSpan.FromMilliseconds(systemMetrics.UptimeMs))}");
        Console.WriteLine($"   🧠 Working Set: {FormatBytes(systemMetrics.WorkingSetBytes)}");
        Console.WriteLine($"   💾 Private Memory: {FormatBytes(systemMetrics.PrivateMemoryBytes)}");
        Console.WriteLine($"   🔄 CPU Time: {FormatDuration(TimeSpan.FromMilliseconds(systemMetrics.CpuTimeMs))}");
        Console.WriteLine($"   🧵 Threads: {systemMetrics.ThreadCount}");
        Console.WriteLine($"   🔧 Handles: {systemMetrics.HandleCount}");
        Console.WriteLine();

        Console.WriteLine("🗑️  Garbage Collection:");
        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"   📊 Total Memory: {FormatBytes(systemMetrics.TotalMemoryBytes)}");
        
        foreach (var (generation, collections) in systemMetrics.GcCollections)
        {
            Console.WriteLine($"   Gen {generation}: {collections:N0} collections");
        }
        Console.WriteLine();

        return 0;
    }

    private static async Task<int> ExportMetricsAsync(MetricsCollector metrics, string[] args)
    {
        Console.WriteLine("📤 Exporting Metrics");
        Console.WriteLine();

        var format = GetArgument(args, "--format") ?? "json";
        var outputPath = GetArgument(args, "--output") ?? $"metrics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{format}";

        Console.WriteLine($"📊 Format: {format.ToUpper()}");
        Console.WriteLine($"📁 Output: {outputPath}");
        Console.WriteLine();

        try
        {
            var snapshot = await metrics.GetSnapshotAsync();
            var systemMetrics = await metrics.GetSystemMetricsAsync();

            var exportData = new
            {
                ExportedAt = DateTime.UtcNow,
                Application = snapshot,
                System = systemMetrics
            };

            string content = format.ToLower() switch
            {
                "json" => JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                "csv" => ExportToCsv(snapshot),
                _ => throw new ArgumentException($"Unsupported format: {format}")
            };

            await File.WriteAllTextAsync(outputPath, content);

            Console.WriteLine($"✅ Metrics exported successfully");
            Console.WriteLine($"   📁 File: {outputPath}");
            Console.WriteLine($"   📊 Size: {FormatBytes(content.Length)}");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Export failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ClearMetricsAsync(MetricsCollector metrics)
    {
        Console.WriteLine("🗑️  Clearing Metrics");
        Console.WriteLine();

        // Note: MetricsCollector would need a Clear method for this to work
        Console.WriteLine("⚠️  Metrics clearing not implemented in current version");
        Console.WriteLine("   Metrics will be automatically rotated based on retention policy");
        Console.WriteLine();

        return 0;
    }

    private static string ExportToCsv(MetricsSnapshot snapshot)
    {
        var csv = new System.Text.StringBuilder();
        
        // Header
        csv.AppendLine("Timestamp,MetricType,Name,Count,Sum,Average,Min,Max,Value");
        
        // Counters
        foreach (var (name, counter) in snapshot.Counters)
        {
            csv.AppendLine($"{snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss},Counter,{name},{counter.Count},{counter.Sum},{counter.Average},{counter.Min},{counter.Max},{counter.Value}");
        }
        
        // Events
        foreach (var evt in snapshot.RecentEvents)
        {
            csv.AppendLine($"{evt.Timestamp:yyyy-MM-dd HH:mm:ss},Event,{evt.Name},1,{evt.Value},{evt.Value},{evt.Value},{evt.Value},");
        }
        
        return csv.ToString();
    }

    private static string? GetArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{duration.TotalDays:F1}d";
        if (duration.TotalHours >= 1)
            return $"{duration.TotalHours:F1}h";
        if (duration.TotalMinutes >= 1)
            return $"{duration.TotalMinutes:F1}m";
        return $"{duration.TotalSeconds:F1}s";
    }

    private static int ShowMetricsHelp()
    {
        Console.WriteLine("Usage: dbmigrator metrics <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  show                   Show current application metrics");
        Console.WriteLine("  system                 Show system performance metrics");
        Console.WriteLine("  export                 Export metrics to file");
        Console.WriteLine("  clear                  Clear accumulated metrics");
        Console.WriteLine();
        Console.WriteLine("Export Options:");
        Console.WriteLine("  --format <format>      Export format (json, csv)");
        Console.WriteLine("  --output <file>        Output file path");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator metrics show");
        Console.WriteLine("  dbmigrator metrics system");
        Console.WriteLine("  dbmigrator metrics export --format json --output report.json");
        Console.WriteLine("  dbmigrator metrics export --format csv --output metrics.csv");
        
        return 1;
    }
}