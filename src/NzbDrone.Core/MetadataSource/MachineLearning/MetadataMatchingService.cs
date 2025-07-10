using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Model;

namespace NzbDrone.Core.MetadataSource.MachineLearning
{
    public interface IMetadataMatchingService
    {
        double CalculateSimilarity(string text1, string text2);
        List<MetadataBook> FindSimilarBooks(MetadataBook book, List<MetadataBook> candidates, double threshold = 0.8);
        List<MetadataAuthor> FindSimilarAuthors(MetadataAuthor author, List<MetadataAuthor> candidates, double threshold = 0.8);
        bool IsDuplicate(MetadataBook book1, MetadataBook book2, double threshold = 0.9);
        bool IsDuplicate(MetadataAuthor author1, MetadataAuthor author2, double threshold = 0.9);
        MetadataBook MergeBooks(MetadataBook primary, MetadataBook secondary);
        MetadataAuthor MergeAuthors(MetadataAuthor primary, MetadataAuthor secondary);
        List<MetadataBook> DetectSeries(List<MetadataBook> books);
    }

    public class MetadataMatchingService : IMetadataMatchingService
    {
        private readonly Logger _logger;

        public MetadataMatchingService(Logger logger)
        {
            _logger = logger;
        }

        public double CalculateSimilarity(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
                return 0.0;

            // Normalize text
            text1 = NormalizeText(text1);
            text2 = NormalizeText(text2);

            if (text1 == text2)
                return 1.0;

            // Calculate multiple similarity metrics
            var jaccardSimilarity = CalculateJaccardSimilarity(text1, text2);
            var levenshteinSimilarity = CalculateLevenshteinSimilarity(text1, text2);
            var cosineSimilarity = CalculateCosineSimilarity(text1, text2);

            // Weighted average of different similarity measures
            return (jaccardSimilarity * 0.3 + levenshteinSimilarity * 0.4 + cosineSimilarity * 0.3);
        }

        public List<MetadataBook> FindSimilarBooks(MetadataBook book, List<MetadataBook> candidates, double threshold = 0.8)
        {
            var similarBooks = new List<MetadataBook>();

            foreach (var candidate in candidates)
            {
                if (candidate.Id == book.Id)
                    continue;

                var similarity = CalculateBookSimilarity(book, candidate);
                if (similarity >= threshold)
                {
                    similarBooks.Add(candidate);
                    _logger.Debug($"Found similar book: {candidate.Title} (similarity: {similarity:F2})");
                }
            }

            return similarBooks.OrderByDescending(b => CalculateBookSimilarity(book, b)).ToList();
        }

        public List<MetadataAuthor> FindSimilarAuthors(MetadataAuthor author, List<MetadataAuthor> candidates, double threshold = 0.8)
        {
            var similarAuthors = new List<MetadataAuthor>();

            foreach (var candidate in candidates)
            {
                if (candidate.Id == author.Id)
                    continue;

                var similarity = CalculateAuthorSimilarity(author, candidate);
                if (similarity >= threshold)
                {
                    similarAuthors.Add(candidate);
                    _logger.Debug($"Found similar author: {candidate.Name} (similarity: {similarity:F2})");
                }
            }

            return similarAuthors.OrderByDescending(a => CalculateAuthorSimilarity(author, a)).ToList();
        }

        public bool IsDuplicate(MetadataBook book1, MetadataBook book2, double threshold = 0.9)
        {
            var similarity = CalculateBookSimilarity(book1, book2);
            return similarity >= threshold;
        }

        public bool IsDuplicate(MetadataAuthor author1, MetadataAuthor author2, double threshold = 0.9)
        {
            var similarity = CalculateAuthorSimilarity(author1, author2);
            return similarity >= threshold;
        }

