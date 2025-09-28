using Npgsql;

namespace DBMigrator.Core.Database;

public class ConnectionManager
{
    private readonly string _connectionString;

    public ConnectionManager(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public NpgsqlConnection GetConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = GetConnection();
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}