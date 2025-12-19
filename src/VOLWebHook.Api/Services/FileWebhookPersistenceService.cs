using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Models;

namespace VOLWebHook.Api.Services;

public sealed class FileWebhookPersistenceService : IWebhookPersistenceService
{
    private readonly WebhookSettings _settings;
    private readonly ILogger<FileWebhookPersistenceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileWebhookPersistenceService(
        IOptions<WebhookSettings> settings,
        ILogger<FileWebhookPersistenceService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        EnsureStorageDirectoryExists();
    }

    public async Task SaveAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (!_settings.EnablePayloadPersistence)
        {
            _logger.LogDebug("Payload persistence disabled, skipping save for {RequestId}", request.Id);
            return;
        }

        var dateFolder = request.ReceivedAtUtc.ToString("yyyy-MM-dd");
        var directoryPath = Path.Combine(_settings.PayloadStoragePath, dateFolder);
        var filePath = Path.Combine(directoryPath, request.FileName);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            _logger.LogDebug("Persisted webhook {RequestId} to {FilePath}", request.Id, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist webhook {RequestId} to {FilePath}", request.Id, filePath);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<WebhookRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var directories = Directory.GetDirectories(_settings.PayloadStoragePath);

        foreach (var directory in directories.OrderByDescending(d => d))
        {
            var files = Directory.GetFiles(directory, $"*_{id}.json");
            if (files.Length > 0)
            {
                var json = await File.ReadAllTextAsync(files[0], cancellationToken);
                return JsonSerializer.Deserialize<WebhookRequest>(json, _jsonOptions);
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<WebhookRequest>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        var results = new List<WebhookRequest>();

        if (!Directory.Exists(_settings.PayloadStoragePath))
            return results;

        var directories = Directory.GetDirectories(_settings.PayloadStoragePath)
            .OrderByDescending(d => d);

        foreach (var directory in directories)
        {
            var files = Directory.GetFiles(directory, "*.json")
                .OrderByDescending(f => f);

            foreach (var file in files)
            {
                if (results.Count >= count)
                    return results;

                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var request = JsonSerializer.Deserialize<WebhookRequest>(json, _jsonOptions);
                    if (request != null)
                    {
                        results.Add(request);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read webhook file {FilePath}", file);
                }
            }
        }

        return results;
    }

    public Task<int> CleanupOldEntriesAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var deletedCount = 0;

        if (!Directory.Exists(_settings.PayloadStoragePath))
            return Task.FromResult(0);

        var directories = Directory.GetDirectories(_settings.PayloadStoragePath);

        foreach (var directory in directories)
        {
            var dirName = Path.GetFileName(directory);
            if (DateTime.TryParse(dirName, out var dirDate) && dirDate < cutoffDate)
            {
                try
                {
                    var fileCount = Directory.GetFiles(directory).Length;
                    Directory.Delete(directory, recursive: true);
                    deletedCount += fileCount;
                    _logger.LogInformation("Deleted old webhook directory {Directory} with {FileCount} files", directory, fileCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old webhook directory {Directory}", directory);
                }
            }
        }

        return Task.FromResult(deletedCount);
    }

    private void EnsureStorageDirectoryExists()
    {
        if (!Directory.Exists(_settings.PayloadStoragePath))
        {
            Directory.CreateDirectory(_settings.PayloadStoragePath);
            _logger.LogInformation("Created webhook storage directory at {Path}", _settings.PayloadStoragePath);
        }
    }
}
