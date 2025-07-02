#!/usr/bin/env python3
"""
Test script for the Readarr Metadata Service
"""

import requests
import json
import time

BASE_URL = "http://localhost:8787"

def test_health():
    """Test the health endpoint"""
    print("Testing health endpoint...")
    try:
        response = requests.get(f"{BASE_URL}/health", timeout=10)
        if response.status_code == 200:
            print("✓ Health check passed")
            return True
        else:
            print(f"✗ Health check failed: {response.status_code}")
            return False
    except Exception as e:
        print(f"✗ Health check failed: {e}")
        return False

def test_search():
    """Test the search endpoint"""
    print("\nTesting search endpoint...")
    try:
        # Test with a popular book
        response = requests.get(f"{BASE_URL}/search?q=Harry Potter", timeout=30)
        if response.status_code == 200:
            data = response.json()
            if len(data) > 0:
                print(f"✓ Search returned {len(data)} results")
                print(f"  First result: {data[0].get('title', 'No title')} by {data[0].get('author', {}).get('name', 'Unknown')}")
                return data[0].get('workId')
            else:
                print("✗ Search returned no results")
                return None
        else:
            print(f"✗ Search failed: {response.status_code}")
            return None
    except Exception as e:
        print(f"✗ Search failed: {e}")
        return None

def test_book_details(book_id):
    """Test getting book details"""
    if not book_id:
        print("Skipping book details test (no book ID)")
        return
    
    print(f"\nTesting book details for ID {book_id}...")
    try:
        response = requests.get(f"{BASE_URL}/work/{book_id}", timeout=30)
        if response.status_code == 200:
            data = response.json()
            print("✓ Book details retrieved successfully")
            print(f"  Title: {data.get('title', 'No title')}")
            print(f"  Author: {data.get('author', {}).get('name', 'Unknown')}")
            print(f"  Description: {data.get('description', 'No description')[:100]}...")
            print(f"  ISBN: {data.get('isbn13', 'No ISBN')}")
            return True
        else:
            print(f"✗ Book details failed: {response.status_code}")
            return False
    except Exception as e:
        print(f"✗ Book details failed: {e}")
        return False

def test_service():
    """Run all tests"""
    print("=" * 50)
    print("Readarr Metadata Service Test")
    print("=" * 50)
    
    # Test health
    if not test_health():
        print("\n❌ Service is not running or not healthy")
        print("Make sure the service is started with: python app.py")
        return False
    
    # Test search
    book_id = test_search()
    
    # Test book details
    test_book_details(book_id)
    
    print("\n" + "=" * 50)
    print("Test completed!")
    print("If all tests passed, configure Readarr to use:")
    print(f"  Metadata Provider Source: {BASE_URL}")
    print("=" * 50)
    
    return True

if __name__ == "__main__":
    test_service()