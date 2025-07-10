using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Model;
using NzbDrone.Core.MetadataSource.MachineLearning;

namespace NzbDrone.Core.MetadataSource.Analytics
{
    public interface IAdvancedAnalyticsService
    {
        Task<MetadataQualityMetrics> GetMetadataQualityMetricsAsync();
        Task<UsageAnalytics> GetUsageAnalyticsAsync(DateTime startDate, DateTime endDate);
        Task<List<PerformanceMetric>> GetPerformanceMetricsAsync();
        Task<ABTestResult> GetABTestResultAsync(string testId);
        Task<List<ABTestResult>> GetAllABTestResultsAsync();
        Task<ABTestResult> StartABTestAsync(ABTestConfiguration config);
        Task<ABTestResult> StopABTestAsync(string testId);
        Task TrackUserActionAsync(UserAction action);
        Task<List<UserAction>> GetUserActionsAsync(string userId, DateTime startDate, DateTime endDate);
        Task<SystemHealthMetrics> GetSystemHealthMetricsAsync();
        Task<List<ErrorMetric>> GetErrorMetricsAsync();
        Task<RecommendationAnalytics> GetRecommendationAnalyticsAsync();
    }

    public class AdvancedAnalyticsService : IAdvancedAnalyticsService
    {
        private readonly IEnhancedMetadataService _metadataService;
        private readonly IMetadataMatchingService _matchingService;
        private readonly IMetadataBookRepository _metadataBookRepository;
        private readonly IMetadataAuthorRepository _metadataAuthorRepository;
        private readonly IAnalyticsRepository _analyticsRepository;
        private readonly IABTestRepository _abTestRepository;
        private readonly IUserActionRepository _userActionRepository;
        private readonly IPerformanceRepository _performanceRepository;
        private readonly Logger _logger;

        public AdvancedAnalyticsService(
            IEnhancedMetadataService metadataService,
            IMetadataMatchingService matchingService,
            IMetadataBookRepository metadataBookRepository,
            IMetadataAuthorRepository metadataAuthorRepository,
            IAnalyticsRepository analyticsRepository,
            IABTestRepository abTestRepository,
            IUserActionRepository userActionRepository,
            IPerformanceRepository performanceRepository,
            Logger logger)
        {
            _metadataService = metadataService;
            _matchingService = matchingService;
            _metadataBookRepository = metadataBookRepository;
            _metadataAuthorRepository = metadataAuthorRepository;
            _analyticsRepository = analyticsRepository;
            _abTestRepository = abTestRepository;
            _userActionRepository = userActionRepository;
            _performanceRepository = performanceRepository;
            _logger = logger;
        }

        public async Task<MetadataQualityMetrics> GetMetadataQualityMetricsAsync()
        {
            try
            {
                var metrics = new MetadataQualityMetrics
                {
                    TotalBooks = await _metadataBookRepository.GetCountAsync(),
                    TotalAuthors = await _metadataAuthorRepository.GetCountAsync(),
                    AverageConfidenceScore = await _metadataBookRepository.GetAverageConfidenceScoreAsync(),
                    HighConfidenceBooks = await _metadataBookRepository.GetHighConfidenceCountAsync(0.8m),
                    LowConfidenceBooks = await _metadataBookRepository.GetLowConfidenceCountAsync(0.5m),
                    CompleteMetadataBooks = await _metadataBookRepository.GetCompleteMetadataCountAsync(),
                    IncompleteMetadataBooks = await _metadataBookRepository.GetIncompleteMetadataCountAsync(),
                    QualityTrends = await GetQualityTrendsAsync(),
                    SourceDistribution = await GetSourceDistributionAsync(),
                    GenreDistribution = await GetGenreDistributionAsync()
                };

                _logger.Debug($"Generated metadata quality metrics: {metrics.TotalBooks} books, {metrics.TotalAuthors} authors");
                return metrics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get metadata quality metrics");
                return new MetadataQualityMetrics();
            }
        }

        public async Task<UsageAnalytics> GetUsageAnalyticsAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var analytics = new UsageAnalytics
                {
                    Period = new DateRange { StartDate = startDate, EndDate = endDate },
                    TotalSearches = await _userActionRepository.GetSearchCountAsync(startDate, endDate),
                    TotalBookViews = await _userActionRepository.GetBookViewCountAsync(startDate, endDate),
                    TotalAuthorViews = await _userActionRepository.GetAuthorViewCountAsync(startDate, endDate),
                    UniqueUsers = await _userActionRepository.GetUniqueUserCountAsync(startDate, endDate),
                    AverageSessionDuration = await _userActionRepository.GetAverageSessionDurationAsync(startDate, endDate),
                    SearchTrends = await GetSearchTrendsAsync(startDate, endDate),
                    PopularBooks = await GetPopularBooksAsync(startDate, endDate),
                    PopularAuthors = await GetPopularAuthorsAsync(startDate, endDate),
                    UserRetention = await GetUserRetentionAsync(startDate, endDate)
                };

