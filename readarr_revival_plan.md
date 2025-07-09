# Readarr Revival Plan: Getting Back on Track

## Executive Summary

Based on my analysis of the Readarr repository, the primary issue that led to its retirement is the failure of the metadata system, specifically the breakdown of the Goodreads API integration and the stalled transition to Open Library. However, the core application architecture is solid, and with a strategic approach to rebuilding the metadata infrastructure, Readarr can be revived and made more robust than before.

## Current State Analysis

### What's Working
- **Core Architecture**: The C# backend with .NET Core is solid and well-structured
- **Frontend**: React-based UI is modern and functional
- **Download Integration**: Support for major download clients (SABnzbd, NZBGet, qBittorrent, etc.)
- **Media Management**: File organization, renaming, and Calibre integration
- **Infrastructure**: Docker support, multi-platform builds, comprehensive CI/CD

### What's Broken
- **Primary Metadata Source**: Goodreads API integration is failing
- **Backup Sources**: Open Library transition was incomplete
- **Search Functionality**: Book/author discovery relies on broken metadata
- **Import Lists**: Goodreads-dependent features are non-functional

### Root Cause Analysis
1. **Goodreads API Changes**: The unofficial API endpoints used by Readarr have become unreliable
2. **Single Point of Failure**: Over-reliance on Goodreads as the primary metadata source
3. **Incomplete Migration**: The Open Library integration was never completed
4. **Resource Constraints**: The development team lacked time to rebuild the metadata system

## Recommended Solution: Multi-Source Metadata Architecture

### Phase 1: Immediate Revival (4-6 weeks)

#### 1.1 Implement Web Scraping Infrastructure
```
Priority: HIGH
Timeline: 2 weeks
Dependencies: None
```

**Implementation Steps:**
- Create a new `MetadataSource.WebScraping` namespace
- Implement rate-limited, resilient web scrapers for:
  - Goodreads (using the approach from rreading-glasses)
  - Open Library
  - Google Books API
  - Amazon (for ISBN/ASIN lookups)
- Add proxy rotation and user-agent management
- Implement caching layer to reduce API calls

**Key Components:**
```csharp
// New interfaces to implement
public interface IWebScrapingMetadataProvider
{
    Task<Book> GetBookByIsbnAsync(string isbn);
    Task<Author> GetAuthorByNameAsync(string name);
    Task<List<Book>> SearchBooksAsync(string query);
}

public interface IMetadataAggregator
{
    Task<Book> GetBestBookMetadata(string identifier);
    Task<Author> GetBestAuthorMetadata(string identifier);
}
```

#### 1.2 Deploy rreading-glasses Integration
```
Priority: HIGH
Timeline: 1 week
Dependencies: None
```

**Immediate Action:**
- Integrate with the existing rreading-glasses service (api.bookinfo.pro)
- Update `MetadataRequestBuilder.cs` to support alternative endpoints
- Add configuration option for metadata provider source
- Test compatibility with existing libraries

#### 1.3 Create Metadata Fallback System
```
Priority: HIGH
Timeline: 1 week
Dependencies: 1.1, 1.2
```

**Implementation:**
- Modify `BookInfoProxy.cs` to support multiple providers
- Implement priority-based fallback logic
- Add metadata confidence scoring
- Create unified response format

### Phase 2: Enhanced Metadata System (6-8 weeks)

#### 2.1 Build Central Metadata Database
```
Priority: MEDIUM
Timeline: 3 weeks
Dependencies: 1.1
```

