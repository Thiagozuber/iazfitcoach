using System.Collections.Concurrent;
using iazfitcoach.Models;

namespace iazfitcoach.Services;

/// <summary>
/// Thread-safe MLOps telemetry recorder.
/// Every RAG interaction is appended to an in-memory queue so that the
/// /api/metrics/drift endpoint can surface questions whose retrieved
/// context was weak (similarity &lt; the configured threshold).
/// </summary>
public interface ITelemetryService
{
    void Record(InteractionMetric metric);
    IEnumerable<InteractionMetric> Snapshot();
    void Clear();
}

public class TelemetryService : ITelemetryService
{
    // Lock-free FIFO. Kept in memory only; in production this would ship to
    // Application Insights / Seq / Prometheus.
    private readonly ConcurrentQueue<InteractionMetric> _queue = new();
    private readonly int _maxCapacity = 5000;

    public void Record(InteractionMetric metric)
    {
        _queue.Enqueue(metric);

        // Bound memory growth so long-running processes never OOM.
        while (_queue.Count > _maxCapacity && _queue.TryDequeue(out _))
        {
            // discard oldest
        }
    }

    public IEnumerable<InteractionMetric> Snapshot()
    {
        return _queue.ToArray();
    }

    public void Clear()
    {
        _queue.Clear();
    }
}