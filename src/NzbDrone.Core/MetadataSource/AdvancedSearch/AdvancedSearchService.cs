using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Model;
using NzbDrone.Core.MetadataSource.MachineLearning;

namespace NzbDrone.Core.MetadataSource.AdvancedSearch
{
    public interface IAdvancedSearchService
    {
        Task<List<Book>> AdvancedBookSearchAsync(AdvancedSearchRequest request);
        Task<List<Author>> AdvancedAuthorSearchAsync(AdvancedAuthorSearchRequest request);
        Task<List<Book>> GetRecommendationsAsync(string userId, int limit = 10);
        Task<List<Book>> GetSimilarBooksAsync(string bookId, int limit = 10);
        Task<List<Author>> GetSimilarAuthorsAsync(string authorId, int limit = 10);
        Task<List<Book>> GetBooksByGenreAsync(string genre, int limit = 20);
        Task<List<Book>> GetNewReleasesAsync(int days = 30, int limit = 20);
        Task<List<Book>> GetPopularBooksAsync(int limit = 20);
        Task<SearchAnalytics> GetSearchAnalyticsAsync();
    }

    public class AdvancedSearchService : IAdvancedSearchService
    {
        private readonly IEnhancedMetadataService _metadataService;
        private readonly IMetadataMatchingService _matchingService;
        private readonly IMetadataBookRepository _metadataBookRepository;
        private readonly IMetadataAuthorRepository _metadataAuthorRepository;
        private readonly IUserPreferenceRepository _userPreferenceRepository;
        private readonly IRecommendationEngine _recommendationEngine;
        private readonly Logger _logger;

        public AdvancedSearchService(
            IEnhancedMetadataService metadataService,
            IMetadataMatchingService matchingService,
            IMetadataBookRepository metadataBookRepository,
            IMetadataAuthorRepository metadataAuthorRepository,
            IUserPreferenceRepository userPreferenceRepository,
            IRecommendationEngine recommendationEngine,
            Logger logger)
        {
            _metadataService = metadataService;
            _matchingService = matchingService;
            _metadataBookRepository = metadataBookRepository;
            _metadataAuthorRepository = metadataAuthorRepository;
            _userPreferenceRepository = userPreferenceRepository;
            _recommendationEngine = recommendationEngine;
            _logger = logger;
        }

        public async Task<List<Book>> AdvancedBookSearchAsync(AdvancedSearchRequest request)
        {
            try
            {
                _logger.Debug($"Advanced book search: {request.Title} by {request.Author}");

                var results = new List<Book>();

                // Search in database first
                var cachedResults = await SearchCachedBooksAdvancedAsync(request);
                results.AddRange(cachedResults);

                // If not enough results, search external providers
                if (results.Count < request.Limit)
                {
                    var externalResults = await SearchExternalBooksAsync(request);
                    results.AddRange(externalResults);
                }

                // Apply filters and sorting
                results = ApplyBookFilters(results, request);
                results = SortBooks(results, request.SortBy, request.SortOrder);

                // Remove duplicates and limit results
                results = results.DistinctBy(b => b.ForeignBookId).Take(request.Limit).ToList();

                _logger.Debug($"Advanced search returned {results.Count} results");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to perform advanced book search");
                return new List<Book>();
            }
        }

        public async Task<List<Author>> AdvancedAuthorSearchAsync(AdvancedAuthorSearchRequest request)
        {
            try
            {
                _logger.Debug($"Advanced author search: {request.Name}");

                var results = new List<Author>();

                // Search in database first
                var cachedResults = await SearchCachedAuthorsAdvancedAsync(request);
                results.AddRange(cachedResults);

                // If not enough results, search external providers
                if (results.Count < request.Limit)
                {
                    var externalResults = await SearchExternalAuthorsAsync(request);
                    results.AddRange(externalResults);
                }

                // Apply filters and sorting
                results = ApplyAuthorFilters(results, request);
                results = SortAuthors(results, request.SortBy, request.SortOrder);

                // Remove duplicates and limit results
                results = results.DistinctBy(a => a.ForeignAuthorId).Take(request.Limit).ToList();

                _logger.Debug($"Advanced author search returned {results.Count} results");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to perform advanced author search");
                return new List<Author>();
            }
        }

