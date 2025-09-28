using System.Text.Json;

namespace DBMigrator.Core.Services;

public class StructuredLogger
{
    private readonly string _logLevel;
    private readonly bool _enableConsoleOutput;
    private readonly string? _logFilePath;

    public StructuredLogger(string logLevel = "Info", bool enableConsoleOutput = true, string? logFilePath = null)
    {
        _logLevel = logLevel;
        _enableConsoleOutput = enableConsoleOutput;
        _logFilePath = logFilePath;
    }

    public async Task LogAsync(LogLevel level, string message, object? data = null, Exception? exception = null)
    {
        if (!ShouldLog(level))
            return;

        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level.ToString(),
            Message = message,
            Data = data,
            Exception = exception?.ToString(),
            Source = GetCallingMethod()
        };

        var jsonLog = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        if (_enableConsoleOutput)
        {
            WriteToConsole(level, logEntry);
        }

        if (!string.IsNullOrEmpty(_logFilePath))
        {
            await WriteToFileAsync(jsonLog);
        }
    }

    public async Task LogMigrationStartAsync(string migrationId, string migrationFile)
    {
        await LogAsync(LogLevel.Info, "Migration started", new
        {
            MigrationId = migrationId,
            MigrationFile = migrationFile,
            Action = "MigrationStart"
        });
    }

    public async Task LogMigrationCompletedAsync(string migrationId, TimeSpan duration, int stepsExecuted)
    {
        await LogAsync(LogLevel.Info, "Migration completed successfully", new
        {
            MigrationId = migrationId,
            Duration = duration.TotalMilliseconds,
            StepsExecuted = stepsExecuted,
            Action = "MigrationCompleted"
        });
    }

    public async Task LogMigrationFailedAsync(string migrationId, Exception exception, string? failedStep = null)
    {
        await LogAsync(LogLevel.Error, "Migration failed", new
        {
            MigrationId = migrationId,
            FailedStep = failedStep,
            Action = "MigrationFailed"
        }, exception);
    }

    public async Task LogDryRunAsync(string migrationId, object dryRunResult)
    {
        await LogAsync(LogLevel.Info, "Dry run completed", new
        {
            MigrationId = migrationId,
            DryRunResult = dryRunResult,
            Action = "DryRunCompleted"
        });
    }

    public async Task LogConflictDetectedAsync(string conflictType, object conflictDetails)
    {
        await LogAsync(LogLevel.Warning, "Migration conflict detected", new
        {
            ConflictType = conflictType,
            ConflictDetails = conflictDetails,
            Action = "ConflictDetected"
        });
    }

    public async Task LogSchemaAnalysisAsync(string action, object analysisResult)
    {
        await LogAsync(LogLevel.Info, $"Schema analysis: {action}", new
        {
            Action = $"SchemaAnalysis_{action}",
            Result = analysisResult
        });
    }

    public async Task LogConnectionEventAsync(string eventType, string? connectionString = null, Exception? exception = null)
    {
        // Sanitize connection string
        var sanitizedConnectionString = SanitizeConnectionString(connectionString);
        
        await LogAsync(
            exception == null ? LogLevel.Info : LogLevel.Error,
            $"Database connection {eventType}",
            new
            {
                EventType = eventType,
                ConnectionString = sanitizedConnectionString,
                Action = $"Connection_{eventType}"
            },
            exception
        );
    }

    public async Task LogPerformanceAsync(string operation, TimeSpan duration, object? metrics = null)
    {
        await LogAsync(LogLevel.Info, $"Performance metric: {operation}", new
        {
            Operation = operation,
            Duration = duration.TotalMilliseconds,
            Metrics = metrics,
            Action = "Performance"
        });
    }

    private bool ShouldLog(LogLevel level)
    {
        var configuredLevel = Enum.Parse<LogLevel>(_logLevel, true);
        return level >= configuredLevel;
    }

    private void WriteToConsole(LogLevel level, LogEntry entry)
    {
        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        
        var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] [{level}] {entry.Message}");
        
        if (entry.Data != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            var dataJson = JsonSerializer.Serialize(entry.Data, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true 
            });
            Console.WriteLine($"Data: {dataJson}");
        }

        if (!string.IsNullOrEmpty(entry.Exception))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Exception: {entry.Exception}");
        }

        Console.ForegroundColor = originalColor;
    }

    private async Task WriteToFileAsync(string jsonLog)
    {
        try
        {
            await File.AppendAllTextAsync(_logFilePath!, jsonLog + Environment.NewLine);
        }
        catch (Exception)
        {
            // Silently fail to avoid logging loops
        }
    }

    private string GetCallingMethod()
    {
        try
        {
            var frame = new System.Diagnostics.StackFrame(3, false);
            var method = frame.GetMethod();
            if (method != null)
            {
                return $"{method.DeclaringType?.Name}.{method.Name}";
            }
        }
        catch
        {
            // Ignore errors getting calling method
        }
        return "Unknown";
    }

    private string? SanitizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        // Remove sensitive information from connection string
        var sanitized = connectionString;
        var sensitiveKeys = new[] { "password", "pwd", "user id", "uid" };

        foreach (var key in sensitiveKeys)
        {
            var pattern = $@"{key}=[^;]*;?";
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, 
                pattern, 
                $"{key}=***;", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        return sanitized;
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? Exception { get; set; }
    public string Source { get; set; } = string.Empty;
}

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}