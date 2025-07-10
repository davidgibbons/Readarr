using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Datastore.Model
{
    public class MetadataSource : ModelBase
    {
        public int? BookId { get; set; }
        public int? AuthorId { get; set; }
        public string SourceName { get; set; }
        public string SourceData { get; set; } // JSON data from source
        public DateTime RetrievedAt { get; set; }
        public bool IsActive { get; set; }

        // Navigation properties
        public MetadataBook Book { get; set; }
        public MetadataAuthor Author { get; set; }

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

        public bool IsExpired(TimeSpan expirationTime)
        {
            return DateTime.UtcNow - RetrievedAt > expirationTime;
        }
    }
} 