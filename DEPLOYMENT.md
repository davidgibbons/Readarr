# Readarr Revival Project - Deployment Guide

## 🚀 Project Status: READY FOR DEPLOYMENT

The Readarr revival project has been successfully completed with all three phases implemented. This guide provides instructions for deploying the enhanced Readarr system.

## 📋 Prerequisites

### System Requirements
- **Operating System**: Windows 10+, Linux (Ubuntu 18.04+), macOS 10.15+
- **Memory**: Minimum 4GB RAM, Recommended 8GB+
- **Storage**: Minimum 10GB free space, Recommended 50GB+
- **Database**: PostgreSQL 12+ (included in deployment)
- **Network**: Internet connection for metadata sources

### Software Dependencies
- **.NET Core 6.0+**: Runtime for the main application
- **Docker** (Optional): For containerized deployment
- **Python 3.8+** (Optional): For local metadata service

## 🛠️ Installation Options

### Option 1: Docker Deployment (Recommended)

```bash
# Clone the repository
git clone https://github.com/your-org/readarr-revival.git
cd readarr-revival

# Start with Docker Compose
docker-compose up -d

# Access the application
# Web UI: http://localhost:8787
# API: http://localhost:8787/api/v1
```

### Option 2: Native Installation

```bash
# Clone the repository
git clone https://github.com/your-org/readarr-revival.git
cd readarr-revival

# Build the application
dotnet build src/Readarr.sln

# Run the application
dotnet run --project src/NzbDrone.Host/Readarr.Host.csproj

# Access the application
# Web UI: http://localhost:8787
# API: http://localhost:8787/api/v1
```

### Option 3: Windows Installer

1. Download the latest release from GitHub
2. Run the installer as administrator
3. Follow the setup wizard
4. Launch Readarr from the Start Menu

## ⚙️ Configuration

### Initial Setup

1. **First Run**: Navigate to `http://localhost:8787`
2. **Database Setup**: The application will automatically create and configure the PostgreSQL database
3. **Metadata Sources**: Configure your preferred metadata sources:
   - rreading-glasses (default, recommended)
   - Goodreads scraper
   - Open Library
   - Google Books
   - Local metadata service

### Metadata Provider Configuration

```json
{
  "metadataProviders": [
    {
      "name": "rreading-glasses",
      "url": "https://api.bookinfo.pro",
      "priority": 1,
      "enabled": true
    },
    {
      "name": "goodreads-scraper",
      "url": "http://localhost:8787",
      "priority": 2,
      "enabled": true
    }
  ]
}
```

### Advanced Features Configuration

#### Real-time Updates
```json
{
  "realTimeUpdates": {
    "enabled": true,
    "webhookPort": 8788,
    "publisherIntegration": true,
    "autoRefresh": true
  }
}
```

#### Community Features
```json
{
  "communityFeatures": {
    "enabled": true,
    "requireModeration": true,
    "autoApproveTrustedUsers": true,
    "reputationThreshold": 100
  }
}
```

#### Analytics
```json
{
  "analytics": {
    "enabled": true,
    "dataRetentionDays": 90,
    "abTesting": true,
    "performanceMonitoring": true
  }
}
```

## 🔧 Database Setup

### Automatic Setup (Recommended)
The application automatically handles database setup and migrations.

### Manual Setup (Advanced)
```sql
-- Create database
CREATE DATABASE readarr_revival;

-- Run migrations
-- The application will automatically apply all migrations
```

### Database Maintenance
```bash
# Backup database
pg_dump readarr_revival > backup_$(date +%Y%m%d).sql

# Restore database
psql readarr_revival < backup_20240115.sql
```

## 📊 Monitoring and Analytics

### System Health Dashboard
- Access: `http://localhost:8787/health`
- Metrics: Response times, error rates, uptime
- Alerts: Email notifications for critical issues

### Analytics Dashboard
- Access: `http://localhost:8787/analytics`
- Features: Usage analytics, metadata quality metrics
- A/B Testing: Performance comparison and recommendations

### Logs
```bash
# Application logs
tail -f logs/readarr.log

# Database logs
tail -f /var/log/postgresql/postgresql-*.log

# Docker logs (if using Docker)
docker logs readarr-revival
```

## 🔒 Security Considerations

### Network Security
- Use HTTPS in production
- Configure firewall rules
- Enable authentication for admin access

### Data Protection
- Regular database backups
- Encrypt sensitive configuration data
- Monitor access logs

### Rate Limiting
- Configure rate limits for external APIs
- Monitor for abuse patterns
- Implement IP blocking for malicious requests

## 🚀 Performance Optimization

### Caching Strategy
- Database query optimization
- Redis caching for frequently accessed data
- CDN for static assets

### Scaling
- Horizontal scaling with load balancers
- Database read replicas
- Microservices architecture (future)

### Monitoring
- Set up monitoring with Prometheus/Grafana
- Configure alerting for performance issues
- Regular performance audits

## 🔄 Updates and Maintenance

### Automatic Updates
```bash
# Pull latest changes
git pull origin main

# Rebuild and restart
docker-compose down
docker-compose up -d --build
```

### Manual Updates
1. Stop the application
2. Backup the database
3. Update the code
4. Run database migrations
5. Restart the application

### Backup Strategy
```bash
#!/bin/bash
# Daily backup script
DATE=$(date +%Y%m%d)
pg_dump readarr_revival > /backups/readarr_$DATE.sql
tar -czf /backups/config_$DATE.tar.gz /opt/readarr/config
```

## 🆘 Troubleshooting

### Common Issues

#### Database Connection Issues
```bash
# Check database status
sudo systemctl status postgresql

# Test connection
psql -h localhost -U readarr -d readarr_revival
```

#### Metadata Source Issues
```bash
# Test metadata sources
curl -X GET "http://localhost:8787/api/v1/metadata/test"

# Check provider status
curl -X GET "http://localhost:8787/api/v1/metadata/providers"
```

#### Performance Issues
```bash
# Check system resources
htop
df -h
free -h

# Check application logs
tail -f logs/readarr.log | grep ERROR
```

### Support Resources
- **Documentation**: [Wiki](https://github.com/your-org/readarr-revival/wiki)
- **Issues**: [GitHub Issues](https://github.com/your-org/readarr-revival/issues)
- **Discord**: [Community Support](https://discord.gg/readarr-revival)

## 📈 Migration from Original Readarr

### Data Migration
1. Export your existing Readarr database
2. Run the migration script
3. Verify data integrity
4. Update configuration

### Configuration Migration
```bash
# Backup original config
cp -r /opt/readarr/config /opt/readarr/config.backup

# Update configuration files
# The application will guide you through the process
```

## 🎯 Success Metrics

After deployment, verify these metrics:

- [ ] Application starts successfully
- [ ] Database migrations complete
- [ ] Metadata sources are accessible
- [ ] Search functionality works
- [ ] Import lists function properly
- [ ] Real-time updates are working
- [ ] Community features are enabled
- [ ] Analytics are collecting data

## 📞 Support

For deployment assistance:
- **Email**: support@readarr-revival.org
- **Discord**: [Join our community](https://discord.gg/readarr-revival)
- **Documentation**: [Complete guide](https://github.com/your-org/readarr-revival/wiki)

---

**Congratulations!** You've successfully deployed the enhanced Readarr revival system. Enjoy the improved metadata management experience! 🎉 