using BorchSolutions.PostgreSQL.Migration.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BorchSolutions.PostgreSQL.Migration.Core;

public interface IConnectionManager
{
    Task<NpgsqlConnection> GetConnectionAsync(string? connectionName = null);
    Task<bool> TestConnectionAsync(string? connectionName = null);
    Task<DatabaseInfo> GetDatabaseInfoAsync(string? connectionName = null);
    List<DatabaseConnection> GetAvailableConnections();
}

public class ConnectionManager : IConnectionManager
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly Dictionary<string, string> _connections;

    public ConnectionManager(IConfiguration configuration, ILogger<ConnectionManager> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connections = LoadConnections();
    }

    public async Task<NpgsqlConnection> GetConnectionAsync(string? connectionName = null)
    {
        var connectionString = GetConnectionString(connectionName);
        var connection = new NpgsqlConnection(connectionString);
        
        try
        {
            await connection.OpenAsync();
            _logger.LogDebug("Conexión establecida exitosamente a {ConnectionName}", connectionName ?? "Default");
            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error estableciendo conexión a {ConnectionName}", connectionName ?? "Default");
            connection.Dispose();
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(string? connectionName = null)
    {
        try
        {
            using var connection = await GetConnectionAsync(connectionName);
            using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test de conexión falló para {ConnectionName}", connectionName ?? "Default");
            return false;
        }
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync(string? connectionName = null)
    {
        using var connection = await GetConnectionAsync(connectionName);
        
        var info = new DatabaseInfo
        {
            DatabaseName = connection.Database
        };

        // Versión del servidor
        using (var versionCmd = new NpgsqlCommand("SELECT version()", connection))
        {
            var version = await versionCmd.ExecuteScalarAsync() as string;
            info.ServerVersion = version?.Split(' ')[1] ?? "Unknown";
        }

        // Contar tablas
        using (var tableCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM information_schema.tables 
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connection))
        {
            info.TableCount = Convert.ToInt32(await tableCmd.ExecuteScalarAsync());
        }

        // Contar funciones
        using (var funcCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM information_schema.routines 
            WHERE routine_schema = 'public'", connection))
        {
            info.FunctionCount = Convert.ToInt32(await funcCmd.ExecuteScalarAsync());
        }

        // Contar índices
        using (var indexCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public'", connection))
        {
            info.IndexCount = Convert.ToInt32(await indexCmd.ExecuteScalarAsync());
        }

        // Contar triggers
        using (var triggerCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM information_schema.triggers 
            WHERE trigger_schema = 'public'", connection))
        {
            info.TriggerCount = Convert.ToInt32(await triggerCmd.ExecuteScalarAsync());
        }

        return info;
    }

    public List<DatabaseConnection> GetAvailableConnections()
    {
        var connections = new List<DatabaseConnection>();
        
        // Conexión por defecto
        connections.Add(new DatabaseConnection
        {
            Name = "Default",
            ConnectionString = GetConnectionString(),
            Environment = "Default",
            IsDefault = true
        });

        // Conexiones de target databases
        var targetSection = _configuration.GetSection("ConnectionStrings:TargetDatabases");
        if (targetSection.Exists())
        {
            foreach (var child in targetSection.GetChildren())
            {
                connections.Add(new DatabaseConnection
                {
                    Name = child.Key,
                    ConnectionString = child.Value ?? "",
                    Environment = child.Key,
                    IsDefault = false
                });
            }
        }

        return connections;
    }

    private Dictionary<string, string> LoadConnections()
    {
        var connections = new Dictionary<string, string>();
        
        // Conexión por defecto
        var defaultConnection = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(defaultConnection))
        {
            connections["Default"] = defaultConnection;
        }

        // Conexiones adicionales
        var targetSection = _configuration.GetSection("ConnectionStrings:TargetDatabases");
        if (targetSection.Exists())
        {
            foreach (var child in targetSection.GetChildren())
            {
                if (!string.IsNullOrEmpty(child.Value))
                {
                    connections[child.Key] = child.Value;
                }
            }
        }

        _logger.LogInformation("Cargadas {Count} conexiones de base de datos", connections.Count);
        return connections;
    }

    private string GetConnectionString(string? connectionName = null)
    {
        if (string.IsNullOrEmpty(connectionName))
        {
            connectionName = "Default";
        }

        if (_connections.TryGetValue(connectionName, out var connectionString))
        {
            return connectionString;
        }

        throw new InvalidOperationException($"Conexión '{connectionName}' no encontrada en configuración");
    }
}