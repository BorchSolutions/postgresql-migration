using BorchSolutions.PostgreSQL.Migration.Core;
using BorchSolutions.PostgreSQL.Migration.Models;
using BorchSolutions.PostgreSQL.Migration.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace BorchSolutions.PostgreSQL.Migration;

class Program
{
    private static IServiceProvider? _serviceProvider;
    private static ILogger<Program>? _logger;

    static async Task<int> Main(string[] args)
    {
        try
        {
            // Configurar servicios
            await ConfigureServicesAsync();

            // Configurar CLI
            var rootCommand = ConfigureCommands();

            // Ejecutar comando
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error crítico: {ex.Message}");
            
            if (_logger != null)
            {
                _logger.LogCritical(ex, "Error crítico en la aplicación");
            }

            return 1;
        }
        finally
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static async Task ConfigureServicesAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        // Configuración
        services.AddSingleton<IConfiguration>(configuration);
        
        var migrationConfig = configuration.GetSection("MigrationSettings").Get<MigrationConfig>() ?? new MigrationConfig();
        services.AddSingleton(migrationConfig);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        // Servicios principales
        services.AddTransient<IConnectionManager, ConnectionManager>();
        services.AddTransient<IMigrationEngine, MigrationEngine>();
        services.AddTransient<IBaselineGenerator, BaselineGenerator>();
        services.AddTransient<ISchemaInspector, SchemaInspector>();
        services.AddTransient<IDataExtractor, DataExtractor>();
        services.AddTransient<IChangeControlManager, ChangeControlManager>();

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

        _logger.LogInformation("🚀 BorchSolutions PostgreSQL Migration Tool v1.0.0");
        _logger.LogInformation("   Herramienta profesional para migraciones de PostgreSQL");
    }

