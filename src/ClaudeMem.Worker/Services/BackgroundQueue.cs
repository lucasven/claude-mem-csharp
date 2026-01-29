using System.Threading.Channels;

namespace ClaudeMem.Worker.Services;

public class BackgroundQueue : IBackgroundQueue
{
    private readonly Channel<ObservationWorkItem> _observationChannel = Channel.CreateUnbounded<ObservationWorkItem>();
    private readonly Channel<SummaryWorkItem> _summaryChannel = Channel.CreateUnbounded<SummaryWorkItem>();
    private int _observationQueueDepth;
    private int _summaryQueueDepth;

    public int ObservationQueueDepth => _observationQueueDepth;
    public int SummaryQueueDepth => _summaryQueueDepth;

    public void QueueObservation(ObservationWorkItem item)
    {
        _observationChannel.Writer.TryWrite(item);
        Interlocked.Increment(ref _observationQueueDepth);
    }

    public void QueueSummary(SummaryWorkItem item)
    {
        _summaryChannel.Writer.TryWrite(item);
        Interlocked.Increment(ref _summaryQueueDepth);
    }

    public async Task<ObservationWorkItem?> DequeueObservationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var item = await _observationChannel.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _observationQueueDepth);
            return item;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<SummaryWorkItem?> DequeueSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var item = await _summaryChannel.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _summaryQueueDepth);
            return item;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
