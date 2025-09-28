using Npgsql;
using System.Diagnostics;
using System.IO.Compression;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class BackupManager
{
    private readonly DatabaseConfiguration _config;
    private readonly StructuredLogger _logger;
    private readonly string _backupPath;

    public BackupManager(DatabaseConfiguration config, StructuredLogger logger)
    {
        _config = config;
        _logger = logger;
        _backupPath = Path.GetFullPath(_config.Backup.BackupPath);
        
        // Ensure backup directory exists
        Directory.CreateDirectory(_backupPath);
    }

    public async Task<BackupResult> CreateBackupAsync(string migrationId, BackupType backupType = BackupType.Schema)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupId = $"{migrationId}_{timestamp}";
        
        await _logger.LogAsync(LogLevel.Info, "Starting backup creation", new 
        { 
            BackupId = backupId,
            MigrationId = migrationId,
            BackupType = backupType.ToString()
        });

        var startTime = DateTime.UtcNow;
        var backupResult = new BackupResult
        {
            BackupId = backupId,
            MigrationId = migrationId,
            BackupType = backupType,
            StartedAt = startTime,
            Status = BackupStatus.InProgress
        };

        try
        {
            string backupFileName;
            long backupSize;

            switch (backupType)
            {
                case BackupType.Schema:
                    (backupFileName, backupSize) = await CreateSchemaBackupAsync(backupId);
                    break;
                case BackupType.Data:
                    (backupFileName, backupSize) = await CreateDataBackupAsync(backupId);
                    break;
                case BackupType.Full:
                    (backupFileName, backupSize) = await CreateFullBackupAsync(backupId);
                    break;
                default:
                    throw new ArgumentException($"Unsupported backup type: {backupType}");
            }

            backupResult.CompletedAt = DateTime.UtcNow;
            backupResult.Duration = backupResult.CompletedAt.Value - startTime;
            backupResult.BackupFilePath = backupFileName;
            backupResult.BackupSize = backupSize;
            backupResult.Status = BackupStatus.Completed;

            // Compress if configured
            if (_config.Backup.CompressBackups)
            {
                var compressedPath = await CompressBackupAsync(backupFileName);
                backupResult.BackupFilePath = compressedPath;
                backupResult.BackupSize = new FileInfo(compressedPath).Length;
                
                // Remove uncompressed file
                File.Delete(backupFileName);
            }

            // Store backup metadata
            await StoreBackupMetadataAsync(backupResult);

            await _logger.LogAsync(LogLevel.Info, "Backup created successfully", new 
            { 
                BackupId = backupId,
                BackupSize = FormatBytes(backupResult.BackupSize),
                Duration = backupResult.Duration?.TotalSeconds,
                FilePath = backupResult.BackupFilePath
            });

            return backupResult;
        }
        catch (Exception ex)
        {
            backupResult.Status = BackupStatus.Failed;
            backupResult.ErrorMessage = ex.Message;
            backupResult.CompletedAt = DateTime.UtcNow;
            backupResult.Duration = backupResult.CompletedAt.Value - startTime;

            await _logger.LogAsync(LogLevel.Error, "Backup creation failed", new 
            { 
                BackupId = backupId,
                Error = ex.Message
            }, ex);

            await StoreBackupMetadataAsync(backupResult);
            throw;
        }
    }

    private async Task<(string fileName, long size)> CreateSchemaBackupAsync(string backupId)
    {
        var fileName = Path.Combine(_backupPath, $"{backupId}_schema.sql");
        var connInfo = ParseConnectionString(_config.ConnectionString);
        
        var pgDumpArgs = new List<string>
        {
            "--schema-only",
            "--no-owner",
            "--no-privileges",
            "--clean",
            "--if-exists",
            $"--host={connInfo.Host}",
            $"--port={connInfo.Port}",
            $"--username={connInfo.Username}",
            $"--dbname={connInfo.Database}",
            $"--file={fileName}"
        };

        await ExecutePgDumpAsync(pgDumpArgs, connInfo.Password);
        
        var fileInfo = new FileInfo(fileName);
        return (fileName, fileInfo.Length);
    }

    private async Task<(string fileName, long size)> CreateDataBackupAsync(string backupId)
    {
        var fileName = Path.Combine(_backupPath, $"{backupId}_data.sql");
        var connInfo = ParseConnectionString(_config.ConnectionString);
        
        var pgDumpArgs = new List<string>
        {
            "--data-only",
            "--no-owner",
            "--no-privileges",
            "--disable-triggers",
            $"--host={connInfo.Host}",
            $"--port={connInfo.Port}",
            $"--username={connInfo.Username}",
            $"--dbname={connInfo.Database}",
            $"--file={fileName}"
        };

        await ExecutePgDumpAsync(pgDumpArgs, connInfo.Password);
        
        var fileInfo = new FileInfo(fileName);
        return (fileName, fileInfo.Length);
    }

    private async Task<(string fileName, long size)> CreateFullBackupAsync(string backupId)
    {
        var fileName = Path.Combine(_backupPath, $"{backupId}_full.sql");
        var connInfo = ParseConnectionString(_config.ConnectionString);
        
        var pgDumpArgs = new List<string>
        {
            "--no-owner",
            "--no-privileges",
            "--clean",
            "--if-exists",
            $"--host={connInfo.Host}",
            $"--port={connInfo.Port}",
            $"--username={connInfo.Username}",
            $"--dbname={connInfo.Database}",
            $"--file={fileName}"
        };

        await ExecutePgDumpAsync(pgDumpArgs, connInfo.Password);
        
        var fileInfo = new FileInfo(fileName);
        return (fileName, fileInfo.Length);
    }

    private async Task ExecutePgDumpAsync(List<string> args, string? password)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "pg_dump",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        // Use ArgumentList for safe argument passing to prevent command injection
        foreach (var arg in args)
        {
            processInfo.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrEmpty(password))
        {
            processInfo.EnvironmentVariables["PGPASSWORD"] = password;
        }

        await _logger.LogAsync(LogLevel.Debug, "Executing pg_dump", new 
        { 
            Command = processInfo.FileName,
            Arguments = string.Join(" ", processInfo.ArgumentList.Select(arg => 
                arg.Contains(password ?? "", StringComparison.OrdinalIgnoreCase) ? "***" : arg))
        });

        using var process = new Process { StartInfo = processInfo };
        
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();
        
        process.OutputDataReceived += (sender, e) => 
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (sender, e) => 
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            throw new BackupException($"pg_dump failed with exit code {process.ExitCode}: {error}");
        }
    }

    private async Task<string> CompressBackupAsync(string backupPath)
    {
        var compressedPath = backupPath + ".gz";
        
        await _logger.LogAsync(LogLevel.Debug, "Compressing backup", new { OriginalPath = backupPath });
        
        using var originalFileStream = File.OpenRead(backupPath);
        using var compressedFileStream = File.Create(compressedPath);
        using var gzipStream = new GZipStream(compressedFileStream, CompressionMode.Compress);
        
        await originalFileStream.CopyToAsync(gzipStream);
        
        await _logger.LogAsync(LogLevel.Debug, "Backup compressed", new 
        { 
            CompressedPath = compressedPath,
            OriginalSize = FormatBytes(new FileInfo(backupPath).Length),
            CompressedSize = FormatBytes(new FileInfo(compressedPath).Length)
        });
        
        return compressedPath;
    }

    private async Task StoreBackupMetadataAsync(BackupResult backupResult)
    {
        try
        {
            using var connection = new NpgsqlConnection(_config.ConnectionString);
            await connection.OpenAsync();
            
            await CreateBackupTableIfNotExistsAsync(connection);
            
            var sql = @"
                INSERT INTO __dbmigrator_backups 
                (backup_id, migration_id, backup_type, status, started_at, completed_at, 
                 duration_ms, backup_file_path, backup_size, error_message)
                VALUES (@backupId, @migrationId, @backupType, @status, @startedAt, @completedAt,
                        @durationMs, @backupFilePath, @backupSize, @errorMessage)";

            using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("backupId", backupResult.BackupId);
            command.Parameters.AddWithValue("migrationId", backupResult.MigrationId);
            command.Parameters.AddWithValue("backupType", backupResult.BackupType.ToString());
            command.Parameters.AddWithValue("status", backupResult.Status.ToString());
            command.Parameters.AddWithValue("startedAt", backupResult.StartedAt);
            command.Parameters.AddWithValue("completedAt", backupResult.CompletedAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("durationMs", backupResult.Duration?.TotalMilliseconds ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("backupFilePath", backupResult.BackupFilePath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("backupSize", backupResult.BackupSize);
            command.Parameters.AddWithValue("errorMessage", backupResult.ErrorMessage ?? (object)DBNull.Value);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(LogLevel.Warning, "Failed to store backup metadata", new 
            { 
                BackupId = backupResult.BackupId,
                Error = ex.Message
            });
        }
    }

    private async Task CreateBackupTableIfNotExistsAsync(NpgsqlConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS __dbmigrator_backups (
                id SERIAL PRIMARY KEY,
                backup_id VARCHAR(255) UNIQUE NOT NULL,
                migration_id VARCHAR(255),
                backup_type VARCHAR(50) NOT NULL,
                status VARCHAR(50) NOT NULL,
                started_at TIMESTAMP NOT NULL,
                completed_at TIMESTAMP,
                duration_ms BIGINT,
                backup_file_path VARCHAR(1000),
                backup_size BIGINT DEFAULT 0,
                error_message TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_dbmigrator_backups_migration 
            ON __dbmigrator_backups(migration_id);

            CREATE INDEX IF NOT EXISTS idx_dbmigrator_backups_status 
            ON __dbmigrator_backups(status);";

        using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task CleanupOldBackupsAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_config.Backup.RetentionDays);
        
        await _logger.LogAsync(LogLevel.Info, "Starting backup cleanup", new 
        { 
            RetentionDays = _config.Backup.RetentionDays,
            CutoffDate = cutoffDate
        });

        using var connection = new NpgsqlConnection(_config.ConnectionString);
        await connection.OpenAsync();
        
        await CreateBackupTableIfNotExistsAsync(connection);
        
        // Get old backups
        var getOldBackupsSql = @"
            SELECT backup_id, backup_file_path 
            FROM __dbmigrator_backups 
            WHERE started_at < @cutoffDate AND status = 'Completed'";
        
        var oldBackups = new List<(string backupId, string? filePath)>();
        
        using (var command = new NpgsqlCommand(getOldBackupsSql, connection))
        {
            command.Parameters.AddWithValue("cutoffDate", cutoffDate);
            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                oldBackups.Add((
                    reader.GetString(reader.GetOrdinal("backup_id")),
                    reader.IsDBNull(reader.GetOrdinal("backup_file_path")) ? null : reader.GetString(reader.GetOrdinal("backup_file_path"))
                ));
            }
        }
        
        var deletedFiles = 0;
        var deletedRecords = 0;
        
        foreach (var (backupId, filePath) in oldBackups)
        {
            // Delete physical file
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    deletedFiles++;
                }
                catch (Exception ex)
                {
                    await _logger.LogAsync(LogLevel.Warning, "Failed to delete backup file", new 
                    { 
                        BackupId = backupId,
                        FilePath = filePath,
                        Error = ex.Message
                    });
                }
            }
            
            // Delete record
            var deleteRecordSql = "DELETE FROM __dbmigrator_backups WHERE backup_id = @backupId";
            using var deleteCommand = new NpgsqlCommand(deleteRecordSql, connection);
            deleteCommand.Parameters.AddWithValue("backupId", backupId);
            
            if (await deleteCommand.ExecuteNonQueryAsync() > 0)
            {
                deletedRecords++;
            }
        }
        
        await _logger.LogAsync(LogLevel.Info, "Backup cleanup completed", new 
        { 
            DeletedFiles = deletedFiles,
            DeletedRecords = deletedRecords,
            TotalOldBackups = oldBackups.Count
        });
    }

    private ConnectionInfo ParseConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new ConnectionInfo
        {
            Host = builder.Host ?? "localhost",
            Port = builder.Port == 0 ? 5432 : builder.Port,
            Database = builder.Database ?? "",
            Username = builder.Username ?? "",
            Password = builder.Password
        };
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

    private record ConnectionInfo
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
        public string Database { get; init; } = "";
        public string Username { get; init; } = "";
        public string? Password { get; init; }
    }
}

public class BackupResult
{
    public string BackupId { get; set; } = string.Empty;
    public string MigrationId { get; set; } = string.Empty;
    public BackupType BackupType { get; set; }
    public BackupStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? BackupFilePath { get; set; }
    public long BackupSize { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum BackupType
{
    Schema,
    Data,
    Full
}

public enum BackupStatus
{
    InProgress,
    Completed,
    Failed
}

public class BackupException : Exception
{
    public BackupException(string message) : base(message) { }
    public BackupException(string message, Exception innerException) : base(message, innerException) { }
}