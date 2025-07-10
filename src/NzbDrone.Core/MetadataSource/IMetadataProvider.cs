using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataProvider
    {
        string Name { get; }
        int Priority { get; }
        bool IsEnabled { get; }
        
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string foreignBookId);
        Task<Author> GetAuthorInfoAsync(string foreignAuthorId, bool useCache = true);
        Task<List<Book>> SearchForNewBookAsync(string title, string author, bool getAllEditions = true);
        Task<List<Author>> SearchForNewAuthorAsync(string title);
        Task<List<Book>> SearchByIsbnAsync(string isbn);
        Task<List<Book>> SearchByAsinAsync(string asin);
        
        // Fallback support
        bool CanHandle(string identifier);
        double GetConfidenceScore(string identifier);
    }
} 