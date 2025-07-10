using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataAggregator
    {
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId);
        Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true);
        Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true);
        Task<List<Author>> SearchForNewAuthorAsync(string title);
        Task<List<Book>> SearchByIsbnAsync(string isbn);
        Task<List<Book>> SearchByAsinAsync(string asin);
    }

    public class MetadataAggregator : IMetadataAggregator
    {
        private readonly IEnumerable<IMetadataProvider> _providers;
        private readonly Logger _logger;

        public MetadataAggregator(IEnumerable<IMetadataProvider> providers, Logger logger)
        {
            _providers = providers.OrderBy(p => p.Priority).ToList();
            _logger = logger;
        }

        public async Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled && p.CanHandle(foreignBookId)).ToList();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Attempting to get book info from {provider.Name} for ID: {foreignBookId}");
                    var result = await provider.GetBookInfoAsync(foreignBookId);
                    
                    if (result != null && result.Item2 != null)
                    {
                        _logger.Debug($"Successfully retrieved book info from {provider.Name}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to get book info from {provider.Name} for ID {foreignBookId}: {ex.Message}");
                }
            }
            
            _logger.Error($"Failed to get book info from any provider for ID: {foreignBookId}");
            throw new BookNotFoundException(foreignBookId);
        }

        public async Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled && p.CanHandle(foreignAuthorId)).ToList();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Attempting to get author info from {provider.Name} for ID: {foreignAuthorId}");
                    var result = await provider.GetAuthorInfoAsync(foreignAuthorId, useCache);
                    
                    if (result != null)
                    {
                        _logger.Debug($"Successfully retrieved author info from {provider.Name}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to get author info from {provider.Name} for ID {foreignAuthorId}: {ex.Message}");
                }
            }
            
            _logger.Error($"Failed to get author info from any provider for ID: {foreignAuthorId}");
            throw new AuthorNotFoundException(foreignAuthorId);
        }

        public async Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
            var allResults = new List<Book>();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Searching for book '{title}' by '{author}' using {provider.Name}");
                    var results = await provider.SearchForNewBookAsync(title, author, getAllEditions);
                    
                    if (results != null && results.Any())
                    {
                        _logger.Debug($"Found {results.Count} results from {provider.Name}");
                        allResults.AddRange(results);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to search for book using {provider.Name}: {ex.Message}");
                }
            }
            
            // Remove duplicates and return best matches
            var uniqueResults = allResults
                .GroupBy(b => b.ForeignBookId)
                .Select(g => g.OrderByDescending(b => GetBookConfidenceScore(b)).First())
                .ToList();
            
            _logger.Debug($"Returning {uniqueResults.Count} unique book results");
            return uniqueResults;
        }

        public async Task<List<Author>> SearchForNewAuthorAsync(string title)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
            var allResults = new List<Author>();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Searching for author '{title}' using {provider.Name}");
                    var results = await provider.SearchForNewAuthorAsync(title);
                    
                    if (results != null && results.Any())
                    {
                        _logger.Debug($"Found {results.Count} results from {provider.Name}");
                        allResults.AddRange(results);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to search for author using {provider.Name}: {ex.Message}");
                }
            }
            
            // Remove duplicates and return best matches
            var uniqueResults = allResults
                .GroupBy(a => a.ForeignAuthorId)
                .Select(g => g.OrderByDescending(a => GetAuthorConfidenceScore(a)).First())
                .ToList();
            
            _logger.Debug($"Returning {uniqueResults.Count} unique author results");
            return uniqueResults;
        }

        public async Task<List<Book>> SearchByIsbnAsync(string isbn)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Searching by ISBN '{isbn}' using {provider.Name}");
                    var results = await provider.SearchByIsbnAsync(isbn);
                    
                    if (results != null && results.Any())
                    {
                        _logger.Debug($"Found {results.Count} results from {provider.Name}");
                        return results;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to search by ISBN using {provider.Name}: {ex.Message}");
                }
            }
            
            _logger.Debug($"No results found for ISBN: {isbn}");
            return new List<Book>();
        }

        public async Task<List<Book>> SearchByAsinAsync(string asin)
        {
            var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
            
            foreach (var provider in enabledProviders)
            {
                try
                {
                    _logger.Debug($"Searching by ASIN '{asin}' using {provider.Name}");
                    var results = await provider.SearchByAsinAsync(asin);
                    
                    if (results != null && results.Any())
                    {
                        _logger.Debug($"Found {results.Count} results from {provider.Name}");
                        return results;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to search by ASIN using {provider.Name}: {ex.Message}");
                }
            }
            
            _logger.Debug($"No results found for ASIN: {asin}");
            return new List<Book>();
        }

        private double GetBookConfidenceScore(Book book)
        {
            // Simple confidence scoring based on available data
            double score = 0.0;
            
            if (!string.IsNullOrWhiteSpace(book.Title)) score += 0.3;
            if (book.Author?.Metadata != null) score += 0.2;
            if (book.Editions?.Any() == true) score += 0.2;
            if (book.ReleaseDate.HasValue) score += 0.1;
            if (book.Ratings?.Value > 0) score += 0.1;
            if (!string.IsNullOrWhiteSpace(book.Overview)) score += 0.1;
            
            return Math.Min(score, 1.0);
        }

        private double GetAuthorConfidenceScore(Author author)
        {
            // Simple confidence scoring based on available data
            double score = 0.0;
            
            if (!string.IsNullOrWhiteSpace(author.Name)) score += 0.4;
            if (author.Books?.Any() == true) score += 0.3;
            if (!string.IsNullOrWhiteSpace(author.Metadata?.Overview)) score += 0.2;
            if (author.Metadata?.Images?.Any() == true) score += 0.1;
            
            return Math.Min(score, 1.0);
        }
    }
} 