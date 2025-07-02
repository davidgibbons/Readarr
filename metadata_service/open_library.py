#!/usr/bin/env python3
"""
Open Library metadata provider as fallback for Goodreads
"""

import requests
import re
from typing import Dict, List, Optional
import logging

logger = logging.getLogger(__name__)

class OpenLibraryScraper:
    def __init__(self):
        self.base_url = "https://openlibrary.org"
        self.session = requests.Session()
        self.session.headers.update({
            'User-Agent': 'Readarr-Metadata-Service/1.0'
        })
    
    def search_books(self, query: str, limit: int = 20) -> List[Dict]:
        """Search for books on Open Library"""
        try:
            url = f"{self.base_url}/search.json"
            params = {
                'q': query,
                'limit': limit,
                'fields': 'key,title,author_name,first_publish_year,isbn,cover_i,ratings_average'
            }
            
            response = self.session.get(url, params=params, timeout=10)
            response.raise_for_status()
            
            data = response.json()
            results = []
            
            for doc in data.get('docs', []):
                try:
                    book_data = self._parse_search_result(doc)
                    if book_data:
                        results.append(book_data)
                except Exception as e:
                    logger.warning(f"Error parsing Open Library search result: {e}")
                    continue
            
            return results
            
        except Exception as e:
            logger.error(f"Error searching Open Library: {e}")
            return []
    
    def _parse_search_result(self, doc: Dict) -> Optional[Dict]:
        """Parse Open Library search result"""
        try:
            # Extract work ID from key
            key = doc.get('key', '')
            work_id_match = re.search(r'/works/OL(\d+)W', key)
            if not work_id_match:
                return None
            
            work_id = int(work_id_match.group(1))
            
            title = doc.get('title', 'Unknown Title')
            authors = doc.get('author_name', ['Unknown Author'])
            author_name = authors[0] if authors else 'Unknown Author'
            
            # Get publication year
            pub_year = doc.get('first_publish_year')
            
            # Get rating
            rating = doc.get('ratings_average', 0.0)
            
            return {
                'workId': work_id,
                'bookId': work_id,
                'title': title,
                'author': {
                    'id': 0,
                    'name': author_name
                },
                'rating': rating,
                'publicationYear': pub_year,
                'url': f"https://openlibrary.org{key}"
            }
            
        except Exception as e:
            logger.warning(f"Error parsing Open Library result: {e}")
            return None
    
    def get_book_details(self, book_id: str) -> Optional[Dict]:
        """Get detailed book information from Open Library"""
        try:
            # Try to get work details
            work_url = f"{self.base_url}/works/OL{book_id}W.json"
            response = self.session.get(work_url, timeout=10)
            response.raise_for_status()
            
            work_data = response.json()
            
            # Get basic info
            title = work_data.get('title', 'Unknown Title')
            description = self._extract_description(work_data)
            
            # Try to get author info
            authors = work_data.get('authors', [])
            author_name = 'Unknown Author'
            if authors:
                author_key = authors[0].get('author', {}).get('key', '')
                if author_key:
                    author_name = self._get_author_name(author_key)
            
            # Try to get edition info for ISBN
            isbn = None
            editions_url = f"{self.base_url}/works/OL{book_id}W/editions.json"
            try:
                ed_response = self.session.get(editions_url, timeout=5)
                if ed_response.status_code == 200:
                    ed_data = ed_response.json()
                    entries = ed_data.get('entries', [])
                    for entry in entries:
                        isbn_13 = entry.get('isbn_13', [])
                        if isbn_13:
                            isbn = isbn_13[0]
                            break
                        isbn_10 = entry.get('isbn_10', [])
                        if isbn_10:
                            isbn = isbn_10[0]
                            break
            except:
                pass
            
            return {
                'id': int(book_id),
                'title': title,
                'description': description,
                'isbn13': isbn,
                'publicationDate': None,  # Would need more work to extract
                'rating': 0.0,
                'coverUrl': f"https://covers.openlibrary.org/w/olid/OL{book_id}W-L.jpg",
                'author': {
                    'id': 0,
                    'name': author_name
                },
                'url': f"https://openlibrary.org/works/OL{book_id}W"
            }
            
        except Exception as e:
            logger.error(f"Error getting Open Library book details for {book_id}: {e}")
            return None
    
    def _extract_description(self, work_data: Dict) -> str:
        """Extract description from work data"""
        description = work_data.get('description')
        if isinstance(description, dict):
            return description.get('value', '')
        elif isinstance(description, str):
            return description
        return ''
    
    def _get_author_name(self, author_key: str) -> str:
        """Get author name from author key"""
        try:
            author_url = f"{self.base_url}{author_key}.json"
            response = self.session.get(author_url, timeout=5)
            if response.status_code == 200:
                author_data = response.json()
                return author_data.get('name', 'Unknown Author')
        except:
            pass
        return 'Unknown Author'