        public async Task<List<Book>> GetRecommendationsAsync(string userId, int limit = 10)
        {
            try
            {
                var userPreferences = await _userPreferenceRepository.GetByUserIdAsync(userId);
                var recommendations = await _recommendationEngine.GetRecommendationsAsync(userPreferences, limit);
                
                _logger.Debug($"Generated {recommendations.Count} recommendations for user {userId}");
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get recommendations for user {userId}");
                return new List<Book>();
            }
        }

        public async Task<List<Book>> GetSimilarBooksAsync(string bookId, int limit = 10)
        {
            try
            {
                var metadataBook = await _metadataBookRepository.GetByIdentifierAsync(bookId);
                if (metadataBook == null)
                {
                    _logger.Warn($"Book not found for similar books search: {bookId}");
                    return new List<Book>();
                }

                var allBooks = await _metadataBookRepository.GetAllAsync();
                var similarBooks = _matchingService.FindSimilarBooks(metadataBook, allBooks, 0.7);
                
                var results = similarBooks.Take(limit).Select(ConvertMetadataBookToBook).ToList();
                
                _logger.Debug($"Found {results.Count} similar books for {bookId}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get similar books for {bookId}");
                return new List<Book>();
            }
        }

        public async Task<List<Author>> GetSimilarAuthorsAsync(string authorId, int limit = 10)
        {
            try
            {
                var metadataAuthor = await _metadataAuthorRepository.GetByIdentifierAsync(authorId);
                if (metadataAuthor == null)
                {
                    _logger.Warn($"Author not found for similar authors search: {authorId}");
                    return new List<Author>();
                }

                var allAuthors = await _metadataAuthorRepository.GetAllAsync();
                var similarAuthors = _matchingService.FindSimilarAuthors(metadataAuthor, allAuthors, 0.7);
                
                var results = similarAuthors.Take(limit).Select(ConvertMetadataAuthorToAuthor).ToList();
                
                _logger.Debug($"Found {results.Count} similar authors for {authorId}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get similar authors for {authorId}");
                return new List<Author>();
            }
        }

        public async Task<List<Book>> GetBooksByGenreAsync(string genre, int limit = 20)
        {
            try
            {
                var books = await _metadataBookRepository.GetByGenreAsync(genre, limit);
                var results = books.Select(ConvertMetadataBookToBook).ToList();
                
                _logger.Debug($"Found {results.Count} books in genre {genre}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get books by genre {genre}");
                return new List<Book>();
            }
        }

        public async Task<List<Book>> GetNewReleasesAsync(int days = 30, int limit = 20)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-days);
                var books = await _metadataBookRepository.GetNewReleasesAsync(cutoffDate, limit);
                var results = books.Select(ConvertMetadataBookToBook).ToList();
                