    private static RootCommand ConfigureCommands()
    {
        var rootCommand = new RootCommand("🚀 BorchSolutions PostgreSQL Migration Tool");

        // Opciones globales
        var connectionOption = new Option<string?>(
            "--connection", 
            "Nombre de la conexión a usar (por defecto: Default)"
        );

        var verboseOption = new Option<bool>(
            new[] { "--verbose", "-v" },
            "Mostrar información detallada"
        );

        var dryRunOption = new Option<bool>(
            new[] { "--dry-run", "-d" },
            "Ejecutar en modo de prueba (sin cambios reales)"
        );

        rootCommand.AddGlobalOption(connectionOption);
        rootCommand.AddGlobalOption(verboseOption);
        rootCommand.AddGlobalOption(dryRunOption);

        // Comando: init
        var initCommand = new Command("init", "Inicializar motor de migraciones");
        initCommand.SetHandler(async (string? connection, bool verbose) =>
        {
            await HandleInitCommandAsync(connection, verbose);
        }, connectionOption, verboseOption);

        // Comando: baseline
        var baselineCommand = new Command("baseline", "Comandos de baseline");
        
        var baselineGenerateCommand = new Command("generate", "Generar baseline desde BD existente");
        var outputPathOption = new Option<string?>("--output", "Ruta de salida para el archivo baseline");
        baselineGenerateCommand.AddOption(outputPathOption);
        baselineGenerateCommand.SetHandler(async (string? connection, string? output, bool verbose) =>
        {
            await HandleBaselineGenerateAsync(connection, output, verbose);
        }, connectionOption, outputPathOption, verboseOption);

        var baselineMarkCommand = new Command("mark", "Marcar BD actual como baseline ejecutado");
        baselineMarkCommand.SetHandler(async (string? connection, bool verbose) =>
        {
            await HandleBaselineMarkAsync(connection, verbose);
        }, connectionOption, verboseOption);

        baselineCommand.AddCommand(baselineGenerateCommand);
        baselineCommand.AddCommand(baselineMarkCommand);

        // Comando: migrate
        var migrateCommand = new Command("migrate", "Ejecutar migraciones");
        migrateCommand.SetHandler(async (string? connection, bool dryRun, bool verbose) =>
        {
            await HandleMigrateAsync(connection, dryRun, verbose);
        }, connectionOption, dryRunOption, verboseOption);

        // Comando: status
        var statusCommand = new Command("status", "Mostrar estado de migraciones");
        statusCommand.SetHandler(async (string? connection, bool verbose) =>
        {
            await HandleStatusAsync(connection, verbose);
        }, connectionOption, verboseOption);

        // Comando: data
        var dataCommand = new Command("data", "Comandos de datos");
        
        var dataExtractCommand = new Command("extract", "Extraer datos de tablas");
        var tablesOption = new Option<string[]>("--tables", "Lista de tablas a extraer (separadas por coma)") { IsRequired = true };
        var dataOutputOption = new Option<string?>("--output", "Archivo de salida para el script de datos");
        var markAsExecutedOption = new Option<bool>("--mark-as-executed", "Marcar el script extraído como ya ejecutado en la BD") { IsRequired = false };
        dataExtractCommand.AddOption(tablesOption);
        dataExtractCommand.AddOption(dataOutputOption);
        dataExtractCommand.AddOption(markAsExecutedOption);
        dataExtractCommand.SetHandler(async (string? connection, string[] tables, string? output, bool markAsExecuted, bool verbose) =>
        {
            await HandleDataExtractAsync(connection, tables, output, markAsExecuted, verbose);
        }, connectionOption, tablesOption, dataOutputOption, markAsExecutedOption, verboseOption);

        var dataTestCommand = new Command("test", "Verificar existencia y contenido de tablas");
        var testTablesOption = new Option<string[]>("--tables", "Lista de tablas a verificar (separadas por coma)") { IsRequired = true };
        dataTestCommand.AddOption(testTablesOption);
        dataTestCommand.SetHandler(async (string? connection, string[] tables, bool verbose) =>
        {
            await HandleDataTestAsync(connection, tables, verbose);
        }, connectionOption, testTablesOption, verboseOption);

        dataCommand.AddCommand(dataExtractCommand);
        dataCommand.AddCommand(dataTestCommand);

        // Comando: control
        var controlCommand = new Command("control", "Comandos de control de cambios");
        
        var controlInitCommand = new Command("init", "Inicializar control de cambios en directorio");
        var pathOption = new Option<string>("--path", "Ruta del directorio de migraciones") { IsRequired = true };
        controlInitCommand.AddOption(pathOption);
        controlInitCommand.SetHandler(async (string path, bool verbose) =>
        {
            await HandleControlInitAsync(path, verbose);
        }, pathOption, verboseOption);

        var controlScanCommand = new Command("scan", "Escanear cambios en directorio controlado");
        controlScanCommand.AddOption(pathOption);
        controlScanCommand.SetHandler(async (string path, bool verbose) =>
        {
            await HandleControlScanAsync(path, verbose);
        }, pathOption, verboseOption);

        var controlRemoveCommand = new Command("remove", "Remover directorio del control de cambios");
        controlRemoveCommand.AddOption(pathOption);
        controlRemoveCommand.SetHandler(async (string path, bool verbose) =>
        {
            await HandleControlRemoveAsync(path, verbose);
        }, pathOption, verboseOption);

        var controlListCommand = new Command("list", "Listar todos los directorios bajo control");
        controlListCommand.SetHandler(async (bool verbose) =>
        {
            await HandleControlListAsync(verbose);
        }, verboseOption);

        controlCommand.AddCommand(controlInitCommand);
        controlCommand.AddCommand(controlScanCommand);
        controlCommand.AddCommand(controlRemoveCommand);
        controlCommand.AddCommand(controlListCommand);

        // Comando: info
        var infoCommand = new Command("info", "Información de conexiones y base de datos");
        infoCommand.SetHandler(async (string? connection, bool verbose) =>
        {
            await HandleInfoAsync(connection, verbose);
        }, connectionOption, verboseOption);

        // Comando: validate
        var validateCommand = new Command("validate", "Validar integridad de migraciones");
        validateCommand.SetHandler(async (string? connection, bool verbose) =>
        {
            await HandleValidateAsync(connection, verbose);
        }, connectionOption, verboseOption);

        // Agregar todos los comandos
        rootCommand.AddCommand(initCommand);
        rootCommand.AddCommand(baselineCommand);
        rootCommand.AddCommand(migrateCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(dataCommand);
        rootCommand.AddCommand(controlCommand);
        rootCommand.AddCommand(infoCommand);
        rootCommand.AddCommand(validateCommand);

        return rootCommand;
    }

    #region Command Handlers

    private static async Task HandleInitCommandAsync(string? connection, bool verbose)
    {
        var migrationEngine = GetService<IMigrationEngine>();
        
        Console.WriteLine($"🔧 Inicializando motor de migraciones para: {connection ?? "Default"}");
        
        var success = await migrationEngine.InitializeAsync(connection);
        
        if (success)
        {
            Console.WriteLine("✅ Motor de migraciones inicializado exitosamente");
        }
        else
        {
            Console.WriteLine("❌ Error inicializando motor de migraciones");
        }
    }

    private static async Task HandleBaselineGenerateAsync(string? connection, string? output, bool verbose)
    {
        var baselineGenerator = GetService<IBaselineGenerator>();
        
        Console.WriteLine($"🔄 Generando baseline para conexión: {connection ?? "Default"}");
        
        var success = await baselineGenerator.GenerateBaselineAsync(connection, output, markAsExecuted: false);
        
        if (success)
        {
            Console.WriteLine("✅ Baseline generado exitosamente");
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine($"📁 Archivo guardado en: {output}");
            }
        }
        else
        {
            Console.WriteLine("❌ Error generando baseline");
        }
    }

