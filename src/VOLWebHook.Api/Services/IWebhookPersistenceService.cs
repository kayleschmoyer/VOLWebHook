using VOLWebHook.Api.Models;

namespace VOLWebHook.Api.Services;

public interface IWebhookPersistenceService
{
    Task SaveAsync(WebhookRequest request, CancellationToken cancellationToken = default);
    Task<WebhookRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookRequest>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
    Task<int> CleanupOldEntriesAsync(int retentionDays, CancellationToken cancellationToken = default);
}