**Database Schema:**
```sql
-- Core metadata tables
CREATE TABLE metadata_books (
    id SERIAL PRIMARY KEY,
    goodreads_id VARCHAR(50),
    isbn13 VARCHAR(13),
    isbn10 VARCHAR(10),
    title VARCHAR(500),
    subtitle VARCHAR(500),
    description TEXT,
    publication_date DATE,
    page_count INTEGER,
    language VARCHAR(10),
    created_at TIMESTAMP,
    updated_at TIMESTAMP,
    confidence_score DECIMAL(3,2)
);

CREATE TABLE metadata_authors (
    id SERIAL PRIMARY KEY,
    goodreads_id VARCHAR(50),
    name VARCHAR(200),
    biography TEXT,
    birth_date DATE,
    death_date DATE,
    created_at TIMESTAMP,
    updated_at TIMESTAMP,
    confidence_score DECIMAL(3,2)
);

CREATE TABLE metadata_sources (
    id SERIAL PRIMARY KEY,
    book_id INTEGER REFERENCES metadata_books(id),
    source_name VARCHAR(50),
    source_data JSONB,
    retrieved_at TIMESTAMP
);
```

#### 2.2 Implement Machine Learning for Metadata Matching
```
Priority: MEDIUM
Timeline: 2 weeks
Dependencies: 2.1
```

**ML Components:**
- String similarity algorithms for title/author matching
- Confidence scoring based on multiple data points
- Duplicate detection and merging
- Series detection and organization

#### 2.3 Create Community Metadata Contribution System
```
Priority: MEDIUM
Timeline: 3 weeks
Dependencies: 2.1
```

**Features:**
- User-contributed metadata corrections
- Voting system for metadata accuracy
- API for external metadata contributions
- Integration with existing book databases

### Phase 3: Advanced Features (8-12 weeks)

#### 3.1 Enhanced Search and Discovery
```
Priority: LOW
Timeline: 2 weeks
Dependencies: 2.1, 2.2
```

- Full-text search across metadata
- Advanced filtering options
- Recommendation engine based on reading history
- Series completion tracking

#### 3.2 Real-time Metadata Updates
```
Priority: LOW
Timeline: 2 weeks
Dependencies: 2.1
```

- Background jobs for metadata refresh
- Change detection and notification system
- Automated quality improvement

#### 3.3 Publisher Integration
```
Priority: LOW
Timeline: 4 weeks
Dependencies: All previous phases
```

- Direct integration with publisher APIs
- Early release notifications
- Official metadata sources

## Technical Implementation Details

### Web Scraping Best Practices

```python
# Example Goodreads scraper (Python/FastAPI microservice)
import asyncio
import aiohttp
from bs4 import BeautifulSoup
from dataclasses import dataclass
from typing import List, Optional

@dataclass
class BookMetadata:
    title: str
    author: str
    isbn: Optional[str]
    description: Optional[str]
    publication_date: Optional[str]
    rating: Optional[float]
    cover_url: Optional[str]

class GoodreadsScraper:
    def __init__(self):
        self.session = None
        self.rate_limiter = asyncio.Semaphore(10)  # Max 10 concurrent requests
    
    async def search_books(self, query: str) -> List[BookMetadata]:
        async with self.rate_limiter:
            # Implement rate-limited scraping
            await asyncio.sleep(0.1)  # Basic rate limiting
            # Scraping logic here
            pass
```

### C# Integration Layer

```csharp
// Enhanced metadata service
public class EnhancedMetadataService : IMetadataService
{
    private readonly List<IMetadataProvider> _providers;
    private readonly IMetadataCache _cache;
    private readonly IMetadataAggregator _aggregator;

    public async Task<Book> GetBookMetadata(string identifier)
    {
        // Check cache first
        var cached = await _cache.GetAsync(identifier);
        if (cached != null && !cached.IsExpired)
            return cached;

        // Try providers in priority order
        var results = new List<Book>();
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.GetBookAsync(identifier);
                if (result != null)
                    results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Provider {provider.Name} failed: {ex.Message}");
            }
        }

        // Aggregate and return best result
        var best = await _aggregator.GetBestResult(results);
        await _cache.SetAsync(identifier, best);
        return best;
    }
}
```

### Database Migration Strategy