                _logger.Debug($"Generated usage analytics for period {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
                return analytics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get usage analytics");
                return new UsageAnalytics();
            }
        }

        public async Task<List<PerformanceMetric>> GetPerformanceMetricsAsync()
        {
            try
            {
                var metrics = new List<PerformanceMetric>();

                // Database performance
                var dbMetrics = await _performanceRepository.GetDatabaseMetricsAsync();
                metrics.AddRange(dbMetrics);

                // API performance
                var apiMetrics = await _performanceRepository.GetAPIMetricsAsync();
                metrics.AddRange(apiMetrics);

                // Cache performance
                var cacheMetrics = await _performanceRepository.GetCacheMetricsAsync();
                metrics.AddRange(cacheMetrics);

                // External API performance
                var externalMetrics = await _performanceRepository.GetExternalAPIMetricsAsync();
                metrics.AddRange(externalMetrics);

                _logger.Debug($"Generated {metrics.Count} performance metrics");
                return metrics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get performance metrics");
                return new List<PerformanceMetric>();
            }
        }

        public async Task<ABTestResult> GetABTestResultAsync(string testId)
        {
            try
            {
                var result = await _abTestRepository.GetByIdAsync(testId);
                if (result != null)
                {
                    // Calculate statistical significance
                    result.StatisticalSignificance = CalculateStatisticalSignificance(result);
                    result.Recommendation = GenerateRecommendation(result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get A/B test result for {testId}");
                return null;
            }
        }

        public async Task<List<ABTestResult>> GetAllABTestResultsAsync()
        {
            try
            {
                var results = await _abTestRepository.GetAllAsync();
                
                foreach (var result in results)
                {
                    result.StatisticalSignificance = CalculateStatisticalSignificance(result);
                    result.Recommendation = GenerateRecommendation(result);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get all A/B test results");
                return new List<ABTestResult>();
            }
        }

        public async Task<ABTestResult> StartABTestAsync(ABTestConfiguration config)
        {
            try
            {
                _logger.Info($"Starting A/B test: {config.Name}");

                var test = new ABTestResult
                {
                    TestId = Guid.NewGuid().ToString(),
                    Name = config.Name,
                    Description = config.Description,
                    VariantA = config.VariantA,
                    VariantB = config.VariantB,
                    StartDate = DateTime.UtcNow,
                    Status = ABTestStatus.Running,
                    TargetMetric = config.TargetMetric,
                    SampleSize = config.SampleSize,
                    ConfidenceLevel = config.ConfidenceLevel
                };

                test = await _abTestRepository.InsertAsync(test);

                _logger.Info($"A/B test {test.TestId} started successfully");
                return test;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start A/B test: {config.Name}");
                throw;
            }
        }

        public async Task<ABTestResult> StopABTestAsync(string testId)
        {
            try
            {
                _logger.Info($"Stopping A/B test: {testId}");

                var test = await _abTestRepository.GetByIdAsync(testId);
                if (test == null)
                {
                    throw new ArgumentException($"A/B test {testId} not found");
                }

                test.Status = ABTestStatus.Completed;
                test.EndDate = DateTime.UtcNow;
                test.StatisticalSignificance = CalculateStatisticalSignificance(test);
                test.Recommendation = GenerateRecommendation(test);

                test = await _abTestRepository.UpdateAsync(test);

                _logger.Info($"A/B test {testId} stopped successfully");
                return test;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to stop A/B test: {testId}");
                throw;
            }
        }

        public async Task TrackUserActionAsync(UserAction action)
        {
            try
            {
                action.Timestamp = DateTime.UtcNow;
                await _userActionRepository.InsertAsync(action);

                // Update A/B test metrics if applicable
                await UpdateABTestMetricsAsync(action);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to track user action: {action.ActionType}");
            }
        }

        public async Task<List<UserAction>> GetUserActionsAsync(string userId, DateTime startDate, DateTime endDate)
        {
            try
            {
                return await _userActionRepository.GetByUserAndPeriodAsync(userId, startDate, endDate);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get user actions for {userId}");
                return new List<UserAction>();
            }
        }

        public async Task<SystemHealthMetrics> GetSystemHealthMetricsAsync()
        {
            try
            {
                var metrics = new SystemHealthMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    DatabaseHealth = await GetDatabaseHealthAsync(),
                    APICHealth = await GetAPIHealthAsync(),
                    CacheHealth = await GetCacheHealthAsync(),
                    ExternalAPIHealth = await GetExternalAPIHealthAsync(),
                    ErrorRate = await GetErrorRateAsync(),
                    ResponseTime = await GetAverageResponseTimeAsync(),
                    Uptime = await GetUptimeAsync()
                };

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get system health metrics");
                return new SystemHealthMetrics();
            }
        }

        public async Task<List<ErrorMetric>> GetErrorMetricsAsync()
        {
            try
            {
                return await _analyticsRepository.GetErrorMetricsAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get error metrics");
                return new List<ErrorMetric>();
            }
        }

        public async Task<RecommendationAnalytics> GetRecommendationAnalyticsAsync()
        {
            try
            {
                var analytics = new RecommendationAnalytics
                {
                    TotalRecommendations = await _analyticsRepository.GetRecommendationCountAsync(),
                    ClickThroughRate = await _analyticsRepository.GetClickThroughRateAsync(),
                    ConversionRate = await _analyticsRepository.GetConversionRateAsync(),
                    AverageRating = await _analyticsRepository.GetAverageRatingAsync(),
                    PopularRecommendations = await GetPopularRecommendationsAsync(),
                    RecommendationAccuracy = await GetRecommendationAccuracyAsync()
                };

                return analytics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get recommendation analytics");
                return new RecommendationAnalytics();
            }
        }

        #region Private Methods

        private async Task<List<QualityTrend>> GetQualityTrendsAsync()
        {
            // This would typically query historical data
            // For now, return sample data
            return new List<QualityTrend>
            {
                new QualityTrend { Date = DateTime.UtcNow.AddDays(-30), AverageConfidence = 0.75m, CompleteMetadata = 0.60m },
                new QualityTrend { Date = DateTime.UtcNow.AddDays(-20), AverageConfidence = 0.78m, CompleteMetadata = 0.65m },
                new QualityTrend { Date = DateTime.UtcNow.AddDays(-10), AverageConfidence = 0.82m, CompleteMetadata = 0.70m },
                new QualityTrend { Date = DateTime.UtcNow, AverageConfidence = 0.85m, CompleteMetadata = 0.75m }
            };
        }

        private async Task<List<SourceDistribution>> GetSourceDistributionAsync()
        {
            // This would typically query source data
            // For now, return sample data
            return new List<SourceDistribution>
            {
                new SourceDistribution { Source = "rreading-glasses", Count = 5000, Percentage = 0.60m },
                new SourceDistribution { Source = "goodreads-scraper", Count = 2500, Percentage = 0.30m },
                new SourceDistribution { Source = "open-library", Count = 500, Percentage = 0.06m },
                new SourceDistribution { Source = "google-books", Count = 250, Percentage = 0.04m }
            };
        }

        private async Task<List<GenreDistribution>> GetGenreDistributionAsync()
        {
            // This would typically query genre data
            // For now, return sample data
            return new List<GenreDistribution>
            {
                new GenreDistribution { Genre = "Fiction", Count = 3000, Percentage = 0.36m },
                new GenreDistribution { Genre = "Non-Fiction", Count = 2500, Percentage = 0.30m },
                new GenreDistribution { Genre = "Mystery", Count = 1000, Percentage = 0.12m },
                new GenreDistribution { Genre = "Romance", Count = 800, Percentage = 0.10m },
                new GenreDistribution { Genre = "Science Fiction", Count = 600, Percentage = 0.07m },
                new GenreDistribution { Genre = "Other", Count = 500, Percentage = 0.05m }
            };
        }

        private async Task<List<SearchTrend>> GetSearchTrendsAsync(DateTime startDate, DateTime endDate)
        {
            // This would typically query search data
            // For now, return sample data
            return new List<SearchTrend>
            {
                new SearchTrend { Date = startDate.AddDays(1), Count = 150, Term = "fantasy" },
                new SearchTrend { Date = startDate.AddDays(2), Count = 180, Term = "mystery" },
                new SearchTrend { Date = startDate.AddDays(3), Count = 120, Term = "romance" }
            };
        }

        private async Task<List<PopularBook>> GetPopularBooksAsync(DateTime startDate, DateTime endDate)
        {
            // This would typically query view data
            // For now, return sample data
            return new List<PopularBook>
            {
                new PopularBook { BookId = "1", Title = "The Great Gatsby", Views = 500, Rating = 4.2m },
                new PopularBook { BookId = "2", Title = "1984", Views = 450, Rating = 4.5m },
                new PopularBook { BookId = "3", Title = "Pride and Prejudice", Views = 400, Rating = 4.3m }
            };
        }

        private async Task<List<PopularAuthor>> GetPopularAuthorsAsync(DateTime startDate, DateTime endDate)
        {
            // This would typically query view data
            // For now, return sample data
            return new List<PopularAuthor>
            {
                new PopularAuthor { AuthorId = "1", Name = "Jane Austen", Views = 800, Rating = 4.4m },
                new PopularAuthor { AuthorId = "2", Name = "George Orwell", Views = 750, Rating = 4.6m },
                new PopularAuthor { AuthorId = "3", Name = "F. Scott Fitzgerald", Views = 600, Rating = 4.3m }
            };
        }

        private async Task<UserRetention> GetUserRetentionAsync(DateTime startDate, DateTime endDate)
        {
            // This would typically calculate retention metrics
            // For now, return sample data
            return new UserRetention
            {
                Day1Retention = 0.65m,
                Day7Retention = 0.45m,
                Day30Retention = 0.25m,
                MonthlyActiveUsers = 5000,
                WeeklyActiveUsers = 8000,
                DailyActiveUsers = 1200
            };
        }

        private double CalculateStatisticalSignificance(ABTestResult test)
        {
            // Simplified statistical significance calculation
            // In practice, this would use proper statistical tests (t-test, chi-square, etc.)
            var variantASuccess = test.VariantAMetrics.SuccessRate;
            var variantBSuccess = test.VariantBMetrics.SuccessRate;
            var sampleSize = test.SampleSize;

            if (sampleSize < 30) return 0.0; // Need larger sample size

            // Simplified z-test
            var pooledSuccess = (variantASuccess + variantBSuccess) / 2.0;
            var standardError = Math.Sqrt(pooledSuccess * (1 - pooledSuccess) * (2.0 / sampleSize));
            var zScore = Math.Abs(variantBSuccess - variantASuccess) / standardError;

            // Convert z-score to p-value (simplified)
            return zScore > 1.96 ? 0.95 : zScore > 1.645 ? 0.90 : 0.0;
        }

        private string GenerateRecommendation(ABTestResult test)
        {
            if (test.StatisticalSignificance < 0.90)
                return "Insufficient data - continue test";

            var variantA = test.VariantAMetrics.SuccessRate;
            var variantB = test.VariantBMetrics.SuccessRate;

            if (variantB > variantA * 1.05) // 5% improvement threshold
                return $"Recommend Variant B ({(variantB - variantA) / variantA * 100:F1}% improvement)";
            else if (variantA > variantB * 1.05)
                return $"Recommend Variant A ({(variantA - variantB) / variantB * 100:F1}% improvement)";
            else
                return "No significant difference - recommend current variant";
        }

        private async Task UpdateABTestMetricsAsync(UserAction action)
        {
            // Update A/B test metrics based on user actions
            // This would typically update conversion rates, engagement metrics, etc.
        }

        private async Task<HealthStatus> GetDatabaseHealthAsync()
        {
            try
            {
                // Check database connectivity and performance
                var responseTime = await _performanceRepository.GetDatabaseResponseTimeAsync();
                return responseTime < TimeSpan.FromSeconds(1) ? HealthStatus.Healthy : HealthStatus.Degraded;
            }
            catch
            {
                return HealthStatus.Unhealthy;
            }
        }

        private async Task<HealthStatus> GetAPIHealthAsync()
        {
            try
            {
                // Check API response times and error rates
                var errorRate = await _performanceRepository.GetAPIErrorRateAsync();
                return errorRate < 0.05 ? HealthStatus.Healthy : HealthStatus.Degraded;
            }
            catch
            {
                return HealthStatus.Unhealthy;
            }
        }

        private async Task<HealthStatus> GetCacheHealthAsync()
        {
            try
            {
                // Check cache hit rates and performance
                var hitRate = await _performanceRepository.GetCacheHitRateAsync();
                return hitRate > 0.8 ? HealthStatus.Healthy : HealthStatus.Degraded;
            }
            catch
            {
                return HealthStatus.Unhealthy;
            }
        }

        private async Task<HealthStatus> GetExternalAPIHealthAsync()
        {
            try
            {
                // Check external API availability and response times
                var responseTime = await _performanceRepository.GetExternalAPIResponseTimeAsync();
                return responseTime < TimeSpan.FromSeconds(5) ? HealthStatus.Healthy : HealthStatus.Degraded;
            }
            catch
            {
                return HealthStatus.Unhealthy;
            }
        }

        private async Task<decimal> GetErrorRateAsync()
        {
            try
            {
                return await _performanceRepository.GetErrorRateAsync();
            }
            catch
            {
                return 0.0m;
            }
        }

        private async Task<TimeSpan> GetAverageResponseTimeAsync()
        {
            try
            {
                return await _performanceRepository.GetAverageResponseTimeAsync();
            }
            catch
            {
                return TimeSpan.FromSeconds(0);
            }
        }

        private async Task<decimal> GetUptimeAsync()
        {
            try
            {
                return await _performanceRepository.GetUptimeAsync();
            }
            catch
            {
                return 0.0m;
            }
        }

        private async Task<List<PopularRecommendation>> GetPopularRecommendationsAsync()
        {
            // This would typically query recommendation data
            // For now, return sample data
            return new List<PopularRecommendation>
            {
                new PopularRecommendation { BookId = "1", Title = "Recommended Book 1", Clicks = 150, CTR = 0.15m },
                new PopularRecommendation { BookId = "2", Title = "Recommended Book 2", Clicks = 120, CTR = 0.12m },
                new PopularRecommendation { BookId = "3", Title = "Recommended Book 3", Clicks = 100, CTR = 0.10m }
            };
        }

        private async Task<decimal> GetRecommendationAccuracyAsync()
        {
            try
            {
                return await _analyticsRepository.GetRecommendationAccuracyAsync();
            }
            catch
            {
                return 0.0m;
            }
        }

        #endregion
    }

    #region Models

    public class MetadataQualityMetrics
    {
        public int TotalBooks { get; set; }
        public int TotalAuthors { get; set; }
        public decimal AverageConfidenceScore { get; set; }
        public int HighConfidenceBooks { get; set; }
        public int LowConfidenceBooks { get; set; }
        public int CompleteMetadataBooks { get; set; }
        public int IncompleteMetadataBooks { get; set; }
        public List<QualityTrend> QualityTrends { get; set; } = new List<QualityTrend>();
        public List<SourceDistribution> SourceDistribution { get; set; } = new List<SourceDistribution>();
        public List<GenreDistribution> GenreDistribution { get; set; } = new List<GenreDistribution>();
    }

    public class QualityTrend
    {
        public DateTime Date { get; set; }
        public decimal AverageConfidence { get; set; }
        public decimal CompleteMetadata { get; set; }
    }

    public class SourceDistribution
    {
        public string Source { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class GenreDistribution
    {
        public string Genre { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class UsageAnalytics
    {
        public DateRange Period { get; set; }
        public int TotalSearches { get; set; }
        public int TotalBookViews { get; set; }
        public int TotalAuthorViews { get; set; }
        public int UniqueUsers { get; set; }
        public TimeSpan AverageSessionDuration { get; set; }
        public List<SearchTrend> SearchTrends { get; set; } = new List<SearchTrend>();
        public List<PopularBook> PopularBooks { get; set; } = new List<PopularBook>();
        public List<PopularAuthor> PopularAuthors { get; set; } = new List<PopularAuthor>();
        public UserRetention UserRetention { get; set; }
    }

    public class DateRange
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class SearchTrend
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public string Term { get; set; }
    }

    public class PopularBook
    {
        public string BookId { get; set; }
        public string Title { get; set; }
        public int Views { get; set; }
        public decimal Rating { get; set; }
    }

    public class PopularAuthor
    {
        public string AuthorId { get; set; }
        public string Name { get; set; }
        public int Views { get; set; }
        public decimal Rating { get; set; }
    }

    public class UserRetention
    {
        public decimal Day1Retention { get; set; }
        public decimal Day7Retention { get; set; }
        public decimal Day30Retention { get; set; }
        public int MonthlyActiveUsers { get; set; }
        public int WeeklyActiveUsers { get; set; }
        public int DailyActiveUsers { get; set; }
    }

    public class PerformanceMetric
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Value { get; set; }
        public string Unit { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } // "Good", "Warning", "Critical"
    }

    public class ABTestConfiguration
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string VariantA { get; set; }
        public string VariantB { get; set; }
        public string TargetMetric { get; set; }
        public int SampleSize { get; set; }
        public double ConfidenceLevel { get; set; }
    }

    public class ABTestResult : ModelBase
    {
        public string TestId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string VariantA { get; set; }
        public string VariantB { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public ABTestStatus Status { get; set; }
        public string TargetMetric { get; set; }
        public int SampleSize { get; set; }
        public double ConfidenceLevel { get; set; }
        public VariantMetrics VariantAMetrics { get; set; } = new VariantMetrics();
        public VariantMetrics VariantBMetrics { get; set; } = new VariantMetrics();
        public double StatisticalSignificance { get; set; }
        public string Recommendation { get; set; }
    }

    public class VariantMetrics
    {
        public int Impressions { get; set; }
        public int Conversions { get; set; }
        public decimal SuccessRate { get; set; }
        public decimal AverageValue { get; set; }
        public TimeSpan AverageTime { get; set; }
    }

    public enum ABTestStatus
    {
        Running,
        Completed,
        Paused,
        Cancelled
    }

    public class UserAction : ModelBase
    {
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string ActionType { get; set; }
        public string ResourceId { get; set; }
        public string ResourceType { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public DateTime Timestamp { get; set; }
        public string ABTestId { get; set; }
        public string Variant { get; set; }
    }

    public class SystemHealthMetrics
    {
        public DateTime Timestamp { get; set; }
        public HealthStatus DatabaseHealth { get; set; }
        public HealthStatus APICHealth { get; set; }
        public HealthStatus CacheHealth { get; set; }
        public HealthStatus ExternalAPIHealth { get; set; }
        public decimal ErrorRate { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public decimal Uptime { get; set; }
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    public class ErrorMetric
    {
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public int Count { get; set; }
        public DateTime LastOccurrence { get; set; }
        public string Severity { get; set; }
    }

    public class RecommendationAnalytics
    {
        public int TotalRecommendations { get; set; }
        public decimal ClickThroughRate { get; set; }
        public decimal ConversionRate { get; set; }
        public decimal AverageRating { get; set; }
        public List<PopularRecommendation> PopularRecommendations { get; set; } = new List<PopularRecommendation>();
        public decimal RecommendationAccuracy { get; set; }
    }

    public class PopularRecommendation
    {
        public string BookId { get; set; }
        public string Title { get; set; }
        public int Clicks { get; set; }
        public decimal CTR { get; set; }
    }

    #endregion

    #region Repository Interfaces (to be implemented)

    public interface IAnalyticsRepository : IBasicRepository<object>
    {
        Task<List<ErrorMetric>> GetErrorMetricsAsync();
        Task<int> GetRecommendationCountAsync();
        Task<decimal> GetClickThroughRateAsync();
        Task<decimal> GetConversionRateAsync();
        Task<decimal> GetAverageRatingAsync();
        Task<decimal> GetRecommendationAccuracyAsync();
    }

    public interface IABTestRepository : IBasicRepository<ABTestResult>
    {
        Task<ABTestResult> GetByIdAsync(string testId);
    }

    public interface IUserActionRepository : IBasicRepository<UserAction>
    {
        Task<int> GetSearchCountAsync(DateTime startDate, DateTime endDate);
        Task<int> GetBookViewCountAsync(DateTime startDate, DateTime endDate);
        Task<int> GetAuthorViewCountAsync(DateTime startDate, DateTime endDate);
        Task<int> GetUniqueUserCountAsync(DateTime startDate, DateTime endDate);
        Task<TimeSpan> GetAverageSessionDurationAsync(DateTime startDate, DateTime endDate);
        Task<List<UserAction>> GetByUserAndPeriodAsync(string userId, DateTime startDate, DateTime endDate);
    }

    public interface IPerformanceRepository
    {
        Task<List<PerformanceMetric>> GetDatabaseMetricsAsync();
        Task<List<PerformanceMetric>> GetAPIMetricsAsync();
        Task<List<PerformanceMetric>> GetCacheMetricsAsync();
        Task<List<PerformanceMetric>> GetExternalAPIMetricsAsync();
        Task<TimeSpan> GetDatabaseResponseTimeAsync();
        Task<decimal> GetAPIErrorRateAsync();
        Task<decimal> GetCacheHitRateAsync();
        Task<TimeSpan> GetExternalAPIResponseTimeAsync();
        Task<decimal> GetErrorRateAsync();
        Task<TimeSpan> GetAverageResponseTimeAsync();
        Task<decimal> GetUptimeAsync();
    }

    #endregion
} 