public interface IAgentLogShippingService
{
    Task PushRecentLogsAsync(CancellationToken cancellationToken = default);
}