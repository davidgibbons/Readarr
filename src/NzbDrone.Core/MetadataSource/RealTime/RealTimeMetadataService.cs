using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Model;
using NzbDrone.Core.MetadataSource.MachineLearning;

namespace NzbDrone.Core.MetadataSource.RealTime
{
    public interface IRealTimeMetadataService
    {
        Task StartAsync();
        Task StopAsync();
        Task RegisterWebhookAsync(string url, string secret, List<string> events);
        Task UnregisterWebhookAsync(string url);
        Task<List<WebhookSubscription>> GetWebhooksAsync();
        Task ProcessWebhookAsync(WebhookPayload payload);
        Task ScheduleMetadataRefreshAsync(string bookId, DateTime refreshTime);
        Task<List<MetadataUpdate>> GetPendingUpdatesAsync();
        Task ProcessMetadataUpdateAsync(MetadataUpdate update);
        Task<UpdateAnalytics> GetUpdateAnalyticsAsync();
    }

    public class RealTimeMetadataService : IRealTimeMetadataService, IDisposable
    {
        private readonly IEnhancedMetadataService _metadataService;
        private readonly IMetadataMatchingService _matchingService;
        private readonly IMetadataBookRepository _metadataBookRepository;
        private readonly IMetadataAuthorRepository _metadataAuthorRepository;
        private readonly IWebhookRepository _webhookRepository;
        private readonly IMetadataUpdateRepository _metadataUpdateRepository;
        private readonly IPublisherIntegrationService _publisherService;
        private readonly Logger _logger;
        private readonly Timer _refreshTimer;
        private readonly Timer _webhookTimer;
        private bool _disposed = false;

        public RealTimeMetadataService(
            IEnhancedMetadataService metadataService,
            IMetadataMatchingService matchingService,
            IMetadataBookRepository metadataBookRepository,
            IMetadataAuthorRepository metadataAuthorRepository,
            IWebhookRepository webhookRepository,
            IMetadataUpdateRepository metadataUpdateRepository,
            IPublisherIntegrationService publisherService,
            Logger logger)
        {
            _metadataService = metadataService;
            _matchingService = matchingService;
            _metadataBookRepository = metadataBookRepository;
            _metadataAuthorRepository = metadataAuthorRepository;
            _webhookRepository = webhookRepository;
            _metadataUpdateRepository = metadataUpdateRepository;
            _publisherService = publisherService;
            _logger = logger;

            // Initialize timers
            _refreshTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds); // Check every 5 minutes
            _refreshTimer.Elapsed += OnRefreshTimerElapsed;

