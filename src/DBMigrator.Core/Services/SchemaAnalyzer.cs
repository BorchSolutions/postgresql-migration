using System.Text.Json;
using DBMigrator.Core.Database;
using DBMigrator.Core.Models;
using DBMigrator.Core.Models.Schema;
using Npgsql;
using Schema = DBMigrator.Core.Models.Schema;

namespace DBMigrator.Core.Services;

public class SchemaAnalyzer
{
    private readonly ConnectionManager _connectionManager;
    private readonly ConfigurationManager _configurationManager;
    private const string BaselineFileName = ".baseline.json";

    public SchemaAnalyzer(ConnectionManager connectionManager, ConfigurationManager configurationManager)
    {
        _connectionManager = connectionManager;
        _configurationManager = configurationManager;
    }

    public async Task<DatabaseSchema> GetCurrentSchemaAsync(string schemaName = "public")
    {
        var schema = new DatabaseSchema { SchemaName = schemaName };
        
        using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        schema.Tables = await GetTablesAsync(connection, schemaName);
        schema.Functions = await GetFunctionsAsync(connection, schemaName);
        
        return schema;
    }

    private async Task<List<Table>> GetTablesAsync(NpgsqlConnection connection, string schemaName)
    {
        const string sql = @"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = @schema 
            AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        var tables = new List<Table>();
        
        // First, get all table names
        using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("schema", schemaName);
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                tables.Add(new Table { Name = tableName, Schema = schemaName });
            }
        }

        // Then load columns and indexes for each table
        foreach (var table in tables)
        {
            table.Columns = await GetColumnsAsync(connection, schemaName, table.Name);
            table.Indexes = await GetIndexesAsync(connection, schemaName, table.Name);
        }

        return tables;
    }

    private async Task<List<Column>> GetColumnsAsync(NpgsqlConnection connection, string schemaName, string tableName)
    {
        const string sql = @"
            SELECT 
                column_name,
                data_type,
                is_nullable,
                column_default,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                ordinal_position
            FROM information_schema.columns
            WHERE table_schema = @schema 
            AND table_name = @table
            ORDER BY ordinal_position";

        var columns = new List<Column>();
        
        using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("schema", schemaName);
            command.Parameters.AddWithValue("table", tableName);
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = new Column
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1),
                    IsNullable = reader.GetString(2) == "YES",
                    DefaultValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                    MaxLength = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Precision = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Scale = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    OrdinalPosition = reader.GetInt32(7)
                };

                columns.Add(column);
            }
        }

        // Check primary keys after reader is closed
        foreach (var column in columns)
        {
            column.IsPrimaryKey = await IsColumnPrimaryKeyAsync(connection, schemaName, tableName, column.Name);
        }

        return columns;
    }

    private async Task<List<Schema.Index>> GetIndexesAsync(NpgsqlConnection connection, string schemaName, string tableName)
    {
        const string sql = @"
            SELECT DISTINCT
                i.indexname,
                i.indexdef,
                ix.indisunique,
                ix.indisprimary
            FROM pg_indexes i
            JOIN pg_class c ON c.relname = i.tablename
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_index ix ON ix.indexrelid = (
                SELECT oid FROM pg_class WHERE relname = i.indexname
            )
            WHERE n.nspname = @schema 
            AND i.tablename = @table";

        var indexes = new List<Schema.Index>();
        
        using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("schema", schemaName);
            command.Parameters.AddWithValue("table", tableName);
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var indexName = reader.GetString(0);
                var indexDef = reader.GetString(1);
                var isUnique = reader.GetBoolean(2);
                var isPrimary = reader.GetBoolean(3);

                var index = new Schema.Index
                {
                    Name = indexName,
                    TableName = tableName,
                    IsUnique = isUnique,
                    IsPrimary = isPrimary,
                    Columns = ExtractColumnsFromIndexDef(indexDef)
                };

                indexes.Add(index);
            }
        }

        return indexes;
    }

    private async Task<bool> IsColumnPrimaryKeyAsync(NpgsqlConnection connection, string schemaName, string tableName, string columnName)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM information_schema.key_column_usage kcu
            JOIN information_schema.table_constraints tc 
                ON kcu.constraint_name = tc.constraint_name
            WHERE tc.constraint_type = 'PRIMARY KEY'
            AND kcu.table_schema = @schema
            AND kcu.table_name = @table
            AND kcu.column_name = @column";

        using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("schema", schemaName);
            command.Parameters.AddWithValue("table", tableName);
            command.Parameters.AddWithValue("column", columnName);
            
            var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }
    }

    private static List<string> ExtractColumnsFromIndexDef(string indexDef)
    {
        // Simple extraction from index definition
        // Example: "CREATE INDEX idx_name ON table_name (col1, col2)"
        var start = indexDef.IndexOf('(');
        var end = indexDef.LastIndexOf(')');
        
        if (start == -1 || end == -1) return new List<string>();
        
        var columnsStr = indexDef.Substring(start + 1, end - start - 1);
        return columnsStr.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private async Task<List<Function>> GetFunctionsAsync(NpgsqlConnection connection, string schemaName)
    {
        const string sql = @"
            SELECT 
                p.proname as function_name,
                n.nspname as schema_name,
                pg_get_function_result(p.oid) as return_type,
                pg_get_function_arguments(p.oid) as arguments,
                pg_get_functiondef(p.oid) as function_definition,
                l.lanname as language,
                p.provolatile as volatility,
                p.prosecdef as security_definer,
                r.rolname as owner,
                p.procost as cost,
                p.prorows as estimated_rows
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            JOIN pg_language l ON p.prolang = l.oid
            LEFT JOIN pg_roles r ON p.proowner = r.oid
            WHERE n.nspname = @schema
            AND p.prokind = 'f'  -- Only functions, not procedures or aggregates
            ORDER BY p.proname";

        var functions = new List<Function>();
        
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schemaName);
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var function = new Function
            {
                Name = GetSafeString(reader, "function_name"),
                Schema = GetSafeString(reader, "schema_name"),
                ReturnType = GetSafeString(reader, "return_type"),
                Body = GetSafeString(reader, "function_definition"),
                Language = GetSafeString(reader, "language"),
                IsVolatile = GetSafeString(reader, "volatility") != "i", // 'i' = immutable, others = volatile
                IsSecurityDefiner = reader.GetBoolean(reader.GetOrdinal("security_definer")),
                Owner = reader.IsDBNull(reader.GetOrdinal("owner")) ? null : GetSafeString(reader, "owner")
            };

            // Parse parameters from arguments string
            try
            {
                var argumentsString = GetSafeString(reader, "arguments");
                
                function.Parameters = ParseFunctionParameters(argumentsString);
            }
            catch (Exception ex)
            {
                // If parameter parsing fails, log the error but continue with empty parameters
                function.Parameters = new List<FunctionParameter>();
                Console.WriteLine($"Warning: Failed to parse parameters for function {function.Name}: {ex.Message}");
            }

            functions.Add(function);
        }
        
        return functions;
    }

    private List<FunctionParameter> ParseFunctionParameters(string argumentsString)
    {
        var parameters = new List<FunctionParameter>();
        
        if (string.IsNullOrWhiteSpace(argumentsString))
            return parameters;

        try
        {
            // Split by comma, but handle nested types like arrays
            var parts = SplitFunctionArguments(argumentsString);
            
            foreach (var part in parts)
            {
                try
                {
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    var parameter = new FunctionParameter();
                    
                    // Handle parameter modes (IN, OUT, INOUT)
                    if (trimmed.StartsWith("IN "))
                    {
                        parameter.Mode = "IN";
                        trimmed = trimmed[3..].Trim();
                    }
                    else if (trimmed.StartsWith("OUT "))
                    {
                        parameter.Mode = "OUT";
                        trimmed = trimmed[4..].Trim();
                    }
                    else if (trimmed.StartsWith("INOUT "))
                    {
                        parameter.Mode = "INOUT";
                        trimmed = trimmed[6..].Trim();
                    }
                    else
                    {
                        parameter.Mode = "IN"; // Default
                    }

                    // Handle DEFAULT values
                    var defaultIndex = trimmed.IndexOf(" DEFAULT ", StringComparison.OrdinalIgnoreCase);
                    if (defaultIndex >= 0)
                    {
                        parameter.DefaultValue = trimmed[(defaultIndex + 9)..].Trim();
                        trimmed = trimmed[..defaultIndex].Trim();
                    }

                    // Parse name and type - be more defensive
                    var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 2)
                    {
                        parameter.Name = SanitizeParameterName(tokens[0]);
                        parameter.DataType = SanitizeDataType(string.Join(" ", tokens[1..]));
                    }
                    else if (tokens.Length == 1)
                    {
                        // Only type provided (unnamed parameter)
                        parameter.Name = $"param_{parameters.Count + 1}";
                        parameter.DataType = SanitizeDataType(tokens[0]);
                    }
                    else
                    {
                        // Fallback for malformed parameters
                        parameter.Name = $"param_{parameters.Count + 1}";
                        parameter.DataType = "unknown";
                    }

                    parameters.Add(parameter);
                }
                catch (Exception ex)
                {
                    // Skip this parameter if parsing fails
                    Console.WriteLine($"Warning: Failed to parse parameter '{part}': {ex.Message}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            // If entire parsing fails, return empty list
            Console.WriteLine($"Warning: Failed to parse function arguments '{argumentsString}': {ex.Message}");
            return new List<FunctionParameter>();
        }
        
        return parameters;
    }

    private string SanitizeParameterName(string name)
    {
        // Remove any problematic characters from parameter names
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed_param";
        
        // Replace problematic characters
        return name.Replace("\"", "").Replace("'", "").Trim();
    }

    private string SanitizeDataType(string dataType)
    {
        // Handle problematic PostgreSQL data types
        if (string.IsNullOrWhiteSpace(dataType))
            return "unknown";
        
        // Common PostgreSQL type mappings and fixes
        dataType = dataType.Trim();
        
        // Handle quoted types
        if (dataType.StartsWith("\"") && dataType.EndsWith("\""))
            dataType = dataType[1..^1];
        
        // Map some problematic types
        return dataType switch
        {
            "char" => "character",
            "\"char\"" => "character", 
            _ => dataType
        };
    }

    private List<string> SplitFunctionArguments(string arguments)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var parenDepth = 0;
        var inQuotes = false;
        
        for (int i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            
            switch (c)
            {
                case '"' when i == 0 || arguments[i - 1] != '\\':
                    inQuotes = !inQuotes;
                    current.Append(c);
                    break;
                case '(' when !inQuotes:
                    parenDepth++;
                    current.Append(c);
                    break;
                case ')' when !inQuotes:
                    parenDepth--;
                    current.Append(c);
                    break;
                case ',' when !inQuotes && parenDepth == 0:
                    result.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }
        
        if (current.Length > 0)
            result.Add(current.ToString());
            
        return result;
    }

    private string GetSafeString(NpgsqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            
            if (reader.IsDBNull(ordinal))
                return "";
            
            // Try to get as string first
            try
            {
                return reader.GetString(ordinal);
            }
            catch (InvalidCastException)
            {
                // Fallback for problematic data types (like char)
                var value = reader.GetValue(ordinal);
                return value?.ToString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to read column '{columnName}': {ex.Message}");
            return "";
        }
    }

    public async Task SaveBaselineAsync(DatabaseSchema schema, string? migrationsPath = null)
    {
        var basePath = migrationsPath;
        if (string.IsNullOrEmpty(basePath))
        {
            var config = await _configurationManager.LoadConfigurationAsync();
            basePath = config.MigrationsPath;
        }

        var baselinePath = Path.Combine(basePath, BaselineFileName);

        Directory.CreateDirectory(basePath);

        try
        {
            var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            await File.WriteAllTextAsync(baselinePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save baseline to {baselinePath}: {ex.Message}", ex);
        }
    }

    public async Task<DatabaseSchema?> LoadBaselineAsync(string? migrationsPath = null)
    {
        var basePath = migrationsPath;
        if (string.IsNullOrEmpty(basePath))
        {
            var config = await _configurationManager.LoadConfigurationAsync();
            basePath = config.MigrationsPath;
        }

        var baselinePath = Path.Combine(basePath, BaselineFileName);

        if (!File.Exists(baselinePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(baselinePath);
            return JsonSerializer.Deserialize<DatabaseSchema>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load baseline from {baselinePath}: {ex.Message}", ex);
        }
    }
}