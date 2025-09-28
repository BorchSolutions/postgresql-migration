using DBMigrator.Core.Services;
using DBMigrator.Core.Models;

namespace DBMigrator.CLI.Commands;

public static class RepairCommand
{
    public static async Task<int> ExecuteAsync(string connectionString, string action, string[] args)
    {
        try
        {
            var logger = new StructuredLogger("Info", true);
            
            return action.ToLower() switch
            {
                "checksums" => await RepairChecksumsAsync(connectionString, logger, args),
                "locks" => await RepairLocksAsync(connectionString, logger, args),
                "recovery" => await RecoverFromErrorAsync(connectionString, logger, args),
                _ => ShowRepairHelp()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Repair operation failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RepairChecksumsAsync(string connectionString, StructuredLogger logger, string[] args)
    {
        Console.WriteLine("🔧 Repairing migration checksums...");
        
        var checksumManager = new ChecksumManager(connectionString, logger);
        var force = args.Contains("--force");
        string? migrationId = null;

        // Parse migration ID if provided
        var migrationIndex = Array.IndexOf(args, "--migration-id");
        if (migrationIndex >= 0 && migrationIndex + 1 < args.Length)
        {
            migrationId = args[migrationIndex + 1];
        }

        try
        {
            if (!string.IsNullOrEmpty(migrationId))
            {
                // Repair specific migration
                var result = await checksumManager.RepairChecksumAsync(migrationId, force);
                
                if (result.Success)
                {
                    Console.WriteLine($"✅ Checksum repaired for migration: {migrationId}");
                    Console.WriteLine($"   Old checksum: {(result.OldChecksum?.Length >= 8 ? result.OldChecksum[..8] + "..." : result.OldChecksum ?? "N/A")}");
                    Console.WriteLine($"   New checksum: {(result.NewChecksum?.Length >= 8 ? result.NewChecksum[..8] + "..." : result.NewChecksum ?? "N/A")}");
                    Console.WriteLine($"   {result.Message}");
                }
                else
                {
                    Console.WriteLine($"❌ Failed to repair checksum: {result.Error}");
                    return 1;
                }
            }
            else
            {
                // Verify all checksums and offer to repair mismatches
                var mismatches = await checksumManager.VerifyAllChecksumsAsync("./migrations");
                
                if (!mismatches.Any())
                {
                    Console.WriteLine("✅ All checksums are valid, no repairs needed");
                    return 0;
                }

                Console.WriteLine($"⚠️ Found {mismatches.Count} checksum mismatch(es):");
                Console.WriteLine();

                foreach (var mismatch in mismatches)
                {
                    Console.WriteLine($"   📄 {mismatch.MigrationId}");
                    Console.WriteLine($"      Stored:  {(mismatch.StoredChecksum.Length >= 8 ? mismatch.StoredChecksum[..8] + "..." : mismatch.StoredChecksum)}");
                    Console.WriteLine($"      Current: {(mismatch.CurrentChecksum.Length >= 8 ? mismatch.CurrentChecksum[..8] + "..." : mismatch.CurrentChecksum)}");
                    
                    if (!string.IsNullOrEmpty(mismatch.Error))
                    {
                        Console.WriteLine($"      Error: {mismatch.Error}");
                    }
                    Console.WriteLine();
                }

                if (force)
                {
                    Console.WriteLine("🔧 Force repairing all mismatched checksums...");
                    var repaired = 0;
                    
                    foreach (var mismatch in mismatches)
                    {
                        var result = await checksumManager.RepairChecksumAsync(mismatch.MigrationId, true);
                        if (result.Success)
                        {
                            repaired++;
                            Console.WriteLine($"   ✅ Repaired: {mismatch.MigrationId}");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ Failed: {mismatch.MigrationId} - {result.Error}");
                        }
                    }
                    
                    Console.WriteLine($"🎉 Repaired {repaired} out of {mismatches.Count} checksums");
                }
                else
                {
                    Console.WriteLine("💡 Use --force to automatically repair all mismatches");
                    Console.WriteLine("   Or specify --migration-id <id> to repair a specific migration");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Checksum repair failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RepairLocksAsync(string connectionString, StructuredLogger logger, string[] args)
    {
        Console.WriteLine("🔓 Repairing migration locks...");
        
        var lockManager = new MigrationLockManager(connectionString, logger);
        var force = args.Contains("--force");

        try
        {
            var currentLock = await lockManager.GetCurrentLockAsync();
            
            if (currentLock == null)
            {
                Console.WriteLine("✅ No active locks found, nothing to repair");
                return 0;
            }

            Console.WriteLine($"🔒 Found active lock:");
            Console.WriteLine($"   Lock ID: {currentLock.LockId}");
            Console.WriteLine($"   Migration: {currentLock.MigrationId ?? "Global"}");
            Console.WriteLine($"   Acquired by: {currentLock.AcquiredBy}");
            Console.WriteLine($"   Acquired at: {currentLock.AcquiredAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   Expires at: {currentLock.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   Machine: {currentLock.MachineName}");
            Console.WriteLine($"   Process ID: {currentLock.ProcessId}");
            Console.WriteLine();

            if (currentLock.ExpiresAt <= DateTime.UtcNow)
            {
                Console.WriteLine("⏰ Lock has expired, cleaning up...");
                await lockManager.ForceReleaseAllLocksAsync($"REPAIR_EXPIRED_{Environment.UserName}");
                Console.WriteLine("✅ Expired lock cleaned up successfully");
            }
            else if (force)
            {
                Console.WriteLine("🔧 Force releasing active lock...");
                await lockManager.ForceReleaseAllLocksAsync($"REPAIR_FORCED_{Environment.UserName}");
                Console.WriteLine("✅ Lock force released successfully");
            }
            else
            {
                Console.WriteLine("⚠️ Lock is still active and not expired");
                Console.WriteLine("💡 Use --force to release the lock anyway");
                Console.WriteLine("   WARNING: This may interfere with running migrations!");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Lock repair failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RecoverFromErrorAsync(string connectionString, StructuredLogger logger, string[] args)
    {
        Console.WriteLine("🚑 Recovering from migration error...");
        
        try
        {
            // This would implement recovery from the __dbmigrator_recovery_log table
            Console.WriteLine("💡 Recovery from error functionality:");
            Console.WriteLine("   1. Check __dbmigrator_recovery_log table for failed migrations");
            Console.WriteLine("   2. Review and resolve the errors manually");
            Console.WriteLine("   3. Use 'dbmigrator repair locks --force' to clear stuck locks");
            Console.WriteLine("   4. Use 'dbmigrator repair checksums' to fix checksum mismatches");
            Console.WriteLine("   5. Resume migrations with 'dbmigrator apply'");
            Console.WriteLine();
            Console.WriteLine("🔍 To inspect recovery logs, check:");
            Console.WriteLine("   SELECT * FROM __dbmigrator_recovery_log WHERE resolved = false;");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Recovery inspection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ShowRepairHelp()
    {
        Console.WriteLine("Usage: dbmigrator repair <action> [options]");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  checksums [--migration-id <id>] [--force]     Repair checksum mismatches");
        Console.WriteLine("  locks [--force]                               Release stuck migration locks");
        Console.WriteLine("  recovery                                       Show recovery information");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --force                    Force repair without confirmation");
        Console.WriteLine("  --migration-id <id>        Target specific migration");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dbmigrator repair checksums");
        Console.WriteLine("  dbmigrator repair checksums --migration-id 20241201120000_create_users");
        Console.WriteLine("  dbmigrator repair checksums --force");
        Console.WriteLine("  dbmigrator repair locks --force");
        Console.WriteLine("  dbmigrator repair recovery");
        
        return 1;
    }
}