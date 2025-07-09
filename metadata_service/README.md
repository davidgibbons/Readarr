# Readarr Metadata Service

A lightweight Python service that scrapes Goodreads to provide metadata for Readarr, replacing the broken BookInfo API.

## Quick Start

### Option 1: Docker (Recommended)

1. **Build and run with Docker Compose:**
   ```bash
   cd metadata_service
   docker-compose up -d
   ```

2. **Configure Readarr:**
   - Go to `http://your-readarr-instance/settings/development`
   - Set `Metadata Provider Source` to: `http://localhost:8787`
   - Click Save

### Option 2: Python Virtual Environment

1. **Install dependencies:**
   ```bash
   cd metadata_service
   python3 -m venv venv
   source venv/bin/activate  # On Windows: venv\Scripts\activate
   pip install -r requirements.txt
   ```

2. **Run the service:**
   ```bash
   python app.py
   ```

3. **Configure Readarr** (same as above)

## Features

- **Goodreads scraping**: Searches and retrieves book metadata from Goodreads
- **Rate limiting**: Respects Goodreads with built-in rate limiting (10 requests/minute)
- **Caching**: 1-hour cache to reduce repeated requests
- **Readarr compatible**: Drop-in replacement for BookInfo API
- **Lightweight**: Minimal resource usage, runs on a Raspberry Pi

## API Endpoints

- `GET /search?q=<query>` - Search for books
- `GET /work/<id>` - Get book details by work ID
- `GET /book/<id>` - Get book details by book ID  
- `GET /author/<id>` - Get author details (basic implementation)
- `GET /health` - Health check

## Configuration

The service runs on port 8787 by default. You can change this by modifying the `app.run()` call in `app.py`.

## Troubleshooting

### Service won't start
- Check that port 8787 is available
- Ensure all dependencies are installed
- Check logs for specific error messages

### Readarr can't connect
- Verify the service is running: `curl http://localhost:8787/health`
- Check firewall settings
- Ensure Readarr can reach the service (try from Readarr's container/host)

### Search results are poor
- The service scrapes Goodreads search results, so quality depends on Goodreads
- Try more specific search terms
- Consider using ISBN searches for exact matches

### Rate limiting issues
- The service limits to 10 requests per minute to be respectful to Goodreads
- Cached results don't count against rate limits
- If you need higher throughput, consider running multiple instances with a load balancer

## Limitations

- **Author pages**: Currently returns basic author info only
- **Series detection**: Limited series information compared to full BookInfo
- **Rate limits**: Intentionally conservative to avoid being blocked
- **Goodreads dependency**: Still dependent on Goodreads being available

## Extending the Service

The service is designed to be easily extended:

1. **Add more scrapers**: Implement additional sources (Open Library, Google Books, etc.)
2. **Improve caching**: Add persistent database caching
3. **Enhanced parsing**: Better extraction of metadata fields
4. **Fallback sources**: Implement multiple source fallbacks

## Contributing

This is a simple solution focused on getting Readarr working again. Feel free to:

- Report issues with specific books/authors that don't work
- Suggest improvements to the scraping logic
- Add support for additional metadata sources
- Improve error handling and logging

## Legal Notes

This service scrapes publicly available data from Goodreads for personal use. Please:

- Use responsibly and respect rate limits
- Don't use for commercial purposes
- Consider the terms of service of scraped sites
- Use caching to minimize requests

## Alternative Solutions

If this service doesn't meet your needs, consider:

- **rreading-glasses**: More comprehensive solution with better Goodreads integration
- **Open Library API**: Free but limited coverage
- **Google Books API**: Good coverage but requires API key
- **Manual metadata**: Import from Calibre or other sources