        public MetadataBook MergeBooks(MetadataBook primary, MetadataBook secondary)
        {
            var merged = new MetadataBook
            {
                Id = primary.Id,
                GoodreadsId = primary.GoodreadsId ?? secondary.GoodreadsId,
                Isbn13 = primary.Isbn13 ?? secondary.Isbn13,
                Isbn10 = primary.Isbn10 ?? secondary.Isbn10,
                Title = primary.Title ?? secondary.Title,
                Subtitle = primary.Subtitle ?? secondary.Subtitle,
                Description = MergeText(primary.Description, secondary.Description),
                PublicationDate = primary.PublicationDate ?? secondary.PublicationDate,
                PageCount = primary.PageCount ?? secondary.PageCount,
                Language = primary.Language ?? secondary.Language,
                CreatedAt = primary.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                ConfidenceScore = Math.Max(primary.ConfidenceScore ?? 0, secondary.ConfidenceScore ?? 0)
            };

            // Merge source data
            var mergedSourceData = new Dictionary<string, object>();
            mergedSourceData.Merge(primary.GetSourceData());
            mergedSourceData.Merge(secondary.GetSourceData());
            merged.SetSourceData(mergedSourceData);

            merged.UpdateConfidenceScore();
            return merged;
        }

        public MetadataAuthor MergeAuthors(MetadataAuthor primary, MetadataAuthor secondary)
        {
            var merged = new MetadataAuthor
            {
                Id = primary.Id,
                GoodreadsId = primary.GoodreadsId ?? secondary.GoodreadsId,
                Name = primary.Name ?? secondary.Name,
                Biography = MergeText(primary.Biography, secondary.Biography),
                BirthDate = primary.BirthDate ?? secondary.BirthDate,
                DeathDate = primary.DeathDate ?? secondary.DeathDate,
                CreatedAt = primary.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                ConfidenceScore = Math.Max(primary.ConfidenceScore ?? 0, secondary.ConfidenceScore ?? 0)
            };

            // Merge source data
            var mergedSourceData = new Dictionary<string, object>();
            mergedSourceData.Merge(primary.GetSourceData());
            mergedSourceData.Merge(secondary.GetSourceData());
            merged.SetSourceData(mergedSourceData);

            merged.UpdateConfidenceScore();
            return merged;
        }

        public List<MetadataBook> DetectSeries(List<MetadataBook> books)
        {
            var seriesGroups = new List<MetadataBook>();
            var processedBooks = new HashSet<int>();

            foreach (var book in books.OrderBy(b => b.Title))
            {
                if (processedBooks.Contains(book.Id))
                    continue;

                var series = FindSeriesBooks(book, books);
                if (series.Count > 1)
                {
                    seriesGroups.AddRange(series);
                    foreach (var seriesBook in series)
                    {
                        processedBooks.Add(seriesBook.Id);
                    }
                }
            }

            return seriesGroups;
        }

        #region Private Methods

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Convert to lowercase
            text = text.ToLowerInvariant();

            // Remove special characters and extra whitespace
            text = Regex.Replace(text, @"[^\w\s]", " ");
            text = Regex.Replace(text, @"\s+", " ");

            return text.Trim();
        }

