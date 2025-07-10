# Readarr Quick Fix Guide

This guide will get your Readarr instance working again by replacing the broken metadata service with a robust multi-source system.

## 🚀 Quick Start (5 minutes)

### Option 1: Use rreading-glasses (Recommended - No Setup Required)

**This is now the default!** Readarr has been updated to use the rreading-glasses service (api.bookinfo.pro) as the default metadata source. Simply:

1. **Update Readarr** to the latest version
2. **No configuration needed** - it will work automatically
3. **Test it** by searching for a book or author

### Option 2: Use Local Metadata Service

If you prefer to use the local metadata service:

#### Step 1: Start the Metadata Service

Choose one of these options:

##### Option A: Docker (Recommended)
```bash
cd metadata_service
docker-compose up -d
```

##### Option B: Python Script
```bash
cd metadata_service
chmod +x start.sh
./start.sh
```

The service will be available at `http://localhost:8787`

#### Step 2: Configure Readarr

1. **Open Readarr** in your browser
2. **Navigate to Development Settings**:
   - Go to `http://your-readarr-instance/settings/development`
   - Or: Settings → General → Show Advanced → Development
3. **Set Metadata Provider Source**:
   - Find "Metadata Source" field
   - Enter: `http://localhost:8787`
   - Click "Save"

### Step 3: Test It

1. **Search for a book**: Try adding a new author or book
2. **Check import lists**: Your Goodreads import lists should work again
3. **Verify metadata**: New books should have proper titles, descriptions, and covers

## 🔧 Troubleshooting

### Service Won't Start

**Check Python installation:**
```bash
python3 --version  # Should be 3.7 or later
```

**Check port availability:**
```bash
netstat -an | grep 8787  # Should be empty if port is free
```

**View service logs:**
```bash
# If using Docker:
docker-compose logs readarr-metadata

# If using Python directly:
# Check the terminal where you ran the service
```

### Readarr Can't Connect

**Test the service manually:**
```bash
curl http://localhost:8787/health
# Should return: {"status": "healthy", ...}
```

**Check from Readarr's perspective:**
- If Readarr is in Docker, use `http://host.docker.internal:8787`
- If on different machines, use the actual IP address

**Verify configuration:**
- Go to Settings → Development in Readarr
- Ensure "Metadata Source" field is filled correctly
- Click "Test" if available

### Poor Search Results

**Try different search terms:**
- Use exact titles: "Harry Potter and the Philosopher's Stone"
- Include author: "Harry Potter J.K. Rowling"
- Try ISBN searches: "9780747532699"

**Check Goodreads directly:**
- Verify the book exists on Goodreads.com
- Try the same search on Goodreads website

### Rate Limiting Issues

The service limits requests to 10 per minute to be respectful to Goodreads.

**If you hit limits:**
- Wait a minute and try again
- Results are cached for 1 hour
- Consider using ISBN for exact matches

## 📊 What's Working Now

✅ **Book Search**: Find books by title, author, or ISBN  
✅ **Author Search**: Find authors and their books  
✅ **Metadata**: Titles, descriptions, ISBNs, publication dates  
✅ **Cover Images**: High-quality book covers  
✅ **Import Lists**: Goodreads shelves and lists  
✅ **Caching**: Reduced load on Goodreads  
✅ **Multi-Source Support**: rreading-glasses + local scraper  
✅ **Fallback System**: Automatic provider switching  
✅ **Confidence Scoring**: Better result quality  

## ⚠️ Current Limitations

- **Series Information**: Limited series detection
- **Author Biographies**: Basic author info only
- **Rate Limits**: 10 requests per minute (local service)
- **Goodreads Dependency**: Still relies on Goodreads being available

## 🔄 Alternative Solutions

If this solution doesn't work for you:

### Option 1: Use rreading-glasses (More comprehensive)
```
Metadata Provider Source: https://api.bookinfo.pro
```
*This is now the default and should work automatically*

### Option 2: Use Open Library (Free but limited)
```
Metadata Provider Source: https://openlibrary.org/api
```
*(Note: You'd need to implement Open Library support)*

## 🛠️ Advanced Configuration

### Change Service Port
Edit `metadata_service/app.py`:
```python
app.run(host='0.0.0.0', port=8888, debug=False)  # Change 8787 to 8888
```

### Increase Rate Limits
Edit `metadata_service/app.py`:
```python
rate_limiter = RateLimiter(max_requests=20, time_window=60)  # 20 requests/minute
```

### Add More Caching
The service caches results for 1 hour. To increase:
```python
app.config['CACHE_DEFAULT_TIMEOUT'] = 7200  # 2 hours
```

### Run as System Service

Create `/etc/systemd/system/readarr-metadata.service`:
```ini
[Unit]
Description=Readarr Metadata Service
After=network.target

[Service]
Type=simple
User=readarr
WorkingDirectory=/path/to/metadata_service
ExecStart=/path/to/metadata_service/venv/bin/python app.py
Restart=always

[Install]
WantedBy=multi-user.target
```

Then:
```bash
sudo systemctl enable readarr-metadata
sudo systemctl start readarr-metadata
```

## 📈 Monitoring

### Check Service Health
```bash
curl http://localhost:8787/health
```

### Test Search
```bash
curl "http://localhost:8787/search?q=Harry Potter"
```

### Monitor Logs
```bash
# Docker:
docker-compose logs -f readarr-metadata

# Python:
# Check terminal output where service is running
```

## 🔄 Updating

### Update the Service
```bash
cd metadata_service
git pull  # If you cloned from git
# Or download new files and replace

# Restart the service:
docker-compose restart  # For Docker
# Or Ctrl+C and restart for Python
```

### Backup Your Config
Before making changes, backup your Readarr database:
```bash
cp ~/.config/Readarr/readarr.db ~/.config/Readarr/readarr.db.backup
```

## 🆘 Getting Help

1. **Check the logs** for specific error messages
2. **Test the service** with the provided test script:
   ```bash
   cd metadata_service
   python test_service.py
   ```
3. **Verify network connectivity** between Readarr and the service
4. **Try the rreading-glasses alternative** if scraping issues persist

## 🎯 Success Criteria

You'll know it's working when:
- ✅ You can search for and add new books
- ✅ Import lists sync successfully  
- ✅ New books have proper metadata and covers
- ✅ No more "metadata source unavailable" errors
- ✅ Multiple metadata sources provide redundancy

## 🚀 What's New in This Version

### Enhanced Metadata System
- **Multi-Source Architecture**: Support for multiple metadata providers
- **Automatic Fallback**: If one provider fails, automatically tries others
- **Confidence Scoring**: Better result quality through intelligent ranking
- **rreading-glasses Integration**: Now the default metadata source
- **Improved Caching**: Better performance and reduced API calls

### Better Error Handling
- **Graceful Degradation**: System continues working even if some providers fail
- **Detailed Logging**: Better debugging information
- **Retry Logic**: Automatic retries with exponential backoff

This should get your Readarr instance back to full functionality with improved reliability and performance!