                _logger.Debug($"Found {results.Count} new releases in the last {days} days");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get new releases for the last {days} days");
                return new List<Book>();
            }
        }

        public async Task<List<Book>> GetPopularBooksAsync(int limit = 20)
        {
            try
            {
                var books = await _metadataBookRepository.GetPopularBooksAsync(limit);
                var results = books.Select(ConvertMetadataBookToBook).ToList();
                
                _logger.Debug($"Found {results.Count} popular books");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get popular books");
                return new List<Book>();
            }
        }

        public async Task<SearchAnalytics> GetSearchAnalyticsAsync()
        {
            try
            {
                var analytics = new SearchAnalytics
                {
                    TotalBooks = await _metadataBookRepository.GetCountAsync(),
                    TotalAuthors = await _metadataAuthorRepository.GetCountAsync(),
                    AverageConfidenceScore = await _metadataBookRepository.GetAverageConfidenceScoreAsync(),
                    TopGenres = await _metadataBookRepository.GetTopGenresAsync(10),
                    SearchTrends = await GetSearchTrendsAsync()
                };

                return analytics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get search analytics");
                return new SearchAnalytics();
            }
        }

        #region Private Methods

        private async Task<List<Book>> SearchCachedBooksAdvancedAsync(AdvancedSearchRequest request)
        {
            var cachedBooks = await _metadataBookRepository.AdvancedSearchAsync(request);
            return cachedBooks.Select(ConvertMetadataBookToBook).ToList();
        }

        private async Task<List<Author>> SearchCachedAuthorsAdvancedAsync(AdvancedAuthorSearchRequest request)
        {
            var cachedAuthors = await _metadataAuthorRepository.AdvancedSearchAsync(request);
            return cachedAuthors.Select(ConvertMetadataAuthorToAuthor).ToList();
        }

        private async Task<List<Book>> SearchExternalBooksAsync(AdvancedSearchRequest request)
        {
            var results = new List<Book>();

            // Search by title and author
            if (!string.IsNullOrWhiteSpace(request.Title) && !string.IsNullOrWhiteSpace(request.Author))
            {
                var searchResults = await _metadataService.SearchForNewBookAsync(request.Title, request.Author, true);
                results.AddRange(searchResults);
            }

            // Search by ISBN if provided
            if (!string.IsNullOrWhiteSpace(request.Isbn))
            {
                var isbnResults = await _metadataService.SearchByIsbnAsync(request.Isbn);
                results.AddRange(isbnResults);
            }

            return results;
        }

        private async Task<List<Author>> SearchExternalAuthorsAsync(AdvancedAuthorSearchRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                return await _metadataService.SearchForNewAuthorAsync(request.Name);
            }

            return new List<Author>();
        }

        private List<Book> ApplyBookFilters(List<Book> books, AdvancedSearchRequest request)
        {
            var filtered = books.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(request.Genre))
            {
                filtered = filtered.Where(b => b.Metadata?.Genres?.Contains(request.Genre, StringComparer.OrdinalIgnoreCase) == true);
            }

            if (request.MinPublicationYear.HasValue)
            {
                filtered = filtered.Where(b => b.ReleaseDate?.Year >= request.MinPublicationYear.Value);
            }

            if (request.MaxPublicationYear.HasValue)
            {
                filtered = filtered.Where(b => b.ReleaseDate?.Year <= request.MaxPublicationYear.Value);
            }

            if (request.MinPageCount.HasValue)
            {
                filtered = filtered.Where(b => b.PageCount >= request.MinPageCount.Value);
            }

            if (request.MaxPageCount.HasValue)
            {
                filtered = filtered.Where(b => b.PageCount <= request.MaxPageCount.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                filtered = filtered.Where(b => b.Metadata?.Language?.Equals(request.Language, StringComparison.OrdinalIgnoreCase) == true);
            }

            return filtered.ToList();
        }

        private List<Author> ApplyAuthorFilters(List<Author> authors, AdvancedAuthorSearchRequest request)
        {
            var filtered = authors.AsEnumerable();

            if (request.MinBirthYear.HasValue)
            {
                filtered = filtered.Where(a => a.Metadata?.BirthDate?.Year >= request.MinBirthYear.Value);
            }

            if (request.MaxBirthYear.HasValue)
            {
                filtered = filtered.Where(a => a.Metadata?.BirthDate?.Year <= request.MaxBirthYear.Value);
            }

            return filtered.ToList();
        }

        private List<Book> SortBooks(List<Book> books, string sortBy, string sortOrder)
        {
            var sorted = sortBy?.ToLowerInvariant() switch
            {
                "title" => books.OrderBy(b => b.Title),
                "author" => books.OrderBy(b => b.AuthorMetadata?.Value.Name),
                "publicationdate" => books.OrderBy(b => b.ReleaseDate),
                "pagecount" => books.OrderBy(b => b.PageCount),
                "confidence" => books.OrderBy(b => b.Metadata?.ConfidenceScore),
                _ => books.OrderBy(b => b.Title)
            };

            return sortOrder?.ToLowerInvariant() == "desc" ? sorted.Reverse().ToList() : sorted.ToList();
        }

        private List<Author> SortAuthors(List<Author> authors, string sortBy, string sortOrder)
        {
            var sorted = sortBy?.ToLowerInvariant() switch
            {
                "name" => authors.OrderBy(a => a.Name),
                "birthdate" => authors.OrderBy(a => a.Metadata?.BirthDate),
                "confidence" => authors.OrderBy(a => a.Metadata?.ConfidenceScore),
                _ => authors.OrderBy(a => a.Name)
            };

            return sortOrder?.ToLowerInvariant() == "desc" ? sorted.Reverse().ToList() : sorted.ToList();
        }

        private Book ConvertMetadataBookToBook(MetadataBook metadataBook)
        {
            return new Book
            {
                ForeignBookId = metadataBook.GoodreadsId ?? metadataBook.Id.ToString(),
                Title = metadataBook.Title,
                Overview = metadataBook.Description,
                ReleaseDate = metadataBook.PublicationDate,
                PageCount = metadataBook.PageCount,
                Metadata = new BookMetadata
                {
                    ConfidenceScore = metadataBook.ConfidenceScore,
                    Language = metadataBook.Language
                }
            };
        }

        private Author ConvertMetadataAuthorToAuthor(MetadataAuthor metadataAuthor)
        {
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = metadataAuthor.GoodreadsId ?? metadataAuthor.Id.ToString(),
                Name = metadataAuthor.Name,
                Overview = metadataAuthor.Biography,
                BirthDate = metadataAuthor.BirthDate,
                DeathDate = metadataAuthor.DeathDate
            };

            return new Author
            {
                ForeignAuthorId = authorMetadata.ForeignAuthorId,
                Name = authorMetadata.Name,
                Metadata = authorMetadata
            };
        }

        private async Task<List<SearchTrend>> GetSearchTrendsAsync()
        {
            // This would typically query analytics data
            // For now, return sample data
            return new List<SearchTrend>
            {
                new SearchTrend { Term = "fantasy", Count = 150, Trend = "up" },
                new SearchTrend { Term = "mystery", Count = 120, Trend = "stable" },
                new SearchTrend { Term = "romance", Count = 200, Trend = "up" }
            };
        }

        #endregion
    }

    #region Request Models

    public class AdvancedSearchRequest
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Isbn { get; set; }
        public string Genre { get; set; }
        public int? MinPublicationYear { get; set; }
        public int? MaxPublicationYear { get; set; }
        public int? MinPageCount { get; set; }
        public int? MaxPageCount { get; set; }
        public string Language { get; set; }
        public string SortBy { get; set; } = "title";
        public string SortOrder { get; set; } = "asc";
        public int Limit { get; set; } = 20;
    }

    public class AdvancedAuthorSearchRequest
    {
        public string Name { get; set; }
        public int? MinBirthYear { get; set; }
        public int? MaxBirthYear { get; set; }
        public string SortBy { get; set; } = "name";
        public string SortOrder { get; set; } = "asc";
        public int Limit { get; set; } = 20;
    }

    public class SearchAnalytics
    {
        public int TotalBooks { get; set; }
        public int TotalAuthors { get; set; }
        public decimal AverageConfidenceScore { get; set; }
        public List<string> TopGenres { get; set; } = new List<string>();
        public List<SearchTrend> SearchTrends { get; set; } = new List<SearchTrend>();
    }

    public class SearchTrend
    {
        public string Term { get; set; }
        public int Count { get; set; }
        public string Trend { get; set; } // "up", "down", "stable"
    }

    #endregion

    #region Repository Interfaces (to be implemented)

    public interface IUserPreferenceRepository : IBasicRepository<UserPreference>
    {
        Task<UserPreference> GetByUserIdAsync(string userId);
        Task<List<string>> GetUserGenresAsync(string userId);
        Task<List<string>> GetUserAuthorsAsync(string userId);
    }

    public interface IRecommendationEngine
    {
        Task<List<Book>> GetRecommendationsAsync(UserPreference preferences, int limit);
    }

    public class UserPreference : ModelBase
    {
        public string UserId { get; set; }
        public List<string> PreferredGenres { get; set; } = new List<string>();
        public List<string> PreferredAuthors { get; set; } = new List<string>();
        public List<string> ReadBooks { get; set; } = new List<string>();
        public List<string> WishlistBooks { get; set; } = new List<string>();
        public DateTime LastUpdated { get; set; }
    }

    #endregion
} 