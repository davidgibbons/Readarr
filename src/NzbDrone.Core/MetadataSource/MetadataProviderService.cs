using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataProviderService
    {
        IMetadataAggregator GetAggregator();
        List<IMetadataProvider> GetAllProviders();
        List<IMetadataProvider> GetEnabledProviders();
        void EnableProvider(string providerName);
        void DisableProvider(string providerName);
        void SetProviderPriority(string providerName, int priority);
    }

    public class MetadataProviderService : IMetadataProviderService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Logger _logger;
        private readonly Dictionary<string, bool> _providerStates;
        private readonly Dictionary<string, int> _providerPriorities;

        public MetadataProviderService(IServiceProvider serviceProvider, Logger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            // Initialize provider states and priorities
            _providerStates = new Dictionary<string, bool>
            {
                ["rreading-glasses"] = true,  // Default provider
                ["web-scraping"] = true,      // Web scraping provider
                ["local-service"] = false     // Local metadata service (optional)
            };

            _providerPriorities = new Dictionary<string, int>
            {
                ["rreading-glasses"] = 1,     // Highest priority
                ["web-scraping"] = 2,         // Medium priority
                ["local-service"] = 3         // Lowest priority
            };
        }

        public IMetadataAggregator GetAggregator()
        {
            var providers = GetEnabledProviders();
            return new MetadataAggregator(providers, _logger);
        }

        public List<IMetadataProvider> GetAllProviders()
        {
            var providers = new List<IMetadataProvider>();

            try
            {
                // Get rreading-glasses provider (BookInfoProxy)
                var bookInfoProxy = _serviceProvider.GetService<BookInfoProxy>();
                if (bookInfoProxy != null)
                {
                    providers.Add(new BookInfoProxyAdapter(bookInfoProxy, "rreading-glasses"));
                }

                // Get web scraping provider
                var webScrapingProvider = _serviceProvider.GetService<WebScraping.WebScrapingMetadataProvider>();
                if (webScrapingProvider != null)
                {
                    providers.Add(webScrapingProvider);
                }

                // Get local service provider (if configured)
                var localServiceProvider = _serviceProvider.GetService<LocalMetadataProvider>();
                if (localServiceProvider != null)
                {
                    providers.Add(localServiceProvider);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting metadata providers");
            }

            return providers;
        }

        public List<IMetadataProvider> GetEnabledProviders()
        {
            var allProviders = GetAllProviders();
            var enabledProviders = new List<IMetadataProvider>();

            foreach (var provider in allProviders)
            {
                if (_providerStates.TryGetValue(provider.Name.ToLowerInvariant(), out var isEnabled) && isEnabled)
                {
                    // Set priority based on configuration
                    if (_providerPriorities.TryGetValue(provider.Name.ToLowerInvariant(), out var priority))
                    {
                        if (provider is WebScraping.WebScrapingMetadataProvider webProvider)
                        {
                            // Update priority dynamically
                            webProvider.SetPriority(priority);
                        }
                    }
                    
                    enabledProviders.Add(provider);
                }
            }

            // Sort by priority
            return enabledProviders.OrderBy(p => p.Priority).ToList();
        }

        public void EnableProvider(string providerName)
        {
            var key = providerName.ToLowerInvariant();
            if (_providerStates.ContainsKey(key))
            {
                _providerStates[key] = true;
                _logger.Info($"Enabled metadata provider: {providerName}");
            }
            else
            {
                _logger.Warn($"Unknown metadata provider: {providerName}");
            }
        }

        public void DisableProvider(string providerName)
        {
            var key = providerName.ToLowerInvariant();
            if (_providerStates.ContainsKey(key))
            {
                _providerStates[key] = false;
                _logger.Info($"Disabled metadata provider: {providerName}");
            }
            else
            {
                _logger.Warn($"Unknown metadata provider: {providerName}");
            }
        }

        public void SetProviderPriority(string providerName, int priority)
        {
            var key = providerName.ToLowerInvariant();
            if (_providerPriorities.ContainsKey(key))
            {
                _providerPriorities[key] = priority;
                _logger.Info($"Set priority for metadata provider {providerName} to {priority}");
            }
            else
            {
                _logger.Warn($"Unknown metadata provider: {providerName}");
            }
        }
    }

    // Adapter to make BookInfoProxy implement IMetadataProvider
    public class BookInfoProxyAdapter : IMetadataProvider
    {
        private readonly BookInfoProxy _bookInfoProxy;
        private readonly string _name;

        public BookInfoProxyAdapter(BookInfoProxy bookInfoProxy, string name)
        {
            _bookInfoProxy = bookInfoProxy;
            _name = name;
        }

        public string Name => _name;
        public int Priority => 1; // Highest priority
        public bool IsEnabled => true;

        public bool CanHandle(string identifier)
        {
            // BookInfoProxy can handle most identifiers
            return !string.IsNullOrWhiteSpace(identifier);
        }

        public double GetConfidenceScore(string identifier)
        {
            // High confidence for the primary provider
            return 0.9;
        }

        public async Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId)
        {
            return await Task.Run(() => _bookInfoProxy.GetBookInfo(foreignBookId));
        }

        public async Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true)
        {
            return await Task.Run(() => _bookInfoProxy.GetAuthorInfo(foreignAuthorId, useCache));
        }

        public async Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true)
        {
            return await Task.Run(() => _bookInfoProxy.SearchForNewBook(title, author, getAllEditions));
        }

        public async Task<List<Author>> SearchForNewAuthorAsync(string title)
        {
            return await Task.Run(() => _bookInfoProxy.SearchForNewAuthor(title));
        }

        public async Task<List<Book>> SearchByIsbnAsync(string isbn)
        {
            return await Task.Run(() => _bookInfoProxy.SearchByIsbn(isbn));
        }

        public async Task<List<Book>> SearchByAsinAsync(string asin)
        {
            return await Task.Run(() => _bookInfoProxy.SearchByAsin(asin));
        }
    }

    // Local metadata service provider (for the Python service)
    public class LocalMetadataProvider : IMetadataProvider
    {
        public string Name => "Local Metadata Service";
        public int Priority { get; private set; } = 3;
        public bool IsEnabled => true;

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _baseUrl;

        public LocalMetadataProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _baseUrl = "http://localhost:8787"; // Default local service URL
        }

        public bool CanHandle(string identifier)
        {
            // Can handle most identifiers
            return !string.IsNullOrWhiteSpace(identifier);
        }

        public double GetConfidenceScore(string identifier)
        {
            // Medium confidence for local service
            return 0.7;
        }

        public void SetPriority(int priority)
        {
            Priority = priority;
        }

        public async Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId)
        {
            try
            {
                var request = new HttpRequest($"{_baseUrl}/book/{foreignBookId}");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Book format
                    // TODO: Implement response parsing
                    _logger.Debug($"Retrieved book info from local service for ID: {foreignBookId}");
                    return null; // Placeholder
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get book info from local service for {foreignBookId}");
            }
            
            return null;
        }

        public async Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true)
        {
            try
            {
                var request = new HttpRequest($"{_baseUrl}/author/{foreignAuthorId}");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Author format
                    // TODO: Implement response parsing
                    _logger.Debug($"Retrieved author info from local service for ID: {foreignAuthorId}");
                    return null; // Placeholder
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get author info from local service for {foreignAuthorId}");
            }
            
            return null;
        }

        public async Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true)
        {
            try
            {
                var query = $"{title} {author}".Trim();
                var request = new HttpRequest($"{_baseUrl}/search?q={Uri.EscapeDataString(query)}");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Book list
                    // TODO: Implement response parsing
                    _logger.Debug($"Searched for book '{title}' by '{author}' using local service");
                    return new List<Book>();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search for book using local service: {title} by {author}");
            }
            
            return new List<Book>();
        }

        public async Task<List<Author>> SearchForNewAuthorAsync(string title)
        {
            try
            {
                var request = new HttpRequest($"{_baseUrl}/search?q={Uri.EscapeDataString(title)}&type=author");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Author list
                    // TODO: Implement response parsing
                    _logger.Debug($"Searched for author '{title}' using local service");
                    return new List<Author>();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search for author using local service: {title}");
            }
            
            return new List<Author>();
        }

        public async Task<List<Book>> SearchByIsbnAsync(string isbn)
        {
            try
            {
                var request = new HttpRequest($"{_baseUrl}/search?q={Uri.EscapeDataString(isbn)}&type=isbn");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Book list
                    // TODO: Implement response parsing
                    _logger.Debug($"Searched by ISBN '{isbn}' using local service");
                    return new List<Book>();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search by ISBN using local service: {isbn}");
            }
            
            return new List<Book>();
        }

        public async Task<List<Book>> SearchByAsinAsync(string asin)
        {
            try
            {
                var request = new HttpRequest($"{_baseUrl}/search?q={Uri.EscapeDataString(asin)}&type=asin");
                var response = await _httpClient.GetAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Parse response and convert to Book list
                    // TODO: Implement response parsing
                    _logger.Debug($"Searched by ASIN '{asin}' using local service");
                    return new List<Book>();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search by ASIN using local service: {asin}");
            }
            
            return new List<Book>();
        }
    }
} 