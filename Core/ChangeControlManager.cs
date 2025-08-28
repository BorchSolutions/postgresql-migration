using BorchSolutions.PostgreSQL.Migration.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BorchSolutions.PostgreSQL.Migration.Core;

public class ChangeControlInfo
{
    public string Path { get; set; } = string.Empty;
    public List<string> TrackedFiles { get; set; } = new();
    public Dictionary<string, string> FileChecksums { get; set; } = new();
    public DateTime LastScan { get; set; } = DateTime.UtcNow;
    public string Environment { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
}

public class FileChangeInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Added, Modified, Deleted, Unchanged
    public string? OldChecksum { get; set; }
    public string? NewChecksum { get; set; }
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
}

public interface IChangeControlManager
{
    Task<bool> InitializeChangeControlAsync(string migrationPath);
    Task<ChangeControlInfo> ScanDirectoryAsync(string path);
    Task<List<FileChangeInfo>> DetectChangesAsync(string path);
    Task<bool> UpdateChangeControlAsync(string path);
    Task<bool> RemovePathFromControlAsync(string path);
    Task<List<ChangeControlInfo>> GetAllTrackedPathsAsync();
    Task<bool> ValidatePathIntegrityAsync(string path);
}

public class ChangeControlManager : IChangeControlManager
{
    private readonly ILogger<ChangeControlManager> _logger;
    private readonly string _controlFileName = ".borchsolutions-migration-control";
    private readonly string _globalControlFile;

    public ChangeControlManager(ILogger<ChangeControlManager> logger)
    {
        _logger = logger;
        _globalControlFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            ".borchsolutions", 
            "migration-control.json"
        );
        
