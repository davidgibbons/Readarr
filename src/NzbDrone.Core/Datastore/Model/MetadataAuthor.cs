using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Datastore.Model
{
    public class MetadataAuthor : ModelBase
    {
        public string GoodreadsId { get; set; }
        public string Name { get; set; }
        public string Biography { get; set; }
        public DateTime? BirthDate { get; set; }
        public DateTime? DeathDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string SourceData { get; set; } // JSON data from sources

        // Navigation properties
        public List<MetadataSource> Sources { get; set; } = new List<MetadataSource>();

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

            if (!string.IsNullOrWhiteSpace(Name)) { score += 0.4m; factors++; }
            if (!string.IsNullOrWhiteSpace(Biography)) { score += 0.3m; factors++; }
            if (BirthDate.HasValue) { score += 0.1m; factors++; }
            if (DeathDate.HasValue) { score += 0.1m; factors++; }
            if (!string.IsNullOrWhiteSpace(GoodreadsId)) { score += 0.1m; factors++; }

            ConfidenceScore = factors > 0 ? score / factors : 0.0m;
        }
    }
} 