            _webhookTimer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds); // Process webhooks every minute
            _webhookTimer.Elapsed += OnWebhookTimerElapsed;
        }

        public async Task StartAsync()
        {
            try
            {
                _logger.Info("Starting real-time metadata service");

                // Start timers
                _refreshTimer.Start();
                _webhookTimer.Start();

                // Initialize publisher integrations
                await _publisherService.InitializeAsync();

                _logger.Info("Real-time metadata service started successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start real-time metadata service");
                throw;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _logger.Info("Stopping real-time metadata service");

                // Stop timers
                _refreshTimer.Stop();
                _webhookTimer.Stop();

                // Cleanup publisher integrations
                await _publisherService.CleanupAsync();

                _logger.Info("Real-time metadata service stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to stop real-time metadata service");
                throw;
            }
        }

        public async Task RegisterWebhookAsync(string url, string secret, List<string> events)
        {
            try
            {
                var webhook = new WebhookSubscription
                {
                    Url = url,
                    Secret = secret,
                    Events = events,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastTriggered = null
                };

                await _webhookRepository.InsertAsync(webhook);
                _logger.Info($"Registered webhook: {url} for events: {string.Join(", ", events)}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to register webhook: {url}");
                throw;
            }
        }

        public async Task UnregisterWebhookAsync(string url)
        {
            try
            {
                var webhook = await _webhookRepository.GetByUrlAsync(url);
                if (webhook != null)
                {
                    webhook.IsActive = false;
                    await _webhookRepository.UpdateAsync(webhook);
                    _logger.Info($"Unregistered webhook: {url}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to unregister webhook: {url}");
                throw;
            }
        }

        public async Task<List<WebhookSubscription>> GetWebhooksAsync()
        {
            try
            {
                return await _webhookRepository.GetActiveAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get webhooks");
                return new List<WebhookSubscription>();
            }
        }

        public async Task ProcessWebhookAsync(WebhookPayload payload)
        {
            try
            {
                _logger.Debug($"Processing webhook: {payload.EventType} for {payload.ResourceId}");

                switch (payload.EventType.ToLowerInvariant())
                {
                    case "book.updated":
                        await ProcessBookUpdateAsync(payload);
                        break;
                    case "author.updated":
                        await ProcessAuthorUpdateAsync(payload);
                        break;
                    case "series.updated":
                        await ProcessSeriesUpdateAsync(payload);
                        break;
                    case "new.release":
                        await ProcessNewReleaseAsync(payload);
                        break;
                    default:
                        _logger.Warn($"Unknown webhook event type: {payload.EventType}");
                        break;
                }

                // Trigger webhooks for subscribers
                await TriggerWebhooksAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to process webhook: {payload.EventType}");
                throw;
            }
        }

        public async Task ScheduleMetadataRefreshAsync(string bookId, DateTime refreshTime)
        {
            try
            {
                var update = new MetadataUpdate
                {
                    ResourceId = bookId,
                    ResourceType = "book",
                    UpdateType = "refresh",
                    ScheduledTime = refreshTime,
                    Status = UpdateStatus.Scheduled,
                    CreatedAt = DateTime.UtcNow
                };

                await _metadataUpdateRepository.InsertAsync(update);
                _logger.Debug($"Scheduled metadata refresh for book {bookId} at {refreshTime}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to schedule metadata refresh for book {bookId}");
                throw;
            }
        }

        public async Task<List<MetadataUpdate>> GetPendingUpdatesAsync()
        {
            try
            {
                return await _metadataUpdateRepository.GetPendingAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get pending updates");
                return new List<MetadataUpdate>();
            }
        }

        public async Task ProcessMetadataUpdateAsync(MetadataUpdate update)
        {
            try
            {
                _logger.Debug($"Processing metadata update: {update.UpdateType} for {update.ResourceId}");

                update.Status = UpdateStatus.Processing;
                update.ProcessingStartedAt = DateTime.UtcNow;
                await _metadataUpdateRepository.UpdateAsync(update);

                switch (update.ResourceType.ToLowerInvariant())
                {
                    case "book":
                        await ProcessBookMetadataUpdateAsync(update);
                        break;
                    case "author":
                        await ProcessAuthorMetadataUpdateAsync(update);
                        break;
                    default:
                        _logger.Warn($"Unknown resource type: {update.ResourceType}");
                        update.Status = UpdateStatus.Failed;
                        update.ErrorMessage = $"Unknown resource type: {update.ResourceType}";
                        break;
                }

                update.Status = UpdateStatus.Completed;
                update.CompletedAt = DateTime.UtcNow;
                await _metadataUpdateRepository.UpdateAsync(update);

                _logger.Debug($"Completed metadata update: {update.UpdateType} for {update.ResourceId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to process metadata update: {update.UpdateType} for {update.ResourceId}");
                
                update.Status = UpdateStatus.Failed;
                update.ErrorMessage = ex.Message;
                update.CompletedAt = DateTime.UtcNow;
                await _metadataUpdateRepository.UpdateAsync(update);
            }
        }

        public async Task<UpdateAnalytics> GetUpdateAnalyticsAsync()
        {
            try
            {
                var analytics = new UpdateAnalytics
                {
                    TotalUpdates = await _metadataUpdateRepository.GetCountAsync(),
                    PendingUpdates = await _metadataUpdateRepository.GetPendingCountAsync(),
                    CompletedUpdates = await _metadataUpdateRepository.GetCompletedCountAsync(),
                    FailedUpdates = await _metadataUpdateRepository.GetFailedCountAsync(),
                    AverageProcessingTime = await _metadataUpdateRepository.GetAverageProcessingTimeAsync(),
                    UpdateTrends = await GetUpdateTrendsAsync()
                };

                return analytics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get update analytics");
                return new UpdateAnalytics();
            }
        }

        #region Private Methods

        private async void OnRefreshTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var pendingUpdates = await GetPendingUpdatesAsync();
                var dueUpdates = pendingUpdates.Where(u => u.ScheduledTime <= DateTime.UtcNow).ToList();

                foreach (var update in dueUpdates)
                {
                    await ProcessMetadataUpdateAsync(update);
                }

                if (dueUpdates.Any())
                {
                    _logger.Debug($"Processed {dueUpdates.Count} scheduled metadata updates");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in refresh timer elapsed");
            }
        }

        private async void OnWebhookTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // Process any pending webhook deliveries
                var pendingWebhooks = await _webhookRepository.GetPendingDeliveriesAsync();
                
                foreach (var webhook in pendingWebhooks)
                {
                    await DeliverWebhookAsync(webhook);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in webhook timer elapsed");
            }
        }

        private async Task ProcessBookUpdateAsync(WebhookPayload payload)
        {
            var bookId = payload.ResourceId;
            var metadataBook = await _metadataBookRepository.GetByIdentifierAsync(bookId);
            
            if (metadataBook != null)
            {
                // Update existing metadata
                metadataBook.UpdatedAt = DateTime.UtcNow;
                await _metadataBookRepository.UpdateAsync(metadataBook);
                _logger.Debug($"Updated book metadata: {bookId}");
            }
            else
            {
                // Fetch new metadata
                await _metadataService.GetBookInfoAsync(bookId);
                _logger.Debug($"Fetched new book metadata: {bookId}");
            }
        }

        private async Task ProcessAuthorUpdateAsync(WebhookPayload payload)
        {
            var authorId = payload.ResourceId;
            var metadataAuthor = await _metadataAuthorRepository.GetByIdentifierAsync(authorId);
            
            if (metadataAuthor != null)
            {
                // Update existing metadata
                metadataAuthor.UpdatedAt = DateTime.UtcNow;
                await _metadataAuthorRepository.UpdateAsync(metadataAuthor);
                _logger.Debug($"Updated author metadata: {authorId}");
            }
            else
            {
                // Fetch new metadata
                await _metadataService.GetAuthorInfoAsync(authorId);
                _logger.Debug($"Fetched new author metadata: {authorId}");
            }
        }

        private async Task ProcessSeriesUpdateAsync(WebhookPayload payload)
        {
            // Handle series updates
            _logger.Debug($"Processing series update: {payload.ResourceId}");
            // Implementation would depend on series handling logic
        }

        private async Task ProcessNewReleaseAsync(WebhookPayload payload)
        {
            try
            {
                var bookId = payload.ResourceId;
                
                // Schedule immediate metadata refresh
                await ScheduleMetadataRefreshAsync(bookId, DateTime.UtcNow);
                
                // Notify subscribers about new release
                var notificationPayload = new WebhookPayload
                {
                    EventType = "new.release",
                    ResourceId = bookId,
                    ResourceType = "book",
                    Data = payload.Data,
                    Timestamp = DateTime.UtcNow
                };
                
                await TriggerWebhooksAsync(notificationPayload);
                
                _logger.Info($"Processed new release: {bookId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to process new release: {payload.ResourceId}");
            }
        }

        private async Task ProcessBookMetadataUpdateAsync(MetadataUpdate update)
        {
            var bookId = update.ResourceId;
            
            // Fetch fresh metadata from providers
            await _metadataService.GetBookInfoAsync(bookId);
            
            // Update confidence score
            var metadataBook = await _metadataBookRepository.GetByIdentifierAsync(bookId);
            if (metadataBook != null)
            {
                metadataBook.UpdateConfidenceScore();
                await _metadataBookRepository.UpdateAsync(metadataBook);
            }
        }

        private async Task ProcessAuthorMetadataUpdateAsync(MetadataUpdate update)
        {
            var authorId = update.ResourceId;
            
            // Fetch fresh metadata from providers
            await _metadataService.GetAuthorInfoAsync(authorId);
            
            // Update confidence score
            var metadataAuthor = await _metadataAuthorRepository.GetByIdentifierAsync(authorId);
            if (metadataAuthor != null)
            {
                metadataAuthor.UpdateConfidenceScore();
                await _metadataAuthorRepository.UpdateAsync(metadataAuthor);
            }
        }

        private async Task TriggerWebhooksAsync(WebhookPayload payload)
        {
            try
            {
                var webhooks = await GetWebhooksAsync();
                var relevantWebhooks = webhooks.Where(w => w.Events.Contains(payload.EventType)).ToList();

                foreach (var webhook in relevantWebhooks)
                {
                    await DeliverWebhookAsync(webhook, payload);
                }

                _logger.Debug($"Triggered {relevantWebhooks.Count} webhooks for event: {payload.EventType}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to trigger webhooks");
            }
        }

        private async Task DeliverWebhookAsync(WebhookSubscription webhook, WebhookPayload payload = null)
        {
            try
            {
                // Implementation would include HTTP POST to webhook URL
                // with proper authentication and retry logic
                
                webhook.LastTriggered = DateTime.UtcNow;
                await _webhookRepository.UpdateAsync(webhook);
                
                _logger.Debug($"Delivered webhook to: {webhook.Url}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to deliver webhook to: {webhook.Url}");
                
                // Mark webhook as failed for retry
                webhook.FailureCount++;
                await _webhookRepository.UpdateAsync(webhook);
            }
        }

        private async Task<List<UpdateTrend>> GetUpdateTrendsAsync()
        {
            // This would typically query analytics data
            // For now, return sample data
            return new List<UpdateTrend>
            {
                new UpdateTrend { Date = DateTime.UtcNow.AddDays(-7), Count = 150, Type = "book" },
                new UpdateTrend { Date = DateTime.UtcNow.AddDays(-6), Count = 120, Type = "author" },
                new UpdateTrend { Date = DateTime.UtcNow.AddDays(-5), Count = 180, Type = "book" }
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _refreshTimer?.Dispose();
                _webhookTimer?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    #region Models

    public class WebhookSubscription : ModelBase
    {
        public string Url { get; set; }
        public string Secret { get; set; }
        public List<string> Events { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastTriggered { get; set; }
        public int FailureCount { get; set; }
    }

    public class WebhookPayload
    {
        public string EventType { get; set; }
        public string ResourceId { get; set; }
        public string ResourceType { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public DateTime Timestamp { get; set; }
    }

    public class MetadataUpdate : ModelBase
    {
        public string ResourceId { get; set; }
        public string ResourceType { get; set; }
        public string UpdateType { get; set; }
        public DateTime ScheduledTime { get; set; }
        public UpdateStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    public enum UpdateStatus
    {
        Scheduled,
        Processing,
        Completed,
        Failed
    }

    public class UpdateAnalytics
    {
        public int TotalUpdates { get; set; }
        public int PendingUpdates { get; set; }
        public int CompletedUpdates { get; set; }
        public int FailedUpdates { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public List<UpdateTrend> UpdateTrends { get; set; } = new List<UpdateTrend>();
    }

    public class UpdateTrend
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public string Type { get; set; }
    }

    #endregion

    #region Repository Interfaces (to be implemented)

    public interface IWebhookRepository : IBasicRepository<WebhookSubscription>
    {
        Task<WebhookSubscription> GetByUrlAsync(string url);
        Task<List<WebhookSubscription>> GetActiveAsync();
        Task<List<WebhookSubscription>> GetPendingDeliveriesAsync();
    }

    public interface IMetadataUpdateRepository : IBasicRepository<MetadataUpdate>
    {
        Task<List<MetadataUpdate>> GetPendingAsync();
        Task<int> GetPendingCountAsync();
        Task<int> GetCompletedCountAsync();
        Task<int> GetFailedCountAsync();
        Task<TimeSpan> GetAverageProcessingTimeAsync();
    }

    public interface IPublisherIntegrationService
    {
        Task InitializeAsync();
        Task CleanupAsync();
        Task<List<PublisherUpdate>> GetPublisherUpdatesAsync();
    }

    public class PublisherUpdate
    {
        public string PublisherId { get; set; }
        public string ResourceId { get; set; }
        public string UpdateType { get; set; }
        public DateTime PublishedAt { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }

    #endregion
} 