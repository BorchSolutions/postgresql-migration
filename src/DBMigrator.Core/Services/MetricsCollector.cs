using System.Collections.Concurrent;
using System.Diagnostics;
using DBMigrator.Core.Models;

namespace DBMigrator.Core.Services;

public class MetricsCollector : IDisposable
{
    private readonly StructuredLogger _logger;
    private readonly ConcurrentDictionary<string, PerformanceCounter> _counters;
    private readonly ConcurrentQueue<MetricEvent> _events;
    private readonly Timer? _flushTimer;
    private readonly string _instanceId;
    private bool _disposed = false;

    public MetricsCollector(StructuredLogger logger) : this(logger, true)
    {
    }

    public MetricsCollector(StructuredLogger logger, bool enableAutoFlush)
    {
        _logger = logger;
        _counters = new ConcurrentDictionary<string, PerformanceCounter>();
        _events = new ConcurrentQueue<MetricEvent>();
        _instanceId = Environment.MachineName + "_" + Environment.ProcessId;

        // Flush metrics every 30 seconds only if enabled
        if (enableAutoFlush)
        {
            _flushTimer = new Timer(FlushMetrics, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    public IDisposable StartOperation(string operationName, string? context = null)
    {
        return new OperationTimer(this, operationName, context);
    }

    public void RecordEvent(string eventName, double value = 1.0, Dictionary<string, object>? tags = null)
    {
        var metricEvent = new MetricEvent
        {
            Name = eventName,
            Value = value,
            Timestamp = DateTime.UtcNow,
            InstanceId = _instanceId,
            Tags = tags ?? new Dictionary<string, object>()
        };

        _events.Enqueue(metricEvent);
    }

    public void IncrementCounter(string counterName, double value = 1.0)
    {
        var counter = _counters.GetOrAdd(counterName, _ => new PerformanceCounter());
        counter.Increment(value);
    }

    public void SetGauge(string gaugeName, double value)
    {
        var counter = _counters.GetOrAdd(gaugeName, _ => new PerformanceCounter());
        counter.SetValue(value);
    }

    public void RecordHistogram(string histogramName, double value)
    {
        var counter = _counters.GetOrAdd(histogramName, _ => new PerformanceCounter());
        counter.RecordValue(value);
    }

    public async Task<MetricsSnapshot> GetSnapshotAsync()
    {
        var snapshot = new MetricsSnapshot
        {
            GeneratedAt = DateTime.UtcNow,
            InstanceId = _instanceId,
            Counters = new Dictionary<string, PerformanceCounterSnapshot>(),
            RecentEvents = new List<MetricEvent>()
        };

        // Capture counter snapshots
        foreach (var (name, counter) in _counters)
        {
            snapshot.Counters[name] = counter.GetSnapshot();
        }

        // Capture recent events (last 100) - thread-safe approach
        var recentEvents = new List<MetricEvent>();
        var maxEvents = Math.Min(100, _events.Count);
        for (int i = 0; i < maxEvents && _events.TryDequeue(out var evt); i++)
        {
            recentEvents.Add(evt);
        }
        snapshot.RecentEvents = recentEvents;

        await _logger.LogAsync(LogLevel.Debug, "Metrics snapshot generated", new 
        { 
            CountersCount = snapshot.Counters.Count,
            EventsCount = snapshot.RecentEvents.Count
        });

        return snapshot;
    }

    public async Task<SystemMetrics> GetSystemMetricsAsync()
    {
        var process = Process.GetCurrentProcess();
        
        var metrics = new SystemMetrics
        {
            CollectedAt = DateTime.UtcNow,
            ProcessId = Environment.ProcessId,
            MachineName = Environment.MachineName,
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            CpuTimeMs = (long)process.TotalProcessorTime.TotalMilliseconds,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            UptimeMs = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalMilliseconds
        };

        // Add GC metrics
        metrics.GcCollections = new Dictionary<int, long>();
        for (int generation = 0; generation <= GC.MaxGeneration; generation++)
        {
            metrics.GcCollections[generation] = GC.CollectionCount(generation);
        }

        metrics.TotalMemoryBytes = GC.GetTotalMemory(false);

        return metrics;
    }

    public void RecordMigrationMetrics(string migrationId, TimeSpan duration, bool success, int linesExecuted = 0)
    {
        RecordEvent("migration_executed", 1.0, new Dictionary<string, object>
        {
            ["migration_id"] = migrationId,
            ["success"] = success,
            ["duration_ms"] = duration.TotalMilliseconds,
            ["lines_executed"] = linesExecuted
        });

        RecordHistogram("migration_duration_ms", duration.TotalMilliseconds);
        IncrementCounter(success ? "migrations_success" : "migrations_failed");
    }

    public void RecordBackupMetrics(string backupId, TimeSpan duration, long sizeBytes, bool success)
    {
        RecordEvent("backup_created", 1.0, new Dictionary<string, object>
        {
            ["backup_id"] = backupId,
            ["success"] = success,
            ["duration_ms"] = duration.TotalMilliseconds,
            ["size_bytes"] = sizeBytes
        });

        RecordHistogram("backup_duration_ms", duration.TotalMilliseconds);
        RecordHistogram("backup_size_bytes", sizeBytes);
        IncrementCounter(success ? "backups_success" : "backups_failed");
    }

    private void FlushMetrics(object? state)
    {
        // Fire and forget pattern for timer callbacks - no async void
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await GetSnapshotAsync();
                var systemMetrics = await GetSystemMetricsAsync();

                await _logger.LogAsync(LogLevel.Info, "Metrics snapshot", new 
                { 
                    Snapshot = snapshot,
                    SystemMetrics = systemMetrics
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await _logger.LogAsync(LogLevel.Error, "Failed to flush metrics", new { Error = ex.Message }, ex);
                }
                catch
                {
                    // Swallow logging errors to prevent cascade failures
                }
            }
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _flushTimer?.Dispose();
            }
            _disposed = true;
        }
    }

    private class OperationTimer : IDisposable
    {
        private readonly MetricsCollector _collector;
        private readonly string _operationName;
        private readonly string? _context;
        private readonly Stopwatch _stopwatch;

        public OperationTimer(MetricsCollector collector, string operationName, string? context)
        {
            _collector = collector;
            _operationName = operationName;
            _context = context;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            
            var tags = new Dictionary<string, object>
            {
                ["duration_ms"] = _stopwatch.ElapsedMilliseconds
            };
            
            if (!string.IsNullOrEmpty(_context))
            {
                tags["context"] = _context;
            }

            _collector.RecordEvent($"operation_{_operationName}", 1.0, tags);
            _collector.RecordHistogram($"{_operationName}_duration_ms", _stopwatch.ElapsedMilliseconds);
        }
    }
}

public class PerformanceCounter
{
    private long _count;
    private double _sum;
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    private double _value;
    private readonly object _lock = new();