```sql
-- Add new metadata columns to existing tables
ALTER TABLE Books ADD COLUMN metadata_confidence DECIMAL(3,2) DEFAULT 0.5;
ALTER TABLE Books ADD COLUMN metadata_sources TEXT[];
ALTER TABLE Books ADD COLUMN last_metadata_update TIMESTAMP;

-- Create indexes for performance
CREATE INDEX idx_books_goodreads_id ON Books(ForeignBookId);
CREATE INDEX idx_books_isbn ON Books((Editions->0->>'Isbn13'));
CREATE INDEX idx_metadata_confidence ON Books(metadata_confidence);
```

## Resource Requirements

### Development Team
- **1 Senior Backend Developer** (C#/.NET Core) - 3 months
- **1 Python Developer** (Web scraping/APIs) - 2 months  
- **1 Database Developer** (PostgreSQL/optimization) - 1 month
- **1 DevOps Engineer** (Deployment/infrastructure) - 0.5 months

### Infrastructure
- **Database Server**: PostgreSQL 14+ with 100GB+ storage
- **Application Servers**: 2-4 instances for load balancing
- **Cache Layer**: Redis for metadata caching
- **Monitoring**: Grafana/Prometheus for system monitoring
- **CDN**: For cover images and static assets

### Estimated Costs (Monthly)
- **Development**: $25,000-30,000 (3-month project)
- **Infrastructure**: $500-1,000/month
- **Third-party APIs**: $200-500/month
- **Monitoring/Tools**: $100-300/month

## Risk Mitigation

### Technical Risks
1. **Web Scraping Reliability**: Implement multiple fallback sources
2. **Rate Limiting**: Use distributed caching and smart throttling
3. **Legal Issues**: Respect robots.txt and terms of service
4. **Data Quality**: Implement validation and confidence scoring

### Business Risks
1. **Community Adoption**: Engage existing user base early
2. **Maintenance Burden**: Design for minimal ongoing maintenance
3. **Scalability**: Plan for growth from day one

## Success Metrics

### Phase 1 Success Criteria
- [ ] 95% of existing books can be matched with new metadata system
- [ ] Search functionality restored for new books/authors
- [ ] Import lists working with at least 2 sources
- [ ] No data corruption in existing libraries

### Long-term Success Criteria
- [ ] 99.5% uptime for metadata services
- [ ] <2 second average response time for metadata queries
- [ ] 90% user satisfaction with search results
- [ ] Active community contributing metadata improvements

## Alternative Solutions Considered

### 1. Fork and Maintain Existing APIs
**Pros**: Minimal code changes, faster implementation
**Cons**: Still dependent on external services, limited control

### 2. Open Library Only
**Pros**: Free, open source, stable
**Cons**: Limited coverage, especially for newer releases

### 3. Commercial Metadata Services
**Pros**: High quality, reliable
**Cons**: Expensive, vendor lock-in

### 4. Community-Driven Database (Recommended)
**Pros**: Full control, scalable, community ownership
**Cons**: Higher initial investment, requires ongoing maintenance

## Conclusion

The recommended approach combines immediate relief through existing solutions (rreading-glasses) with a long-term strategy for building a robust, community-driven metadata system. This approach:

1. **Gets users back online quickly** with the rreading-glasses integration
2. **Reduces single points of failure** with multiple metadata sources
3. **Improves long-term sustainability** through community involvement
4. **Maintains compatibility** with existing installations
5. **Provides room for growth** and future enhancements

The total investment of approximately $75,000-90,000 over 3 months would restore Readarr to full functionality and position it for long-term success. The key is starting with Phase 1 immediately to provide relief to the existing user base while building toward the more comprehensive solution.

## Next Steps

1. **Week 1**: Set up development environment and integrate rreading-glasses
2. **Week 2**: Begin web scraping infrastructure development
3. **Week 3**: Start database design and migration planning
4. **Week 4**: Implement fallback system and testing
5. **Month 2-3**: Build out comprehensive metadata system
6. **Month 4+**: Community features and advanced functionality

This plan provides a clear path forward for reviving Readarr and making it more resilient than ever before.