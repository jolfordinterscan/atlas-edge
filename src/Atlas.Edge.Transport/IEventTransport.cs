using Atlas.Edge.Core;

namespace Atlas.Edge.Transport;

public interface IEventTransport
{
    Task<TransportSendResult> SendAsync(
        IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
        CancellationToken cancellationToken);

    Task<TransportSendResult> SendInventoryAsync(
        ScannerInventoryEvent inventory,
        CancellationToken cancellationToken) =>
        Task.FromResult(TransportSendResult.NonRetryable("scanner_inventory_transport_unsupported"));
}