        // Crear directorio si no existe
        var globalDir = Path.GetDirectoryName(_globalControlFile);
        if (!string.IsNullOrEmpty(globalDir) && !Directory.Exists(globalDir))
        {
            Directory.CreateDirectory(globalDir);
        }
    }

    public async Task<bool> InitializeChangeControlAsync(string migrationPath)
    {
        try
        {
            _logger.LogInformation("🔧 Inicializando control de cambios para: {Path}", migrationPath);

            if (!Directory.Exists(migrationPath))
            {
                _logger.LogError("❌ El directorio no existe: {Path}", migrationPath);
                return false;
            }

            // Escanear directorio inicial
            var changeControlInfo = await ScanDirectoryAsync(migrationPath);
            
            // Guardar archivo de control local
            var controlFilePath = Path.Combine(migrationPath, _controlFileName);
            await SaveChangeControlInfoAsync(controlFilePath, changeControlInfo);
            
            // Registrar en control global
            await RegisterPathInGlobalControlAsync(migrationPath, changeControlInfo);
            
            _logger.LogInformation("✅ Control de cambios inicializado - {FileCount} archivos rastreados", changeControlInfo.TotalFiles);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error inicializando control de cambios");
            return false;
        }
    }

    public async Task<ChangeControlInfo> ScanDirectoryAsync(string path)
    {
        _logger.LogDebug("🔍 Escaneando directorio: {Path}", path);

        var changeControlInfo = new ChangeControlInfo
        {
            Path = Path.GetFullPath(path),
            LastScan = DateTime.UtcNow,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
        };

        if (!Directory.Exists(path))
        {
            _logger.LogWarning("⚠️  Directorio no existe: {Path}", path);
            return changeControlInfo;
        }

        // Buscar archivos .sql de forma recursiva
        var sqlFiles = Directory.GetFiles(path, "*.sql", SearchOption.AllDirectories);
        
        // También incluir archivos de configuración importantes
        var configPatterns = new[] { "*.json", "*.yml", "*.yaml", "*.xml" };
        var configFiles = configPatterns
            .SelectMany(pattern => Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly))
            .ToArray();

        var allFiles = sqlFiles.Concat(configFiles).ToArray();

        foreach (var file in allFiles)
        {
            try
            {
                var relativePath = Path.GetRelativePath(path, file);
                var checksum = await CalculateFileChecksumAsync(file);
                
                changeControlInfo.TrackedFiles.Add(relativePath);
                changeControlInfo.FileChecksums[relativePath] = checksum;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️  Error procesando archivo: {File}", file);
            }
        }

        changeControlInfo.TotalFiles = changeControlInfo.TrackedFiles.Count;
        
        _logger.LogDebug("✅ Escaneo completado - {FileCount} archivos encontrados", changeControlInfo.TotalFiles);
        return changeControlInfo;
    }

    public async Task<List<FileChangeInfo>> DetectChangesAsync(string path)
    {
        var changes = new List<FileChangeInfo>();

        try
        {
            _logger.LogDebug("🔍 Detectando cambios en: {Path}", path);

            // Obtener estado actual
            var currentState = await ScanDirectoryAsync(path);
            
            // Cargar estado previo
            var controlFilePath = Path.Combine(path, _controlFileName);
            var previousState = await LoadChangeControlInfoAsync(controlFilePath);
            
            if (previousState == null)
            {
                _logger.LogInformation("ℹ️  No existe control previo, todos los archivos son nuevos");
                
                // Todos los archivos son nuevos
                foreach (var file in currentState.TrackedFiles)
                {
                    var filePath = Path.Combine(path, file);
                    var fileInfo = new FileInfo(filePath);
                    
                    changes.Add(new FileChangeInfo
                    {
                        FilePath = file,
                        Status = "Added",
                        NewChecksum = currentState.FileChecksums[file],
                        LastModified = fileInfo.LastWriteTimeUtc,
                        FileSize = fileInfo.Length
                    });
                }
                
                return changes;
            }

            // Comparar estados
            var currentFiles = new HashSet<string>(currentState.TrackedFiles);
            var previousFiles = new HashSet<string>(previousState.TrackedFiles);

            // Archivos agregados
            var addedFiles = currentFiles.Except(previousFiles);
            foreach (var file in addedFiles)
            {
                var filePath = Path.Combine(path, file);
                var fileInfo = new FileInfo(filePath);
                
                changes.Add(new FileChangeInfo
                {
                    FilePath = file,
                    Status = "Added",
                    NewChecksum = currentState.FileChecksums[file],
                    LastModified = fileInfo.LastWriteTimeUtc,
                    FileSize = fileInfo.Length
                });
            }

            // Archivos eliminados
            var deletedFiles = previousFiles.Except(currentFiles);
            foreach (var file in deletedFiles)
            {
                changes.Add(new FileChangeInfo
                {
                    FilePath = file,
                    Status = "Deleted",
                    OldChecksum = previousState.FileChecksums.GetValueOrDefault(file),
                    LastModified = DateTime.UtcNow,
                    FileSize = 0
                });
            }

            // Archivos modificados
            var commonFiles = currentFiles.Intersect(previousFiles);
            foreach (var file in commonFiles)
            {
                var currentChecksum = currentState.FileChecksums[file];
                var previousChecksum = previousState.FileChecksums.GetValueOrDefault(file);

                if (currentChecksum != previousChecksum)
                {
                    var filePath = Path.Combine(path, file);
                    var fileInfo = new FileInfo(filePath);
                    
                    changes.Add(new FileChangeInfo
                    {
                        FilePath = file,
                        Status = "Modified",
                        OldChecksum = previousChecksum,
                        NewChecksum = currentChecksum,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        FileSize = fileInfo.Length
                    });
                }
                else
                {
                    // Archivo sin cambios (opcional incluir en log detallado)
                    var filePath = Path.Combine(path, file);
                    var fileInfo = new FileInfo(filePath);
                    
                    changes.Add(new FileChangeInfo
                    {
                        FilePath = file,
                        Status = "Unchanged",
                        OldChecksum = previousChecksum,
                        NewChecksum = currentChecksum,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        FileSize = fileInfo.Length
                    });
                }
            }

            _logger.LogInformation("🔍 Cambios detectados: {Added} agregados, {Modified} modificados, {Deleted} eliminados", 
                changes.Count(c => c.Status == "Added"),
                changes.Count(c => c.Status == "Modified"),
                changes.Count(c => c.Status == "Deleted"));

            return changes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error detectando cambios");
            return changes;
        }
    }

    public async Task<bool> UpdateChangeControlAsync(string path)
    {
        try
        {
            _logger.LogDebug("🔄 Actualizando control de cambios: {Path}", path);

            var changeControlInfo = await ScanDirectoryAsync(path);
            
            // Actualizar archivo local
            var controlFilePath = Path.Combine(path, _controlFileName);
            await SaveChangeControlInfoAsync(controlFilePath, changeControlInfo);
            
            // Actualizar registro global
            await RegisterPathInGlobalControlAsync(path, changeControlInfo);
            
            _logger.LogInformation("✅ Control de cambios actualizado");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error actualizando control de cambios");
            return false;
        }
    }

    public async Task<bool> RemovePathFromControlAsync(string path)
    {
        try
        {
            _logger.LogInformation("🗑️  Removiendo path del control de cambios: {Path}", path);

            var fullPath = Path.GetFullPath(path);
            
            // Eliminar archivo de control local
            var controlFilePath = Path.Combine(path, _controlFileName);
            if (File.Exists(controlFilePath))
            {
                File.Delete(controlFilePath);
                _logger.LogDebug("🗑️  Archivo de control local eliminado");
            }

            // Remover del control global
            var globalControl = await LoadGlobalControlAsync();
            if (globalControl.ContainsKey(fullPath))
            {
                globalControl.Remove(fullPath);
                await SaveGlobalControlAsync(globalControl);
                _logger.LogDebug("🗑️  Path removido del control global");
            }

            _logger.LogInformation("✅ Path removido exitosamente del control de cambios");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error removiendo path del control");
            return false;
        }
    }

    public async Task<List<ChangeControlInfo>> GetAllTrackedPathsAsync()
    {
        try
        {
            var globalControl = await LoadGlobalControlAsync();
            return globalControl.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error obteniendo paths rastreados");
            return new List<ChangeControlInfo>();
        }
    }

    public async Task<bool> ValidatePathIntegrityAsync(string path)
    {
        try
        {
            _logger.LogInformation("🔍 Validando integridad de path: {Path}", path);

            var changes = await DetectChangesAsync(path);
            var modifiedOrDeleted = changes.Where(c => c.Status == "Modified" || c.Status == "Deleted").ToList();

            if (modifiedOrDeleted.Any())
            {
                _logger.LogWarning("⚠️  Se encontraron {Count} archivos modificados o eliminados", modifiedOrDeleted.Count);
                
                foreach (var change in modifiedOrDeleted)
                {
                    _logger.LogWarning("  {Status}: {File}", change.Status, change.FilePath);
                }
                
                return false;
            }

            _logger.LogInformation("✅ Integridad validada - No se encontraron cambios");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error validando integridad");
            return false;
        }
    }

    private async Task<string> CalculateFileChecksumAsync(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToBase64String(hash);
    }

    private async Task SaveChangeControlInfoAsync(string filePath, ChangeControlInfo info)
    {
        var json = JsonConvert.SerializeObject(info, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json);
    }

    private async Task<ChangeControlInfo?> LoadChangeControlInfoAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<ChangeControlInfo>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️  Error cargando archivo de control: {FilePath}", filePath);
            return null;
        }
    }

    private async Task RegisterPathInGlobalControlAsync(string path, ChangeControlInfo info)
    {
        var globalControl = await LoadGlobalControlAsync();
        var fullPath = Path.GetFullPath(path);
        
        globalControl[fullPath] = info;
        await SaveGlobalControlAsync(globalControl);
    }

    private async Task<Dictionary<string, ChangeControlInfo>> LoadGlobalControlAsync()
    {
        if (!File.Exists(_globalControlFile))
        {
            return new Dictionary<string, ChangeControlInfo>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_globalControlFile);
            return JsonConvert.DeserializeObject<Dictionary<string, ChangeControlInfo>>(json) 
                   ?? new Dictionary<string, ChangeControlInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️  Error cargando control global, creando nuevo");
            return new Dictionary<string, ChangeControlInfo>();
        }
    }

    private async Task SaveGlobalControlAsync(Dictionary<string, ChangeControlInfo> globalControl)
    {
        var json = JsonConvert.SerializeObject(globalControl, Formatting.Indented);
        await File.WriteAllTextAsync(_globalControlFile, json);
    }
}