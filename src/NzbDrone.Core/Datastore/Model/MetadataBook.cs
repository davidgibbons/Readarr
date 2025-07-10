using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Datastore.Model
{
    public class MetadataBook : ModelBase
    {
        public string GoodreadsId { get; set; }
        public string Isbn13 { get; set; }
        public string Isbn10 { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public DateTime? PublicationDate { get; set; }
        public int? PageCount { get; set; }
        public string Language { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string SourceData { get; set; } // JSON data from sources

        // Navigation properties
        public List<MetadataSource> Sources { get; set; } = new List<MetadataSource>();
        public List<MetadataSeriesBook> SeriesBooks { get; set; } = new List<MetadataSeriesBook>();

        // Helper methods
        public Dictionary<string, object> GetSourceData()
        {
            if (string.IsNullOrWhiteSpace(SourceData))
                return new Dictionary<string, object>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(SourceData);
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        public void SetSourceData(Dictionary<string, object> data)
        {
            SourceData = JsonSerializer.Serialize(data);
        }

        public void UpdateConfidenceScore()
        {
            var score = 0.0m;
            var factors = 0;

            if (!string.IsNullOrWhiteSpace(Title)) { score += 0.3m; factors++; }
            if (!string.IsNullOrWhiteSpace(Description)) { score += 0.2m; factors++; }
            if (PublicationDate.HasValue) { score += 0.1m; factors++; }
            if (PageCount.HasValue) { score += 0.1m; factors++; }
            if (!string.IsNullOrWhiteSpace(Isbn13) || !string.IsNullOrWhiteSpace(Isbn10)) { score += 0.2m; factors++; }
            if (!string.IsNullOrWhiteSpace(GoodreadsId)) { score += 0.1m; factors++; }

            ConfidenceScore = factors > 0 ? score / factors : 0.0m;
        }
    }
} 