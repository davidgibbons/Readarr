using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Model;
using NzbDrone.Core.MetadataSource.MachineLearning;
using System.Text.Json;

namespace NzbDrone.Core.MetadataSource
{
    public interface IEnhancedMetadataService
    {
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId);
        Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true);
        Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true);
        Task<List<Author>> SearchForNewAuthorAsync(string title);
        Task<List<Book>> SearchByIsbnAsync(string isbn);
        Task<List<Book>> SearchByAsinAsync(string asin);
        
        // Enhanced features
        Task<MetadataBook> GetOrCreateMetadataBookAsync(string identifier);
        Task<MetadataAuthor> GetOrCreateMetadataAuthorAsync(string identifier);
        Task<List<MetadataBook>> FindSimilarBooksAsync(string title, string author);
        Task<List<MetadataAuthor>> FindSimilarAuthorsAsync(string name);
        Task<List<MetadataBook>> DetectSeriesAsync(List<MetadataBook> books);
        Task<MetadataBook> MergeDuplicateBooksAsync(MetadataBook primary, MetadataBook secondary);
        Task<MetadataAuthor> MergeDuplicateAuthorsAsync(MetadataAuthor primary, MetadataAuthor secondary);
    }

    public class EnhancedMetadataService : IEnhancedMetadataService
    {
        private readonly IMetadataAggregator _metadataAggregator;
        private readonly IMetadataMatchingService _matchingService;
        private readonly IMetadataBookRepository _metadataBookRepository;
        private readonly IMetadataAuthorRepository _metadataAuthorRepository;
        private readonly IMetadataSourceRepository _metadataSourceRepository;
        private readonly Logger _logger;

        public EnhancedMetadataService(
            IMetadataAggregator metadataAggregator,
            IMetadataMatchingService matchingService,
            IMetadataBookRepository metadataBookRepository,
            IMetadataAuthorRepository metadataAuthorRepository,
            IMetadataSourceRepository metadataSourceRepository,
            Logger logger)
        {
            _metadataAggregator = metadataAggregator;
            _matchingService = matchingService;
            _metadataBookRepository = metadataBookRepository;
            _metadataAuthorRepository = metadataAuthorRepository;
            _metadataSourceRepository = metadataSourceRepository;
            _logger = logger;
        }

        public async Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId)
        {
            try
            {
                // First, try to get from database
                var metadataBook = await GetOrCreateMetadataBookAsync(foreignBookId);
                if (metadataBook != null && metadataBook.ConfidenceScore >= 0.7m)
                {
                    _logger.Debug($"Using cached metadata for book {foreignBookId} (confidence: {metadataBook.ConfidenceScore})");
                    return ConvertMetadataBookToBook(metadataBook);
                }

                // If not in database or low confidence, get from providers
                var result = await _metadataAggregator.GetBookInfoAsync(foreignBookId);
                
                // Store in database for future use
                await StoreBookMetadataAsync(foreignBookId, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get book info for {foreignBookId}");
                throw;
            }
        }

        public async Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true)
        {
            try
            {
                // First, try to get from database
                var metadataAuthor = await GetOrCreateMetadataAuthorAsync(foreignAuthorId);
                if (metadataAuthor != null && metadataAuthor.ConfidenceScore >= 0.7m)
                {
                    _logger.Debug($"Using cached metadata for author {foreignAuthorId} (confidence: {metadataAuthor.ConfidenceScore})");
                    return ConvertMetadataAuthorToAuthor(metadataAuthor);
                }

                // If not in database or low confidence, get from providers
                var result = await _metadataAggregator.GetAuthorInfoAsync(foreignAuthorId, useCache);
                
                // Store in database for future use
                await StoreAuthorMetadataAsync(foreignAuthorId, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get author info for {foreignAuthorId}");
                throw;
            }
        }

        public async Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true)
        {
            try
            {
                // Search in database first
                var cachedResults = await SearchCachedBooksAsync(title, author);
                if (cachedResults.Any())
                {
                    _logger.Debug($"Found {cachedResults.Count} cached results for '{title}' by '{author}'");
                    return cachedResults;
                }

                // If no cached results, search providers
                var results = await _metadataAggregator.SearchForNewBookAsync(title, author, getAllEditions);
                
                // Store results in database
                foreach (var book in results)
                {
                    await StoreBookMetadataAsync(book.ForeignBookId, Tuple.Create(book.AuthorMetadata.Value.ForeignAuthorId, book, new List<AuthorMetadata> { book.AuthorMetadata.Value }));
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search for book '{title}' by '{author}'");
                return new List<Book>();
            }
        }

        public async Task<List<Author>> SearchForNewAuthorAsync(string title)
        {
            try
            {
                // Search in database first
                var cachedResults = await SearchCachedAuthorsAsync(title);
                if (cachedResults.Any())
                {
                    _logger.Debug($"Found {cachedResults.Count} cached results for author '{title}'");
                    return cachedResults;
                }

                // If no cached results, search providers
                var results = await _metadataAggregator.SearchForNewAuthorAsync(title);
                
                // Store results in database
                foreach (var author in results)
                {
                    await StoreAuthorMetadataAsync(author.ForeignAuthorId, author);
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search for author '{title}'");
                return new List<Author>();
            }
        }

        public async Task<List<Book>> SearchByIsbnAsync(string isbn)
        {
            try
            {
                // Search in database first
                var cachedBook = await _metadataBookRepository.GetByIsbnAsync(isbn);
                if (cachedBook != null)
                {
                    _logger.Debug($"Found cached book for ISBN {isbn}");
                    return new List<Book> { ConvertMetadataBookToBook(cachedBook).Item2 };
                }

                // If not in database, search providers
                var results = await _metadataAggregator.SearchByIsbnAsync(isbn);
                
                // Store results in database
                foreach (var book in results)
                {
                    await StoreBookMetadataAsync(book.ForeignBookId, Tuple.Create(book.AuthorMetadata.Value.ForeignAuthorId, book, new List<AuthorMetadata> { book.AuthorMetadata.Value }));
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search by ISBN {isbn}");
                return new List<Book>();
            }
        }

        public async Task<List<Book>> SearchByAsinAsync(string asin)
        {
            try
            {
                // Search providers (ASIN is less common in our database)
                var results = await _metadataAggregator.SearchByAsinAsync(asin);
                
                // Store results in database
                foreach (var book in results)
                {
                    await StoreBookMetadataAsync(book.ForeignBookId, Tuple.Create(book.AuthorMetadata.Value.ForeignAuthorId, book, new List<AuthorMetadata> { book.AuthorMetadata.Value }));
                }
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search by ASIN {asin}");
                return new List<Book>();
            }
        }

        #region Enhanced Features

        public async Task<MetadataBook> GetOrCreateMetadataBookAsync(string identifier)
        {
            var metadataBook = await _metadataBookRepository.GetByIdentifierAsync(identifier);
            if (metadataBook == null)
            {
                metadataBook = new MetadataBook
                {
                    GoodreadsId = identifier.All(char.IsDigit) ? identifier : null,
                    Isbn13 = IsValidIsbn13(identifier) ? identifier : null,
                    Isbn10 = IsValidIsbn10(identifier) ? identifier : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                
                metadataBook.UpdateConfidenceScore();
                metadataBook = await _metadataBookRepository.InsertAsync(metadataBook);
            }
            
            return metadataBook;
        }

        public async Task<MetadataAuthor> GetOrCreateMetadataAuthorAsync(string identifier)
        {
            var metadataAuthor = await _metadataAuthorRepository.GetByIdentifierAsync(identifier);
            if (metadataAuthor == null)
            {
                metadataAuthor = new MetadataAuthor
                {
                    GoodreadsId = identifier.All(char.IsDigit) ? identifier : null,
                    Name = identifier,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                
                metadataAuthor.UpdateConfidenceScore();
                metadataAuthor = await _metadataAuthorRepository.InsertAsync(metadataAuthor);
            }
            
            return metadataAuthor;
        }

        public async Task<List<MetadataBook>> FindSimilarBooksAsync(string title, string author)
        {
            var allBooks = await _metadataBookRepository.GetAllAsync();
            var searchBook = new MetadataBook { Title = title };
            
            return _matchingService.FindSimilarBooks(searchBook, allBooks);
        }

        public async Task<List<MetadataAuthor>> FindSimilarAuthorsAsync(string name)
        {
            var allAuthors = await _metadataAuthorRepository.GetAllAsync();
            var searchAuthor = new MetadataAuthor { Name = name };
            
            return _matchingService.FindSimilarAuthors(searchAuthor, allAuthors);
        }

        public async Task<List<MetadataBook>> DetectSeriesAsync(List<MetadataBook> books)
        {
            return _matchingService.DetectSeries(books);
        }

        public async Task<MetadataBook> MergeDuplicateBooksAsync(MetadataBook primary, MetadataBook secondary)
        {
            var merged = _matchingService.MergeBooks(primary, secondary);
            merged = await _metadataBookRepository.UpdateAsync(merged);
            
            // Mark secondary as inactive
            secondary.UpdatedAt = DateTime.UtcNow;
            await _metadataBookRepository.DeleteAsync(secondary.Id);
            
            _logger.Info($"Merged duplicate books: {primary.Title} and {secondary.Title}");
            return merged;
        }

        public async Task<MetadataAuthor> MergeDuplicateAuthorsAsync(MetadataAuthor primary, MetadataAuthor secondary)
        {
            var merged = _matchingService.MergeAuthors(primary, secondary);
            merged = await _metadataAuthorRepository.UpdateAsync(merged);
            
            // Mark secondary as inactive
            secondary.UpdatedAt = DateTime.UtcNow;
            await _metadataAuthorRepository.DeleteAsync(secondary.Id);
            
            _logger.Info($"Merged duplicate authors: {primary.Name} and {secondary.Name}");
            return merged;
        }

        #endregion

        #region Private Methods

        private async Task StoreBookMetadataAsync(string identifier, Tuple<string, Book, List<AuthorMetadata>> bookInfo)
        {
            try
            {
                var metadataBook = await GetOrCreateMetadataBookAsync(identifier);
                
                // Update metadata book with new information
                metadataBook.Title = bookInfo.Item2.Title;
                metadataBook.Description = bookInfo.Item2.Overview;
                metadataBook.PublicationDate = bookInfo.Item2.ReleaseDate;
                metadataBook.UpdatedAt = DateTime.UtcNow;
                
                // Store source information
                var sourceData = new Dictionary<string, object>
                {
                    ["title"] = bookInfo.Item2.Title,
                    ["overview"] = bookInfo.Item2.Overview,
                    ["releaseDate"] = bookInfo.Item2.ReleaseDate?.ToString("O"),
                    ["foreignBookId"] = bookInfo.Item2.ForeignBookId
                };
                
                metadataBook.SetSourceData(sourceData);
                metadataBook.UpdateConfidenceScore();
                
                await _metadataBookRepository.UpdateAsync(metadataBook);
                
                // Store source record
                var source = new MetadataSource
                {
                    BookId = metadataBook.Id,
                    SourceName = "aggregator",
                    SourceData = JsonSerializer.Serialize(sourceData),
                    RetrievedAt = DateTime.UtcNow,
                    IsActive = true
                };
                
                await _metadataSourceRepository.InsertAsync(source);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to store book metadata for {identifier}");
            }
        }

        private async Task StoreAuthorMetadataAsync(string identifier, Author author)
        {
            try
            {
                var metadataAuthor = await GetOrCreateMetadataAuthorAsync(identifier);
                
                // Update metadata author with new information
                metadataAuthor.Name = author.Name;
                metadataAuthor.Biography = author.Metadata?.Overview;
                metadataAuthor.UpdatedAt = DateTime.UtcNow;
                
                // Store source information
                var sourceData = new Dictionary<string, object>
                {
                    ["name"] = author.Name,
                    ["overview"] = author.Metadata?.Overview,
                    ["foreignAuthorId"] = author.ForeignAuthorId
                };
                
                metadataAuthor.SetSourceData(sourceData);
                metadataAuthor.UpdateConfidenceScore();
                
                await _metadataAuthorRepository.UpdateAsync(metadataAuthor);
                
                // Store source record
                var source = new MetadataSource
                {
                    AuthorId = metadataAuthor.Id,
                    SourceName = "aggregator",
                    SourceData = JsonSerializer.Serialize(sourceData),
                    RetrievedAt = DateTime.UtcNow,
                    IsActive = true
                };
                
                await _metadataSourceRepository.InsertAsync(source);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to store author metadata for {identifier}");
            }
        }

        private async Task<List<Book>> SearchCachedBooksAsync(string title, string author)
        {
            var cachedBooks = await _metadataBookRepository.SearchAsync(title, author);
            return cachedBooks.Select(ConvertMetadataBookToBook).Select(t => t.Item2).ToList();
        }

        private async Task<List<Author>> SearchCachedAuthorsAsync(string name)
        {
            var cachedAuthors = await _metadataAuthorRepository.SearchAsync(name);
            return cachedAuthors.Select(ConvertMetadataAuthorToAuthor).ToList();
        }

        private Tuple<string, Book, List<AuthorMetadata>> ConvertMetadataBookToBook(MetadataBook metadataBook)
        {
            var book = new Book
            {
                ForeignBookId = metadataBook.GoodreadsId ?? metadataBook.Id.ToString(),
                Title = metadataBook.Title,
                Overview = metadataBook.Description,
                ReleaseDate = metadataBook.PublicationDate
            };

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = "0", // Will be updated when author is found
                Name = "Unknown Author"
            };

            return Tuple.Create(authorMetadata.ForeignAuthorId, book, new List<AuthorMetadata> { authorMetadata });
        }

        private Author ConvertMetadataAuthorToAuthor(MetadataAuthor metadataAuthor)
        {
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = metadataAuthor.GoodreadsId ?? metadataAuthor.Id.ToString(),
                Name = metadataAuthor.Name,
                Overview = metadataAuthor.Biography
            };

            return new Author
            {
                ForeignAuthorId = authorMetadata.ForeignAuthorId,
                Name = authorMetadata.Name,
                Metadata = authorMetadata
            };
        }

        private bool IsValidIsbn13(string isbn)
        {
            return !string.IsNullOrWhiteSpace(isbn) && isbn.Length == 13 && isbn.All(char.IsDigit);
        }

        private bool IsValidIsbn10(string isbn)
        {
            return !string.IsNullOrWhiteSpace(isbn) && isbn.Length == 10 && isbn.All(char.IsDigit);
        }

        #endregion
    }

    #region Repository Interfaces (to be implemented)

    public interface IMetadataBookRepository : IBasicRepository<MetadataBook>
    {
        Task<MetadataBook> GetByIdentifierAsync(string identifier);
        Task<MetadataBook> GetByIsbnAsync(string isbn);
        Task<List<MetadataBook>> SearchAsync(string title, string author);
        Task<List<MetadataBook>> GetAllAsync();
    }

    public interface IMetadataAuthorRepository : IBasicRepository<MetadataAuthor>
    {
        Task<MetadataAuthor> GetByIdentifierAsync(string identifier);
        Task<List<MetadataAuthor>> SearchAsync(string name);
        Task<List<MetadataAuthor>> GetAllAsync();
    }

    public interface IMetadataSourceRepository : IBasicRepository<MetadataSource>
    {
        Task<List<MetadataSource>> GetByBookIdAsync(int bookId);
        Task<List<MetadataSource>> GetByAuthorIdAsync(int authorId);
    }

    #endregion
} 