    private static async Task HandleBaselineMarkAsync(string? connection, bool verbose)
    {
        var baselineGenerator = GetService<IBaselineGenerator>();
        
        Console.WriteLine($"🏷️  Marcando BD como baseline para conexión: {connection ?? "Default"}");
        
        var success = await baselineGenerator.GenerateBaselineAsync(connection, null, markAsExecuted: true);
        
        if (success)
        {
            Console.WriteLine("✅ BD marcada como baseline exitosamente");
        }
        else
        {
            Console.WriteLine("❌ Error marcando BD como baseline");
        }
    }

    private static async Task HandleMigrateAsync(string? connection, bool dryRun, bool verbose)
    {
        var migrationEngine = GetService<IMigrationEngine>();
        
        if (dryRun)
        {
            Console.WriteLine($"🔍 [DRY RUN] Verificando migraciones pendientes para: {connection ?? "Default"}");
        }
        else
        {
            Console.WriteLine($"🚀 Ejecutando migraciones para: {connection ?? "Default"}");
        }
        
        var pendingMigrations = await migrationEngine.GetPendingMigrationsAsync(connection);
        
        if (!pendingMigrations.Any())
        {
            Console.WriteLine("✅ No hay migraciones pendientes");
            return;
        }

        Console.WriteLine($"📋 Se encontraron {pendingMigrations.Count} migraciones pendientes:");
        foreach (var migration in pendingMigrations)
        {
            Console.WriteLine($"  • {migration.Version} - {migration.Description}");
        }
        
        if (!dryRun)
        {
            Console.WriteLine();
            Console.Write("¿Continuar con la ejecución? (y/N): ");
            var response = Console.ReadLine()?.ToLower();
            
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("❌ Operación cancelada por el usuario");
                return;
            }
        }
        
        var success = await migrationEngine.ExecuteMigrationsAsync(pendingMigrations, connection, dryRun);
        
