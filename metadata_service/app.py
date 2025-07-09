#!/usr/bin/env python3
"""
Simple Readarr Metadata Service
A lightweight replacement for the broken BookInfo API that scrapes Goodreads
"""

import asyncio
import json
import re
import time
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Any
from urllib.parse import quote, urljoin

import aiohttp
import requests
from bs4 import BeautifulSoup
from flask import Flask, jsonify, request
from flask_caching import Cache
import sqlite3
import logging
from open_library import OpenLibraryScraper

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)
app.config['CACHE_TYPE'] = 'simple'
app.config['CACHE_DEFAULT_TIMEOUT'] = 3600  # 1 hour cache
cache = Cache(app)

# Rate limiting
class RateLimiter:
    def __init__(self, max_requests: int = 10, time_window: int = 60):
        self.max_requests = max_requests
        self.time_window = time_window
        self.requests = []
    
    def can_make_request(self) -> bool:
        now = time.time()
        # Remove old requests
        self.requests = [req_time for req_time in self.requests if now - req_time < self.time_window]
        
        if len(self.requests) < self.max_requests:
            self.requests.append(now)
            return True
        return False
    
    def wait_time(self) -> float:
        if not self.requests:
            return 0
        oldest_request = min(self.requests)
        return max(0, self.time_window - (time.time() - oldest_request))

rate_limiter = RateLimiter(max_requests=10, time_window=60)

class GoodreadsScraper:
    def __init__(self):
        self.session = requests.Session()
        self.session.headers.update({
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36'
        })
        
    def _wait_for_rate_limit(self):
        """Wait if we've hit rate limits"""
        if not rate_limiter.can_make_request():
            wait_time = rate_limiter.wait_time()
            if wait_time > 0:
                logger.info(f"Rate limited, waiting {wait_time:.2f} seconds")
                time.sleep(wait_time)
    
    def search_books(self, query: str, limit: int = 20) -> List[Dict]:
        """Search for books on Goodreads"""
        self._wait_for_rate_limit()
        
        try:
            url = f"https://www.goodreads.com/search?q={quote(query)}"
            response = self.session.get(url, timeout=10)
            response.raise_for_status()
            
            soup = BeautifulSoup(response.content, 'html.parser')
            results = []
            
            # Find book results
            book_rows = soup.find_all('tr', {'itemtype': 'http://schema.org/Book'})[:limit]
            
            for row in book_rows:
                try:
                    book_data = self._parse_search_result(row)
                    if book_data:
                        results.append(book_data)
                except Exception as e:
                    logger.warning(f"Error parsing search result: {e}")
                    continue
            
            return results
            
        except Exception as e:
            logger.error(f"Error searching Goodreads: {e}")
            return []
    
    def _parse_search_result(self, row) -> Optional[Dict]:
        """Parse a single search result row"""
        try:
            # Extract book ID from link
            link_elem = row.find('a', class_='bookTitle')
            if not link_elem:
                return None
            
            book_url = link_elem.get('href', '')
            book_id_match = re.search(r'/book/show/(\d+)', book_url)
            if not book_id_match:
                return None
            
            book_id = book_id_match.group(1)
            title = link_elem.get_text(strip=True)
            
            # Extract author
            author_elem = row.find('a', class_='authorName')
            author = author_elem.get_text(strip=True) if author_elem else "Unknown"
            
            # Extract rating
            rating_elem = row.find('span', class_='minirating')
            rating = 0.0
            if rating_elem:
                rating_text = rating_elem.get_text()
                rating_match = re.search(r'(\d+\.\d+)', rating_text)
                if rating_match:
                    rating = float(rating_match.group(1))
            
            # Extract publication year
            pub_year = None
            pub_elem = row.find('span', class_='greyText')
            if pub_elem:
                pub_text = pub_elem.get_text()
                year_match = re.search(r'(\d{4})', pub_text)
                if year_match:
                    pub_year = int(year_match.group(1))
            
            return {
                'workId': int(book_id),
                'bookId': int(book_id),
                'title': title,
                'author': {
                    'id': 0,  # We'll need to get this separately
                    'name': author
                },
                'rating': rating,
                'publicationYear': pub_year,
                'url': f"https://www.goodreads.com{book_url}"
            }
            
        except Exception as e:
            logger.warning(f"Error parsing search result: {e}")
            return None
    
    def get_book_details(self, book_id: str) -> Optional[Dict]:
        """Get detailed book information"""
        self._wait_for_rate_limit()
        
        try:
            url = f"https://www.goodreads.com/book/show/{book_id}"
            response = self.session.get(url, timeout=10)
            response.raise_for_status()
            
            soup = BeautifulSoup(response.content, 'html.parser')
            return self._parse_book_details(soup, book_id)
            
        except Exception as e:
            logger.error(f"Error getting book details for {book_id}: {e}")
            return None
    
    def _parse_book_details(self, soup: BeautifulSoup, book_id: str) -> Dict:
        """Parse detailed book information from Goodreads page"""
        try:
            # Title
            title_elem = soup.find('h1', {'data-testid': 'bookTitle'}) or soup.find('h1', class_='gr-h1')
            title = title_elem.get_text(strip=True) if title_elem else "Unknown Title"
            
            # Author
            author_elem = soup.find('span', {'data-testid': 'name'}) or soup.find('a', class_='authorName')
            author_name = author_elem.get_text(strip=True) if author_elem else "Unknown Author"
            
            # Description
            desc_elem = soup.find('div', {'data-testid': 'description'}) or soup.find('div', id='description')
            description = ""
            if desc_elem and hasattr(desc_elem, 'find_all'):
                # Remove "...more" links and clean up
                for more_link in desc_elem.find_all('a', string=re.compile(r'\.\.\.more')):
                    more_link.decompose()
                description = desc_elem.get_text(strip=True)
            
            # ISBN
            isbn = None
            isbn_elem = soup.find('span', string=re.compile(r'ISBN'))
            if isbn_elem and isbn_elem.parent:
                isbn_text = isbn_elem.parent.get_text()
                isbn_match = re.search(r'(\d{10,13})', isbn_text)
                if isbn_match:
                    isbn = isbn_match.group(1)
            
            # Publication date
            pub_date = None
            pub_elem = soup.find('p', {'data-testid': 'publicationInfo'})
            if pub_elem:
                pub_text = pub_elem.get_text()
                # Try to extract date
                date_match = re.search(r'(\w+ \d{1,2}, \d{4})', pub_text)
                if date_match:
                    try:
                        pub_date = datetime.strptime(date_match.group(1), '%B %d, %Y').isoformat()
                    except:
                        pass
            
            # Rating
            rating = 0.0
            rating_elem = soup.find('div', class_='RatingStatistics__rating')
            if rating_elem:
                rating_text = rating_elem.get_text()
                rating_match = re.search(r'(\d+\.\d+)', rating_text)
                if rating_match:
                    rating = float(rating_match.group(1))
            
            # Cover image
            cover_url = None
            img_elem = soup.find('img', {'data-testid': 'coverImage'}) or soup.find('img', id='coverImage')
            if img_elem and hasattr(img_elem, 'get'):
                cover_url = img_elem.get('src')
            
            return {
                'id': int(book_id),
                'title': title,
                'description': description,
                'isbn13': isbn,
                'publicationDate': pub_date,
                'rating': rating,
                'coverUrl': cover_url,
                'author': {
                    'id': 0,
                    'name': author_name
                },
                'url': f"https://www.goodreads.com/book/show/{book_id}"
            }
            
        except Exception as e:
            logger.error(f"Error parsing book details: {e}")
            return {
                'id': int(book_id),
                'title': "Unknown Title",
                'description': "",
                'author': {'id': 0, 'name': "Unknown Author"}
            }

