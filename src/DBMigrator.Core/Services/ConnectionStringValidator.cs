using Npgsql;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public static class ConnectionStringValidator
{
    public static ConnectionStringValidationResult Validate(string connectionString)
    {
        var result = new ConnectionStringValidationResult();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            result.IsValid = false;
            result.Errors.Add("Connection string cannot be null or empty");
            return result;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            
            // Validate required properties
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                result.Errors.Add("Host is required in connection string");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                result.Errors.Add("Database name is required in connection string");
            }

            if (string.IsNullOrWhiteSpace(builder.Username))
            {
                result.Errors.Add("Username is required in connection string");
            }

            // Validate port range
            if (builder.Port <= 0 || builder.Port > 65535)
            {
                result.Errors.Add($"Invalid port number: {builder.Port}. Must be between 1 and 65535");
            }

            // Validate timeout values
            if (builder.Timeout < 0)
            {
                result.Errors.Add($"Invalid timeout value: {builder.Timeout}. Must be >= 0");
            }

            if (builder.CommandTimeout < 0)
            {
                result.Errors.Add($"Invalid command timeout value: {builder.CommandTimeout}. Must be >= 0");
            }

            // Validate connection string format by attempting to parse it
            _ = new NpgsqlConnection(connectionString);

            result.IsValid = !result.Errors.Any();
            result.ParsedHost = builder.Host;
            result.ParsedDatabase = builder.Database;
            result.ParsedPort = builder.Port == 0 ? 5432 : builder.Port;
        }
        catch (ArgumentException ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Invalid connection string format: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Error parsing connection string: {ex.Message}");
        }

        return result;
    }

    public static async Task<ConnectionTestResult> TestConnectionAsync(string connectionString, int timeoutSeconds = 5)
    {
        var result = new ConnectionTestResult();
        
        // First validate the connection string format
        var validation = Validate(connectionString);
        if (!validation.IsValid)
        {
            result.IsSuccessful = false;
            result.Error = string.Join("; ", validation.Errors);
            return result;
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            
            var startTime = DateTime.UtcNow;
            
            // Create a cancellation token for timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            
            await connection.OpenAsync(cts.Token);
            
            // Test basic query
            using var command = new NpgsqlCommand("SELECT version()", connection);
            command.CommandTimeout = timeoutSeconds;
            var version = await command.ExecuteScalarAsync(cts.Token);
            
            result.IsSuccessful = true;
            result.ConnectionTime = DateTime.UtcNow - startTime;
            result.ServerVersion = version?.ToString();
        }
        catch (TimeoutException)
        {
            result.IsSuccessful = false;
            result.Error = $"Connection timeout after {timeoutSeconds} seconds";
        }
        catch (OperationCanceledException)
        {
            result.IsSuccessful = false;
            result.Error = $"Connection timeout after {timeoutSeconds} seconds";
        }
        catch (Npgsql.PostgresException ex)
        {
            result.IsSuccessful = false;
            result.Error = $"PostgreSQL error: {ex.MessageText} (Code: {ex.SqlState})";
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.Error = $"Connection failed: {ex.Message}";
        }

        return result;
    }

    public static string SanitizeConnectionStringForLogging(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            
            // Mask sensitive information
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }
            
            return builder.ToString();
        }
        catch
        {
            // If parsing fails, return a generic masked version
            return connectionString.Contains("Password=") 
                ? System.Text.RegularExpressions.Regex.Replace(connectionString, @"Password=[^;]*", "Password=***")
                : connectionString;
        }
    }
}

public class ConnectionStringValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public string? ParsedHost { get; set; }
    public string? ParsedDatabase { get; set; }
    public int ParsedPort { get; set; }
}

public class ConnectionTestResult
{
    public bool IsSuccessful { get; set; }
    public string? Error { get; set; }
    public TimeSpan ConnectionTime { get; set; }
    public string? ServerVersion { get; set; }
}