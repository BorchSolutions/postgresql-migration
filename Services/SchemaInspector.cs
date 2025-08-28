using BorchSolutions.PostgreSQL.Migration.Core;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BorchSolutions.PostgreSQL.Migration.Services;

public class SchemaObject
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string CreateScript { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class SchemaDefinitions
{
    public List<SchemaObject> Tables { get; set; } = new();
    public List<SchemaObject> Indexes { get; set; } = new();
    public List<SchemaObject> Functions { get; set; } = new();
    public List<SchemaObject> Triggers { get; set; } = new();
    public List<SchemaObject> Sequences { get; set; } = new();
    public List<SchemaObject> Views { get; set; } = new();
}

public interface ISchemaInspector
{
    Task<SchemaDefinitions> GetSchemaDefinitionsAsync(string? connectionName = null);
    Task<List<string>> GetTableNamesAsync(string? connectionName = null);
    Task<SchemaObject?> GetTableDefinitionAsync(string tableName, string? connectionName = null);
    Task<List<SchemaObject>> GetFunctionDefinitionsAsync(string? connectionName = null);
}

public class SchemaInspector : ISchemaInspector
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<SchemaInspector> _logger;

    public SchemaInspector(IConnectionManager connectionManager, ILogger<SchemaInspector> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<SchemaDefinitions> GetSchemaDefinitionsAsync(string? connectionName = null)
    {
        var definitions = new SchemaDefinitions();

        using var connection = await _connectionManager.GetConnectionAsync(connectionName);

        // Obtener tablas
        _logger.LogDebug("🔍 Inspeccionando tablas...");
        definitions.Tables = await GetTablesAsync(connection);

        // Obtener índices
        _logger.LogDebug("🔍 Inspeccionando índices...");
        definitions.Indexes = await GetIndexesAsync(connection);

        // Obtener funciones
        _logger.LogDebug("🔍 Inspeccionando funciones...");
        definitions.Functions = await GetFunctionsAsync(connection);

        // Obtener triggers
        _logger.LogDebug("🔍 Inspeccionando triggers...");
        definitions.Triggers = await GetTriggersAsync(connection);

        // Obtener secuencias
        _logger.LogDebug("🔍 Inspeccionando secuencias...");
        definitions.Sequences = await GetSequencesAsync(connection);

        // Obtener vistas
        _logger.LogDebug("🔍 Inspeccionando vistas...");
        definitions.Views = await GetViewsAsync(connection);

        _logger.LogInformation("✅ Inspección completada - Tablas: {Tables}, Índices: {Indexes}, Funciones: {Functions}",
            definitions.Tables.Count, definitions.Indexes.Count, definitions.Functions.Count);

        return definitions;
    }

    public async Task<List<string>> GetTableNamesAsync(string? connectionName = null)
    {
        using var connection = await _connectionManager.GetConnectionAsync(connectionName);
        
        var query = @"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' 
                AND table_type = 'BASE TABLE'
                AND table_name NOT IN ('borchsolutions_schema_migrations', 'borchsolutions_data_migrations')
            ORDER BY table_name";

        var tableNames = new List<string>();
        using var command = new NpgsqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    public async Task<SchemaObject?> GetTableDefinitionAsync(string tableName, string? connectionName = null)
    {
        using var connection = await _connectionManager.GetConnectionAsync(connectionName);
        
        var tables = await GetTablesAsync(connection, tableName);
        return tables.FirstOrDefault();
    }

    public async Task<List<SchemaObject>> GetFunctionDefinitionsAsync(string? connectionName = null)
    {
        using var connection = await _connectionManager.GetConnectionAsync(connectionName);
        return await GetFunctionsAsync(connection);
    }

    private async Task<List<SchemaObject>> GetTablesAsync(NpgsqlConnection connection, string? specificTable = null)
    {
        var tables = new List<SchemaObject>();
        
        var tableQuery = @"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' 
                AND table_type = 'BASE TABLE'
                AND table_name NOT IN ('borchsolutions_schema_migrations', 'borchsolutions_data_migrations')";
        
        if (!string.IsNullOrEmpty(specificTable))
        {
            tableQuery += " AND table_name = @tableName";
        }
        
        tableQuery += " ORDER BY table_name";

        var tableNames = new List<string>();
        
        // Primero obtener todos los nombres de tabla
        using (var cmd = new NpgsqlCommand(tableQuery, connection))
        {
            if (!string.IsNullOrEmpty(specificTable))
            {
                cmd.Parameters.AddWithValue("@tableName", specificTable);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        // Luego generar el CREATE TABLE para cada tabla
        foreach (var tableName in tableNames)
        {
            var createScript = await GenerateCreateTableScriptAsync(connection, tableName);
            tables.Add(new SchemaObject
            {
                Name = tableName,
                Type = "TABLE",
                Schema = "public",
                CreateScript = createScript
            });
        }

        return tables;
    }

    private async Task<string> GenerateCreateTableScriptAsync(NpgsqlConnection connection, string tableName)
    {
        var script = new List<string>();
        script.Add($"CREATE TABLE IF NOT EXISTS {tableName} (");

        // Obtener columnas
        var columnQuery = @"
            SELECT 
                column_name,
                data_type,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                column_default,
                ordinal_position
            FROM information_schema.columns 
            WHERE table_schema = 'public' 
                AND table_name = @tableName
            ORDER BY ordinal_position";

        var columns = new List<string>();
        using (var columnCmd = new NpgsqlCommand(columnQuery, connection))
        {
            columnCmd.Parameters.AddWithValue("@tableName", tableName);
            using var reader = await columnCmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var columnDef = GenerateColumnDefinition(reader);
                columns.Add(columnDef);
            }
        }

        script.AddRange(columns.Select((col, index) => index == columns.Count - 1 ? $"    {col}" : $"    {col},"));

        // Obtener constraints
        var constraintQuery = @"
            SELECT 
                tc.constraint_name,
                tc.constraint_type,
                string_agg(kcu.column_name, ', ' ORDER BY kcu.ordinal_position) as columns
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
            WHERE tc.table_schema = 'public' 
                AND tc.table_name = @tableName
                AND tc.constraint_type IN ('PRIMARY KEY', 'UNIQUE')
            GROUP BY tc.constraint_name, tc.constraint_type
            ORDER BY CASE 
                WHEN tc.constraint_type = 'PRIMARY KEY' THEN 1 
                WHEN tc.constraint_type = 'UNIQUE' THEN 2 
                ELSE 3 
            END";

        var constraints = new List<string>();
        using (var constraintCmd = new NpgsqlCommand(constraintQuery, connection))
        {
            constraintCmd.Parameters.AddWithValue("@tableName", tableName);
            using var constraintReader = await constraintCmd.ExecuteReaderAsync();
            
            while (await constraintReader.ReadAsync())
            {
                var constraintType = constraintReader["constraint_type"].ToString();
                var columns_list = constraintReader["columns"].ToString();
                
                var constraintDef = constraintType switch
                {
                    "PRIMARY KEY" => $"    PRIMARY KEY ({columns_list})",
                    "UNIQUE" => $"    UNIQUE ({columns_list})",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(constraintDef))
                {
                    constraints.Add(constraintDef);
                }
            }
        }

        if (constraints.Any())
        {
            script.Add(",");
            script.AddRange(constraints.Select((cons, index) => index == constraints.Count - 1 ? cons : $"{cons},"));
        }

        script.Add(");");
        return string.Join("\n", script);
    }

    private string GenerateColumnDefinition(NpgsqlDataReader reader)
    {
        var columnName = reader["column_name"].ToString();
        var dataType = reader["data_type"].ToString();
        var maxLength = reader["character_maximum_length"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["character_maximum_length"]);
        var isNullable = reader["is_nullable"].ToString() == "YES";
        var defaultValue = reader["column_default"] == DBNull.Value ? null : reader["column_default"].ToString();

        var columnDef = $"{columnName} ";

        // Mapear tipos de datos PostgreSQL
        columnDef += dataType.ToLower() switch
        {
            "character varying" => $"VARCHAR({maxLength ?? 255})",
            "integer" => "INTEGER",
            "bigint" => "BIGINT",
            "smallint" => "SMALLINT",
            "boolean" => "BOOLEAN",
            "timestamp without time zone" => "TIMESTAMP",
            "timestamp with time zone" => "TIMESTAMPTZ",
            "date" => "DATE",
            "time without time zone" => "TIME",
            "text" => "TEXT",
            "numeric" => GetNumericType(reader),
            "decimal" => GetNumericType(reader),
            "real" => "REAL",
            "double precision" => "DOUBLE PRECISION",
            "uuid" => "UUID",
            "json" => "JSON",
            "jsonb" => "JSONB",
            _ => dataType.ToUpper() + (maxLength.HasValue ? $"({maxLength})" : "")
        };

        // NOT NULL
        if (!isNullable)
        {
            columnDef += " NOT NULL";
        }

        // DEFAULT
        if (!string.IsNullOrEmpty(defaultValue))
        {
            columnDef += $" DEFAULT {defaultValue}";
        }

        return columnDef;
    }

    private string GetNumericType(NpgsqlDataReader reader)
    {
        var precision = reader["numeric_precision"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["numeric_precision"]);
        var scale = reader["numeric_scale"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["numeric_scale"]);
        
        if (precision.HasValue && scale.HasValue)
        {
            return $"NUMERIC({precision},{scale})";
        }
        else if (precision.HasValue)
        {
            return $"NUMERIC({precision})";
        }
        
        return "NUMERIC";
    }

    private async Task<List<SchemaObject>> GetIndexesAsync(NpgsqlConnection connection)
    {
        var indexes = new List<SchemaObject>();
        
        var query = @"
            SELECT 
                indexname,
                indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
                AND indexname NOT LIKE '%_pkey'
                AND indexdef NOT LIKE '%UNIQUE%'
            ORDER BY indexname";

        using var cmd = new NpgsqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var indexName = reader["indexname"].ToString();
            var indexDef = reader["indexdef"].ToString();
            
            // Modificar para usar IF NOT EXISTS
            indexDef = indexDef.Replace("CREATE INDEX", "CREATE INDEX IF NOT EXISTS");
            
            indexes.Add(new SchemaObject
            {
                Name = indexName,
                Type = "INDEX",
                Schema = "public",
                CreateScript = indexDef + ";"
            });
        }

        return indexes;
    }

    private async Task<List<SchemaObject>> GetFunctionsAsync(NpgsqlConnection connection)
    {
        var functions = new List<SchemaObject>();
        
        var query = @"
            SELECT 
                p.proname as function_name,
                pg_get_functiondef(p.oid) as function_definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'public'
                AND p.prokind IN ('f', 'p')
            ORDER BY p.proname";

        using var cmd = new NpgsqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var functionName = reader["function_name"].ToString();
            var functionDef = reader["function_definition"].ToString();
            
            functions.Add(new SchemaObject
            {
                Name = functionName,
                Type = "FUNCTION",
                Schema = "public",
                CreateScript = functionDef + ";"
            });
        }

        return functions;
    }

    private async Task<List<SchemaObject>> GetTriggersAsync(NpgsqlConnection connection)
    {
        var triggers = new List<SchemaObject>();
        
        var query = @"
            SELECT 
                t.trigger_name,
                t.event_manipulation,
                t.event_object_table,
                t.action_statement,
                t.action_timing
            FROM information_schema.triggers t
            WHERE t.trigger_schema = 'public'
            ORDER BY t.trigger_name";

        using var cmd = new NpgsqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var triggerName = reader["trigger_name"].ToString();
            var manipulation = reader["event_manipulation"].ToString();
            var table = reader["event_object_table"].ToString();
            var timing = reader["action_timing"].ToString();
            var statement = reader["action_statement"].ToString();

            var createTrigger = $@"CREATE OR REPLACE TRIGGER {triggerName}
    {timing} {manipulation} ON {table}
    FOR EACH ROW
    {statement};";

            triggers.Add(new SchemaObject
            {
                Name = triggerName,
                Type = "TRIGGER",
                Schema = "public",
                CreateScript = createTrigger
            });
        }

        return triggers;
    }

    private async Task<List<SchemaObject>> GetSequencesAsync(NpgsqlConnection connection)
    {
        var sequences = new List<SchemaObject>();
        
        var query = @"
            SELECT 
                sequence_name,
                COALESCE(start_value::text, '1') as start_value,
                COALESCE(minimum_value::text, '1') as minimum_value,
                COALESCE(maximum_value::text, '9223372036854775807') as maximum_value,
                COALESCE(increment::text, '1') as increment
            FROM information_schema.sequences
            WHERE sequence_schema = 'public'
            ORDER BY sequence_name";

        using var cmd = new NpgsqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var sequenceName = reader["sequence_name"].ToString();
            var startValue = reader["start_value"].ToString();
            var minValue = reader["minimum_value"].ToString();
            var maxValue = reader["maximum_value"].ToString();
            var increment = reader["increment"].ToString();

            var createSeq = $@"CREATE SEQUENCE IF NOT EXISTS {sequenceName}
    START WITH {startValue}
    INCREMENT BY {increment}
    MINVALUE {minValue}
    MAXVALUE {maxValue};";

            sequences.Add(new SchemaObject
            {
                Name = sequenceName,
                Type = "SEQUENCE",
                Schema = "public",
                CreateScript = createSeq
            });
        }

        return sequences;
    }

    private async Task<List<SchemaObject>> GetViewsAsync(NpgsqlConnection connection)
    {
        var views = new List<SchemaObject>();
        
        var query = @"
            SELECT 
                table_name,
                view_definition
            FROM information_schema.views
            WHERE table_schema = 'public'
            ORDER BY table_name";

        using var cmd = new NpgsqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var viewName = reader["table_name"].ToString();
            var viewDef = reader["view_definition"].ToString();

            var createView = $"CREATE OR REPLACE VIEW {viewName} AS\n{viewDef};";

            views.Add(new SchemaObject
            {
                Name = viewName,
                Type = "VIEW",
                Schema = "public",
                CreateScript = createView
            });
        }

        return views;
    }
}