    public void Increment(double value = 1.0)
    {
        lock (_lock)
        {
            _count++;
            _sum += value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
        }
    }

    public void SetValue(double value)
    {
        lock (_lock)
        {
            _value = value;
        }
    }

    public void RecordValue(double value)
    {
        lock (_lock)
        {
            _count++;
            _sum += value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
        }
    }

    public PerformanceCounterSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new PerformanceCounterSnapshot
            {
                Count = _count,
                Sum = _sum,
                Min = _min == double.MaxValue ? 0 : _min,
                Max = _max == double.MinValue ? 0 : _max,
                Average = _count > 0 ? _sum / _count : 0,
                Value = _value
            };
        }
    }
}

public class MetricEvent
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public Dictionary<string, object> Tags { get; set; } = new();
}

public class MetricsSnapshot
{
    public DateTime GeneratedAt { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public Dictionary<string, PerformanceCounterSnapshot> Counters { get; set; } = new();
    public List<MetricEvent> RecentEvents { get; set; } = new();
}

public class PerformanceCounterSnapshot
{
    public long Count { get; set; }
    public double Sum { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Average { get; set; }
    public double Value { get; set; }
}

public class SystemMetrics
{
    public DateTime CollectedAt { get; set; }
    public int ProcessId { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long CpuTimeMs { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long UptimeMs { get; set; }
    public Dictionary<int, long> GcCollections { get; set; } = new();
}