# Initialize scrapers
scraper = GoodreadsScraper()
ol_scraper = OpenLibraryScraper()

# Readarr API endpoints
@app.route('/author/<int:author_id>')
@cache.cached(timeout=3600)
def get_author(author_id):
    """Get author information - simplified for now"""
    # For now, return a basic structure
    # In a full implementation, we'd scrape author pages
    return jsonify({
        'id': author_id,
        'name': f'Author {author_id}',
        'books': []
    })

@app.route('/work/<int:work_id>')
@cache.cached(timeout=3600)
def get_work(work_id):
    """Get work (book) information"""
    try:
        book_data = scraper.get_book_details(str(work_id))
        if not book_data:
            return jsonify({'error': 'Book not found'}), 404
        
        # Convert to Readarr format
        readarr_format = {
            'id': book_data['id'],
            'title': book_data['title'],
            'description': book_data.get('description', ''),
            'isbn13': book_data.get('isbn13'),
            'publicationDate': book_data.get('publicationDate'),
            'rating': book_data.get('rating', 0),
            'coverUrl': book_data.get('coverUrl'),
            'author': book_data['author'],
            'editions': [{
                'id': book_data['id'],
                'title': book_data['title'],
                'isbn13': book_data.get('isbn13'),
                'format': 'Unknown',
                'monitored': True
            }]
        }
        
        return jsonify(readarr_format)
        
    except Exception as e:
        logger.error(f"Error getting work {work_id}: {e}")
        return jsonify({'error': 'Internal server error'}), 500

@app.route('/book/<int:book_id>')
@cache.cached(timeout=3600)
def get_book(book_id):
    """Get book information - redirect to work for now"""
    return get_work(book_id)

@app.route('/search')
def search():
    """Search for books"""
    query = request.args.get('q', '')
    if not query:
        return jsonify({'error': 'Query parameter required'}), 400
    
    try:
        # Try Goodreads first
        results = scraper.search_books(query, limit=20)
        
        # If no results from Goodreads, try Open Library
        if not results:
            logger.info(f"No Goodreads results for '{query}', trying Open Library")
            ol_results = ol_scraper.search_books(query, limit=10)
            results.extend(ol_results)
        
        return jsonify(results)
        
    except Exception as e:
        logger.error(f"Error searching for '{query}': {e}")
        return jsonify({'error': 'Search failed'}), 500

@app.route('/health')
def health():
    """Health check endpoint"""
    return jsonify({
        'status': 'healthy',
        'timestamp': datetime.now().isoformat(),
        'service': 'readarr-metadata-service'
    })

@app.route('/')
def index():
    """Basic info endpoint"""
    return jsonify({
        'service': 'Readarr Metadata Service',
        'version': '1.0.0',
        'description': 'Simple Goodreads scraping service for Readarr',
        'endpoints': [
            '/author/<id>',
            '/work/<id>',
            '/book/<id>',
            '/search?q=<query>',
            '/health'
        ]
    })

if __name__ == '__main__':
    print("Starting Readarr Metadata Service...")
    print("This service provides Goodreads metadata for Readarr")
    print("Configure Readarr to use: http://localhost:8787")
    app.run(host='0.0.0.0', port=8787, debug=False)