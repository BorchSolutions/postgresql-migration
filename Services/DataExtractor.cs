using BorchSolutions.PostgreSQL.Migration.Core;
using BorchSolutions.PostgreSQL.Migration.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text;

namespace BorchSolutions.PostgreSQL.Migration.Services;

public class TableDataInfo
{
    public string TableName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<string> PrimaryKeyColumns { get; set; } = new();
    public Dictionary<string, string> ColumnTypes { get; set; } = new();
}

public interface IDataExtractor
{
    Task<string> GenerateDataScriptAsync(List<string> tableNames, string? connectionName = null);
    Task<string> GenerateDataScriptForTableAsync(string tableName, string? connectionName = null, int maxRows = 10000);
    Task<List<TableDataInfo>> GetTableDataInfoAsync(List<string> tableNames, string? connectionName = null);
    Task<bool> ExportTableDataAsync(string tableName, string outputPath, string? connectionName = null);
}

public class DataExtractor : IDataExtractor
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<DataExtractor> _logger;
    private readonly int _defaultMaxRows = 10000;
    private readonly int _batchSize = 1000;

    public DataExtractor(IConnectionManager connectionManager, ILogger<DataExtractor> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<string> GenerateDataScriptAsync(List<string> tableNames, string? connectionName = null)
    {
        var script = new StringBuilder();
        
        // Header
        script.AppendLine("-- ========================================");
        script.AppendLine("-- SCRIPT DE DATOS GENERADO POR BORCHSOLUTIONS");
        script.AppendLine($"-- Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        script.AppendLine($"-- Tablas: {string.Join(", ", tableNames)}");
        script.AppendLine("-- ========================================");
        script.AppendLine();

        var totalRows = 0;

        foreach (var tableName in tableNames)
        {
            _logger.LogDebug("🔄 Extrayendo datos de tabla: {TableName}", tableName);
            
            var tableScript = await GenerateDataScriptForTableAsync(tableName, connectionName);
            if (!string.IsNullOrEmpty(tableScript))
            {
                // Contar filas en el script
                var rows = CountRowsInScript(tableScript);
                totalRows += rows;
                
                script.AppendLine($"-- ========================================");
                script.AppendLine($"-- TABLA: {tableName.ToUpper()} ({rows} registros)");
                script.AppendLine($"-- ========================================");
                script.AppendLine(tableScript);
                script.AppendLine();
                
                _logger.LogDebug("✅ Datos extraídos de {TableName}: {Rows} registros", tableName, rows);
            }
            else
            {
                _logger.LogWarning("⚠️  No se pudieron extraer datos de tabla: {TableName}", tableName);
            }
        }

        script.AppendLine("-- ========================================");
        script.AppendLine($"-- FIN DEL SCRIPT - TOTAL: {totalRows} registros");
        script.AppendLine("-- ========================================");

        _logger.LogInformation("✅ Script de datos generado - {Tables} tablas, {Rows} registros", 
            tableNames.Count, totalRows);

        return script.ToString();
    }

    public async Task<string> GenerateDataScriptForTableAsync(string tableName, string? connectionName = null, int maxRows = 10000)
    {
        try
        {
            using var connection = await _connectionManager.GetConnectionAsync(connectionName);
            
            // Verificar si la tabla existe y obtener información
            var tableInfo = await GetTableInfoAsync(connection, tableName);
            if (tableInfo == null)
            {
                _logger.LogWarning("⚠️  Tabla no encontrada: {TableName}", tableName);
                return string.Empty;
            }

            if (tableInfo.RowCount == 0)
            {
                _logger.LogInformation("ℹ️  Tabla vacía: {TableName}", tableName);
                return $"-- Tabla {tableName} está vacía\n";
            }

            if (tableInfo.RowCount > maxRows)
            {
                _logger.LogWarning("⚠️  Tabla {TableName} tiene {RowCount} registros (límite: {MaxRows}). Se extraerán solo los primeros {MaxRows}.", 
                    tableName, tableInfo.RowCount, maxRows);
            }

            // Generar script INSERT
            return await GenerateInsertScriptAsync(connection, tableInfo, Math.Min(tableInfo.RowCount, maxRows));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generando script de datos para tabla: {TableName}", tableName);
            return string.Empty;
        }
    }

    public async Task<List<TableDataInfo>> GetTableDataInfoAsync(List<string> tableNames, string? connectionName = null)
    {
        var tablesInfo = new List<TableDataInfo>();

        using var connection = await _connectionManager.GetConnectionAsync(connectionName);

        foreach (var tableName in tableNames)
        {
            var info = await GetTableInfoAsync(connection, tableName);
            if (info != null)
            {
                tablesInfo.Add(info);
            }
        }

        return tablesInfo;
    }

    public async Task<bool> ExportTableDataAsync(string tableName, string outputPath, string? connectionName = null)
    {
        try
        {
            _logger.LogInformation("📁 Exportando datos de {TableName} a {OutputPath}", tableName, outputPath);
            
            var script = await GenerateDataScriptForTableAsync(tableName, connectionName);
            if (string.IsNullOrEmpty(script))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, script);
            
            _logger.LogInformation("✅ Datos exportados exitosamente a {OutputPath}", outputPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error exportando datos de tabla: {TableName}", tableName);
            return false;
        }
    }

    private async Task<TableDataInfo?> GetTableInfoAsync(NpgsqlConnection connection, string tableName)
    {
        try
        {
            // Verificar existencia de tabla
            var existsQuery = @"
                SELECT table_name
                FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = @tableName";

            using (var existsCmd = new NpgsqlCommand(existsQuery, connection))
            {
                existsCmd.Parameters.AddWithValue("@tableName", tableName.ToLower());
                var exists = await existsCmd.ExecuteScalarAsync();
                
                if (exists == null)
                {
                    return null;
                }
            }

            var tableInfo = new TableDataInfo { TableName = tableName };

            // Contar registros
            var countQuery = $"SELECT COUNT(*) FROM \"{tableName}\"";
            using (var countCmd = new NpgsqlCommand(countQuery, connection))
            {
                var countResult = await countCmd.ExecuteScalarAsync();
                tableInfo.RowCount = countResult != null ? Convert.ToInt32(countResult) : 0;
            }

            // Obtener información de columnas
            var columnQuery = @"
                SELECT 
                    column_name,
                    data_type,
                    character_maximum_length,
                    numeric_precision,
                    numeric_scale,
                    is_nullable
                FROM information_schema.columns 
                WHERE table_schema = 'public' 
                AND table_name = @tableName
                ORDER BY ordinal_position";

            using (var columnCmd = new NpgsqlCommand(columnQuery, connection))
            {
                columnCmd.Parameters.AddWithValue("@tableName", tableName.ToLower());
                using var reader = await columnCmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString("column_name");
                    var dataType = reader.GetString("data_type");
                    var maxLength = reader.IsDBNull("character_maximum_length") ? (int?)null : reader.GetInt32("character_maximum_length");
                    
                    tableInfo.Columns.Add(columnName);
                    
                    // Formatear tipo de dato
                    var formattedType = FormatDataType(dataType, maxLength);
                    tableInfo.ColumnTypes[columnName] = formattedType;
                }
            }

            // Obtener claves primarias
            var pkQuery = @"
                SELECT column_name
                FROM information_schema.key_column_usage kcu
                JOIN information_schema.table_constraints tc 
                    ON kcu.constraint_name = tc.constraint_name
                WHERE tc.table_schema = 'public' 
                AND tc.table_name = @tableName
                AND tc.constraint_type = 'PRIMARY KEY'
                ORDER BY kcu.ordinal_position";

            using (var pkCmd = new NpgsqlCommand(pkQuery, connection))
            {
                pkCmd.Parameters.AddWithValue("@tableName", tableName.ToLower());
                using var pkReader = await pkCmd.ExecuteReaderAsync();
                
                while (await pkReader.ReadAsync())
                {
                    tableInfo.PrimaryKeyColumns.Add(pkReader.GetString("column_name"));
                }
            }

            return tableInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error obteniendo información de tabla: {TableName}", tableName);
            return null;
        }
    }

    private async Task<string> GenerateInsertScriptAsync(NpgsqlConnection connection, TableDataInfo tableInfo, int maxRows)
    {
        var script = new StringBuilder();
        
        // Generar INSERT con ON CONFLICT para hacer el script idempotente
        var columnList = string.Join(", ", tableInfo.Columns.Select(c => $"\"{c}\""));
        var tableName = tableInfo.TableName;
        
        // Construir cláusula ON CONFLICT
        var onConflictClause = GenerateOnConflictClause(tableInfo);
        
        script.AppendLine($"-- Insertar datos en tabla {tableName}");
        
        // Consultar datos en lotes
        var offset = 0;
        var totalInserted = 0;
        
        while (offset < maxRows)
        {
            var batchSize = Math.Min(_batchSize, maxRows - offset);
            var dataQuery = $@"
                SELECT {columnList}
                FROM ""{tableName}""
                ORDER BY {GetOrderByClause(tableInfo)}
                LIMIT {batchSize} OFFSET {offset}";

            using var dataCmd = new NpgsqlCommand(dataQuery, connection);
            using var reader = await dataCmd.ExecuteReaderAsync();
            
            var values = new List<string>();
            
            while (await reader.ReadAsync())
            {
                var valueList = new List<string>();
                
                for (int i = 0; i < tableInfo.Columns.Count; i++)
                {
                    var value = reader.IsDBNull(i) ? "NULL" : FormatValue(reader.GetValue(i), tableInfo.ColumnTypes[tableInfo.Columns[i]]);
                    valueList.Add(value);
                }
                
                values.Add($"    ({string.Join(", ", valueList)})");
                totalInserted++;
            }
            
            if (values.Any())
            {
                script.AppendLine($"INSERT INTO \"{tableName}\" ({columnList})");
                script.AppendLine("VALUES");
                script.AppendLine(string.Join(",\n", values));
                script.AppendLine(onConflictClause);
                script.AppendLine();
            }
            
            offset += batchSize;
        }

        // Resetear secuencias si la tabla tiene claves primarias seriales
        if (tableInfo.PrimaryKeyColumns.Any())
        {
            foreach (var pkColumn in tableInfo.PrimaryKeyColumns)
            {
                if (await IsSerialColumnAsync(connection, tableName, pkColumn))
                {
                    var sequenceName = $"{tableName}_{pkColumn}_seq";
                    script.AppendLine($"-- Resetear secuencia para {pkColumn}");
                    script.AppendLine($"SELECT SETVAL('{sequenceName}', COALESCE((SELECT MAX(\"{pkColumn}\") FROM \"{tableName}\"), 1));");
                    script.AppendLine();
                }
            }
        }

        return script.ToString();
    }

    private string GenerateOnConflictClause(TableDataInfo tableInfo)
    {
        if (tableInfo.PrimaryKeyColumns.Any())
        {
            var pkColumns = string.Join(", ", tableInfo.PrimaryKeyColumns.Select(c => $"\"{c}\""));
            var updateClauses = tableInfo.Columns
                .Where(c => !tableInfo.PrimaryKeyColumns.Contains(c))
                .Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");
            
            if (updateClauses.Any())
            {
                return $"ON CONFLICT ({pkColumns}) DO UPDATE SET\n    {string.Join(",\n    ", updateClauses)};";
            }
            else
            {
                return $"ON CONFLICT ({pkColumns}) DO NOTHING;";
            }
        }
        
        return "ON CONFLICT DO NOTHING;";
    }

    private string GetOrderByClause(TableDataInfo tableInfo)
    {
        if (tableInfo.PrimaryKeyColumns.Any())
        {
            return string.Join(", ", tableInfo.PrimaryKeyColumns.Select(c => $"\"{c}\""));
        }
        
        // Si no hay PK, ordenar por la primera columna
        return tableInfo.Columns.Any() ? $"\"{tableInfo.Columns.First()}\"" : "1";
    }

    private string FormatValue(object value, string dataType)
    {
        if (value == null || value == DBNull.Value)
        {
            return "NULL";
        }

        return dataType.ToLower() switch
        {
            var type when type.Contains("varchar") || type.Contains("text") || type.Contains("char") =>
                $"'{value.ToString()?.Replace("'", "''")}'",
            
            var type when type.Contains("timestamp") || type.Contains("date") =>
                value is DateTime dt ? $"'{dt:yyyy-MM-dd HH:mm:ss}'" : $"'{value}'",
            
            var type when type.Contains("boolean") =>
                value is bool b ? (b ? "true" : "false") : value.ToString()?.ToLower() == "true" ? "true" : "false",
            
            var type when type.Contains("uuid") =>
                $"'{value}'",
            
            var type when type.Contains("json") =>
                $"'{value.ToString()?.Replace("'", "''")}'::jsonb",
            
            var type when type.Contains("bytea") =>
                $"'\\x{Convert.ToHexString((byte[])value)}'",
            
            _ => value.ToString() ?? "NULL"
        };
    }

    private string FormatDataType(string dataType, int? maxLength)
    {
        return dataType.ToLower() switch
        {
            "character varying" => $"varchar({maxLength ?? 255})",
            "timestamp without time zone" => "timestamp",
            "timestamp with time zone" => "timestamptz",
            _ => dataType.ToLower()
        };
    }

    private async Task<bool> IsSerialColumnAsync(NpgsqlConnection connection, string tableName, string columnName)
    {
        var query = @"
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'public'
            AND table_name = @tableName
            AND column_name = @columnName";

        using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@tableName", tableName.ToLower());
        cmd.Parameters.AddWithValue("@columnName", columnName.ToLower());
        
        var defaultValue = await cmd.ExecuteScalarAsync() as string;
        return !string.IsNullOrEmpty(defaultValue) && 
               (defaultValue.Contains("nextval") || defaultValue.Contains("_seq"));
    }

    private int CountRowsInScript(string script)
    {
        // Contar líneas que contienen VALUES para estimar registros
        var lines = script.Split('\n');
        return lines.Count(line => line.TrimStart().StartsWith("(") && line.TrimEnd().EndsWith(")") || line.TrimEnd().EndsWith("),"));
    }
}