        private double CalculateJaccardSimilarity(string text1, string text2)
        {
            var words1 = new HashSet<string>(text1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var words2 = new HashSet<string>(text2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (words1.Count == 0 && words2.Count == 0)
                return 1.0;

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            return union > 0 ? (double)intersection / union : 0.0;
        }

        private double CalculateLevenshteinSimilarity(string text1, string text2)
        {
            var distance = CalculateLevenshteinDistance(text1, text2);
            var maxLength = Math.Max(text1.Length, text2.Length);
            return maxLength > 0 ? 1.0 - ((double)distance / maxLength) : 1.0;
        }

        private int CalculateLevenshteinDistance(string text1, string text2)
        {
            var matrix = new int[text1.Length + 1, text2.Length + 1];

            for (int i = 0; i <= text1.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= text2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= text1.Length; i++)
            {
                for (int j = 1; j <= text2.Length; j++)
                {
                    var cost = text1[i - 1] == text2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[text1.Length, text2.Length];
        }

        private double CalculateCosineSimilarity(string text1, string text2)
        {
            var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var allWords = words1.Union(words2).Distinct().ToList();
            var vector1 = new double[allWords.Count];
            var vector2 = new double[allWords.Count];

            for (int i = 0; i < allWords.Count; i++)
            {
                vector1[i] = words1.Count(w => w == allWords[i]);
                vector2[i] = words2.Count(w => w == allWords[i]);
            }

            var dotProduct = 0.0;
            var magnitude1 = 0.0;
            var magnitude2 = 0.0;

            for (int i = 0; i < allWords.Count; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0.0;

            return dotProduct / (magnitude1 * magnitude2);
        }

        private double CalculateBookSimilarity(MetadataBook book1, MetadataBook book2)
        {
            var similarities = new List<double>();

            // Title similarity
            if (!string.IsNullOrWhiteSpace(book1.Title) && !string.IsNullOrWhiteSpace(book2.Title))
            {
                similarities.Add(CalculateSimilarity(book1.Title, book2.Title) * 0.4);
            }

            // ISBN similarity
            if (!string.IsNullOrWhiteSpace(book1.Isbn13) && !string.IsNullOrWhiteSpace(book2.Isbn13))
            {
                similarities.Add(book1.Isbn13 == book2.Isbn13 ? 1.0 : 0.0);
            }
            else if (!string.IsNullOrWhiteSpace(book1.Isbn10) && !string.IsNullOrWhiteSpace(book2.Isbn10))
            {
                similarities.Add(book1.Isbn10 == book2.Isbn10 ? 1.0 : 0.0);
            }

            // Goodreads ID similarity
            if (!string.IsNullOrWhiteSpace(book1.GoodreadsId) && !string.IsNullOrWhiteSpace(book2.GoodreadsId))
            {
                similarities.Add(book1.GoodreadsId == book2.GoodreadsId ? 1.0 : 0.0);
            }

            // Publication date similarity
            if (book1.PublicationDate.HasValue && book2.PublicationDate.HasValue)
            {
                var dateDiff = Math.Abs((book1.PublicationDate.Value - book2.PublicationDate.Value).Days);
                similarities.Add(dateDiff <= 365 ? 0.8 : 0.0); // Within a year
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private double CalculateAuthorSimilarity(MetadataAuthor author1, MetadataAuthor author2)
        {
            var similarities = new List<double>();

            // Name similarity
            if (!string.IsNullOrWhiteSpace(author1.Name) && !string.IsNullOrWhiteSpace(author2.Name))
            {
                similarities.Add(CalculateSimilarity(author1.Name, author2.Name) * 0.6);
            }

            // Goodreads ID similarity
            if (!string.IsNullOrWhiteSpace(author1.GoodreadsId) && !string.IsNullOrWhiteSpace(author2.GoodreadsId))
            {
                similarities.Add(author1.GoodreadsId == author2.GoodreadsId ? 1.0 : 0.0);
            }

            // Birth date similarity
            if (author1.BirthDate.HasValue && author2.BirthDate.HasValue)
            {
                var dateDiff = Math.Abs((author1.BirthDate.Value - author2.BirthDate.Value).Days);
                similarities.Add(dateDiff <= 365 ? 0.8 : 0.0); // Within a year
            }

            return similarities.Any() ? similarities.Average() : 0.0;
        }

        private string MergeText(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1))
                return text2;
            if (string.IsNullOrWhiteSpace(text2))
                return text1;

            // Prefer the longer text as it's likely more complete
            return text1.Length >= text2.Length ? text1 : text2;
        }

        private List<MetadataBook> FindSeriesBooks(MetadataBook book, List<MetadataBook> allBooks)
        {
            var seriesBooks = new List<MetadataBook> { book };

            // Look for series patterns in titles
            var seriesPattern = ExtractSeriesPattern(book.Title);
            if (!string.IsNullOrWhiteSpace(seriesPattern))
            {
                foreach (var otherBook in allBooks)
                {
                    if (otherBook.Id != book.Id && ExtractSeriesPattern(otherBook.Title) == seriesPattern)
                    {
                        seriesBooks.Add(otherBook);
                    }
                }
            }

            return seriesBooks;
        }

        private string ExtractSeriesPattern(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            // Common series patterns
            var patterns = new[]
            {
                @"^(.*?)\s*#?\d+", // "Series Name #1"
                @"^(.*?)\s*\(.*?\)", // "Series Name (Subtitle)"
                @"^(.*?)\s*[-–]\s*\d+", // "Series Name - 1"
                @"^(.*?)\s*Book\s*\d+", // "Series Name Book 1"
                @"^(.*?)\s*Volume\s*\d+" // "Series Name Volume 1"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }

            return string.Empty;
        }

        #endregion
    }

    public static class DictionaryExtensions
    {
        public static void Merge<TKey, TValue>(this Dictionary<TKey, TValue> target, Dictionary<TKey, TValue> source)
        {
            foreach (var kvp in source)
            {
                if (!target.ContainsKey(kvp.Key))
                {
                    target[kvp.Key] = kvp.Value;
                }
            }
        }
    }
} 