        if (success)
        {
            if (dryRun)
            {
                Console.WriteLine("✅ [DRY RUN] Todas las migraciones se validaron correctamente");
            }
            else
            {
                Console.WriteLine("✅ Todas las migraciones se ejecutaron exitosamente");
            }
        }
        else
        {
            Console.WriteLine("❌ Error ejecutando migraciones");
        }
    }

    private static async Task HandleStatusAsync(string? connection, bool verbose)
    {
        var migrationEngine = GetService<IMigrationEngine>();
        
        Console.WriteLine($"📊 Estado de migraciones para: {connection ?? "Default"}");
        Console.WriteLine();
        
        var summary = await migrationEngine.GetMigrationSummaryAsync(connection);
        
        Console.WriteLine($"Total de scripts: {summary.TotalScripts}");
        Console.WriteLine($"Completados: {summary.CompletedScripts}");
        Console.WriteLine($"Fallidos: {summary.FailedScripts}");
        Console.WriteLine($"Pendientes: {summary.PendingScripts}");
        Console.WriteLine($"Tasa de éxito: {summary.SuccessRate:F1}%");
        Console.WriteLine($"Tiempo total de ejecución: {summary.TotalExecutionTime}ms");
        Console.WriteLine($"Filas afectadas: {summary.TotalAffectedRows:N0}");
        
        if (summary.LastExecution.HasValue)
        {
            Console.WriteLine($"Última ejecución: {summary.LastExecution:yyyy-MM-dd HH:mm:ss} UTC");
        }

        if (verbose && summary.Scripts.Any())
        {
            Console.WriteLine();
            Console.WriteLine("📋 Detalle de scripts:");
            
            foreach (var script in summary.Scripts.OrderBy(s => s.Version))
            {
                var status = script.Status switch
                {
                    MigrationStatus.Completed => "✅",
                    MigrationStatus.Failed => "❌",
                    MigrationStatus.Pending => "⏳",
                    _ => "❓"
                };
                
                Console.WriteLine($"  {status} {script.Version} - {script.Description}");
            }
        }
    }

    private static async Task HandleDataExtractAsync(string? connection, string[] tables, string? output, bool markAsExecuted, bool verbose)
    {
        var dataExtractor = GetService<IDataExtractor>();
        var migrationEngine = GetService<IMigrationEngine>();
        
        var tableList = tables.SelectMany(t => t.Split(',')).Select(t => t.Trim()).ToList();
        
        Console.WriteLine($"🗃️  Extrayendo datos de {tableList.Count} tablas para conexión: {connection ?? "Default"}");
        Console.WriteLine($"📋 Tablas: {string.Join(", ", tableList)}");
        
        var script = await dataExtractor.GenerateDataScriptAsync(tableList, connection);
        
        if (string.IsNullOrEmpty(script))
        {
            Console.WriteLine("❌ No se pudieron extraer los datos");
            return;
        }
        
        // Crear objeto MigrationScript para registro si se requiere
        MigrationScript? migrationScript = null;
        if (markAsExecuted)
        {
            var fileName = Path.GetFileNameWithoutExtension(output ?? "D000_001__Existing_Data");
            var version = ExtractVersionFromFileName(fileName) ?? "D000_001";
            var description = ExtractDescriptionFromFileName(fileName) ?? "Existing_Data";
            
            migrationScript = new MigrationScript
            {
                Version = version,
                Name = fileName,
                Description = description,
                Content = script,
                Type = MigrationScriptType.Data,
                Checksum = CalculateChecksum(script),
                CreatedAt = DateTime.UtcNow,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
            };
        }
        
        if (!string.IsNullOrEmpty(output))
        {
            var directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(output, script);
            Console.WriteLine($"💾 Datos exportados a: {output}");
            
            if (migrationScript != null)
            {
                migrationScript.FilePath = output;
            }
        }
        else
        {
            Console.WriteLine("📄 Script generado:");
            Console.WriteLine(script);
        }
        
        // Marcar como ejecutado si se solicita
        if (markAsExecuted && migrationScript != null)
        {
            var markResult = await migrationEngine.MarkMigrationAsExecutedAsync(migrationScript, connection);
            if (markResult)
            {
                Console.WriteLine("✅ Script de datos marcado como ejecutado en la base de datos");
            }
            else
            {
                Console.WriteLine("⚠️  Error marcando script de datos como ejecutado");
            }
        }
        
        Console.WriteLine("✅ Extracción de datos completada");
    }

    private static async Task HandleDataTestAsync(string? connection, string[] tables, bool verbose)
    {
        var connectionManager = GetService<IConnectionManager>();
        var tableList = tables.SelectMany(t => t.Split(',')).Select(t => t.Trim()).ToList();
        
        Console.WriteLine($"🧪 Verificando {tableList.Count} tablas para conexión: {connection ?? "Default"}");
        
        using var dbConnection = await connectionManager.GetConnectionAsync(connection);
        
        foreach (var table in tableList)
        {
            try
            {
                // Verificar existencia
                var existsQuery = @"
                    SELECT table_name
                    FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = $1";
                
                using var existsCmd = new Npgsql.NpgsqlCommand(existsQuery.Replace("$1", $"'{table.ToLower()}'"), dbConnection);
                var exists = await existsCmd.ExecuteScalarAsync();
                
                if (exists != null)
                {
                    // Contar registros
                    using var countCmd = new Npgsql.NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", dbConnection);
                    var count = await countCmd.ExecuteScalarAsync();
                    var rowCount = count != null ? Convert.ToInt32(count) : 0;
                    
                    Console.WriteLine($"  ✅ {table}: {rowCount:N0} registros");
                }
                else
                {
                    Console.WriteLine($"  ❌ {table}: No encontrada");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ {table}: Error - {ex.Message}");
            }
        }
        
        Console.WriteLine("✅ Verificación completada");
    }

    private static async Task HandleControlInitAsync(string path, bool verbose)
    {
        var changeControlManager = GetService<IChangeControlManager>();
        
        Console.WriteLine($"🔧 Inicializando control de cambios en: {path}");
        
        var success = await changeControlManager.InitializeChangeControlAsync(path);
        
        if (success)
        {
            Console.WriteLine("✅ Control de cambios inicializado exitosamente");
        }
        else
        {
            Console.WriteLine("❌ Error inicializando control de cambios");
        }
    }

    private static async Task HandleControlScanAsync(string path, bool verbose)
    {
        var changeControlManager = GetService<IChangeControlManager>();
        
        Console.WriteLine($"🔍 Escaneando cambios en: {path}");
        
        var changes = await changeControlManager.DetectChangesAsync(path);
        
        var added = changes.Where(c => c.Status == "Added").ToList();
        var modified = changes.Where(c => c.Status == "Modified").ToList();
        var deleted = changes.Where(c => c.Status == "Deleted").ToList();
        var unchanged = changes.Where(c => c.Status == "Unchanged").ToList();
        
        Console.WriteLine();
        Console.WriteLine($"📊 Resumen de cambios:");
        Console.WriteLine($"  ➕ Agregados: {added.Count}");
        Console.WriteLine($"  📝 Modificados: {modified.Count}");
        Console.WriteLine($"  🗑️  Eliminados: {deleted.Count}");
        Console.WriteLine($"  ✅ Sin cambios: {unchanged.Count}");
        
        if (verbose)
        {
            if (added.Any())
            {
                Console.WriteLine();
                Console.WriteLine("➕ Archivos agregados:");
                foreach (var change in added)
                {
                    Console.WriteLine($"  • {change.FilePath}");
                }
            }
            
            if (modified.Any())
            {
                Console.WriteLine();
                Console.WriteLine("📝 Archivos modificados:");
                foreach (var change in modified)
                {
                    Console.WriteLine($"  • {change.FilePath} (modificado: {change.LastModified:yyyy-MM-dd HH:mm:ss})");
                }
            }
            
            if (deleted.Any())
            {
                Console.WriteLine();
                Console.WriteLine("🗑️  Archivos eliminados:");
                foreach (var change in deleted)
                {
                    Console.WriteLine($"  • {change.FilePath}");
                }
            }
        }
        
        // Actualizar control si hay cambios
        if (added.Any() || modified.Any() || deleted.Any())
        {
            Console.WriteLine();
            Console.Write("¿Actualizar control de cambios con estos cambios? (y/N): ");
            var response = Console.ReadLine()?.ToLower();
            
            if (response == "y" || response == "yes")
            {
                var updateSuccess = await changeControlManager.UpdateChangeControlAsync(path);
                if (updateSuccess)
                {
                    Console.WriteLine("✅ Control de cambios actualizado");
                }
                else
                {
                    Console.WriteLine("❌ Error actualizando control de cambios");
                }
            }
        }
    }

    private static async Task HandleControlRemoveAsync(string path, bool verbose)
    {
        var changeControlManager = GetService<IChangeControlManager>();
        
        Console.WriteLine($"🗑️  Removiendo control de cambios de: {path}");
        Console.Write("¿Está seguro? Esta acción no se puede deshacer (y/N): ");
        
        var response = Console.ReadLine()?.ToLower();
        if (response != "y" && response != "yes")
        {
            Console.WriteLine("❌ Operación cancelada");
            return;
        }
        
        var success = await changeControlManager.RemovePathFromControlAsync(path);
        
        if (success)
        {
            Console.WriteLine("✅ Control de cambios removido exitosamente");
        }
        else
        {
            Console.WriteLine("❌ Error removiendo control de cambios");
        }
    }

    private static async Task HandleControlListAsync(bool verbose)
    {
        var changeControlManager = GetService<IChangeControlManager>();
        
        Console.WriteLine("📋 Directorios bajo control de cambios:");
        
        var trackedPaths = await changeControlManager.GetAllTrackedPathsAsync();
        
        if (!trackedPaths.Any())
        {
            Console.WriteLine("  (No hay directorios bajo control)");
            return;
        }
        
        foreach (var pathInfo in trackedPaths)
        {
            Console.WriteLine();
            Console.WriteLine($"📁 {pathInfo.Path}");
            Console.WriteLine($"   Archivos: {pathInfo.TotalFiles}");
            Console.WriteLine($"   Último escaneo: {pathInfo.LastScan:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   Ambiente: {pathInfo.Environment}");
            
            if (verbose)
            {
                // Verificar integridad
                var isValid = await changeControlManager.ValidatePathIntegrityAsync(pathInfo.Path);
                Console.WriteLine($"   Estado: {(isValid ? "✅ Íntegro" : "⚠️  Con cambios")}");
            }
        }
    }

    private static async Task HandleInfoAsync(string? connection, bool verbose)
    {
        var connectionManager = GetService<IConnectionManager>();
        
        Console.WriteLine("🔗 Información de conexiones:");
        Console.WriteLine();
        
        var connections = connectionManager.GetAvailableConnections();
        
        foreach (var conn in connections)
        {
            var indicator = conn.IsDefault ? "🌟" : "📡";
            Console.WriteLine($"{indicator} {conn.Name} ({conn.Environment})");
            
            var canConnect = await connectionManager.TestConnectionAsync(conn.Name == "Default" ? null : conn.Name);
            Console.WriteLine($"   Estado: {(canConnect ? "✅ Conectado" : "❌ Error de conexión")}");
            
            if (canConnect && verbose)
            {
                try
                {
                    var dbInfo = await connectionManager.GetDatabaseInfoAsync(conn.Name == "Default" ? null : conn.Name);
                    Console.WriteLine($"   Base de datos: {dbInfo.DatabaseName}");
                    Console.WriteLine($"   Versión PostgreSQL: {dbInfo.ServerVersion}");
                    Console.WriteLine($"   Tablas: {dbInfo.TableCount}");
                    Console.WriteLine($"   Funciones: {dbInfo.FunctionCount}");
                    Console.WriteLine($"   Índices: {dbInfo.IndexCount}");
                    Console.WriteLine($"   Triggers: {dbInfo.TriggerCount}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Error obteniendo información: {ex.Message}");
                }
            }
            
            Console.WriteLine();
        }
    }

    private static async Task HandleValidateAsync(string? connection, bool verbose)
    {
        var migrationEngine = GetService<IMigrationEngine>();
        
        Console.WriteLine($"🔍 Validando integridad de migraciones para: {connection ?? "Default"}");
        
        var isValid = await migrationEngine.ValidateMigrationIntegrityAsync(connection);
        
        if (isValid)
        {
            Console.WriteLine("✅ Validación completada - Sin problemas de integridad");
        }
        else
        {
            Console.WriteLine("⚠️  Validación completada - Se encontraron problemas de integridad");
        }
    }

    #endregion

    #region Helper Methods

    private static string? ExtractVersionFromFileName(string fileName)
    {
        var versionMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"^([VD]\d{3}_\d{3})__(.+)$");
        return versionMatch.Success ? versionMatch.Groups[1].Value : null;
    }

    private static string? ExtractDescriptionFromFileName(string fileName)
    {
        var versionMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"^([VD]\d{3}_\d{3})__(.+)$");
        return versionMatch.Success ? versionMatch.Groups[2].Value.Replace('_', ' ') : null;
    }

    private static string CalculateChecksum(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    #endregion

    private static T GetService<T>() where T : notnull
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Servicios no configurados");
        }
        
        return _serviceProvider.GetRequiredService<T>();
    }
}