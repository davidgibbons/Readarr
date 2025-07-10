# 🚀 Installing Readarr Revival on TrueNAS SCALE

## 📋 Prerequisites

Before installing Readarr Revival on TrueNAS SCALE, ensure you have:

- **TrueNAS SCALE** installed and running (version 22.12 or later recommended)
- **Apps** feature enabled in TrueNAS SCALE
- **Pool** with sufficient storage (minimum 50GB recommended)
- **Network access** for downloading Docker images
- **SSH access** to TrueNAS (for advanced configuration)

## 🛠️ Installation Methods

### **Method 1: Using TrueNAS SCALE Apps (Recommended)**

#### **Step 1: Prepare the Application**
1. **Download the configuration files**:
   ```bash
   # SSH into your TrueNAS SCALE system
   ssh root@your-truenas-ip
   
   # Create directory for Readarr Revival
   mkdir -p /mnt/pool/apps/readarr-revival
   cd /mnt/pool/apps/readarr-revival
   
   # Download configuration files
   wget https://raw.githubusercontent.com/your-org/readarr-revival/main/truenas-scale/readarr-revival.yaml
   wget https://raw.githubusercontent.com/your-org/readarr-revival/main/truenas-scale/docker-compose.yml
   ```

#### **Step 2: Create Custom App**
1. **Access TrueNAS Web UI**
2. **Navigate to Apps** → **Available Applications**
3. **Click "Custom App"**
4. **Fill in the application details**:
   - **Application Name**: `readarr-revival`
   - **Image Repository**: `your-registry/readarr-revival:latest`
   - **Image Tag**: `latest`
   - **Container Port**: `8787`
   - **Protocol**: `TCP`

#### **Step 3: Configure Environment Variables**
Add these environment variables in the Custom App configuration:

```yaml
Environment Variables:
  POSTGRES_HOST: postgres
  POSTGRES_PORT: 5432
  POSTGRES_DB: readarr_revival
  POSTGRES_USER: readarr
  POSTGRES_PASSWORD: your_secure_password
  REDIS_HOST: redis
  REDIS_PORT: 6379
  REDIS_PASSWORD: your_redis_password
  NODE_ENV: production
  ASPNETCORE_ENVIRONMENT: Production
```

#### **Step 4: Configure Storage**
Add these volume mounts:

```yaml
Storage:
  - Host Path: /mnt/pool/apps/readarr-revival/data
    Container Path: /app/data
    Read Only: false
  - Host Path: /mnt/pool/apps/readarr-revival/config
    Container Path: /app/config
    Read Only: false
  - Host Path: /mnt/pool/apps/readarr-revival/logs
    Container Path: /app/logs
    Read Only: false
  - Host Path: /mnt/pool/media/books
    Container Path: /books
    Read Only: true
```

#### **Step 5: Deploy the Application**
1. **Click "Deploy"** to start the installation
2. **Wait for the deployment** to complete (5-10 minutes)
3. **Access the application** at `http://your-truenas-ip:8787`

### **Method 2: Using Docker Compose (Advanced)**

#### **Step 1: Enable Docker Compose**
1. **SSH into TrueNAS SCALE**
2. **Install Docker Compose** (if not already installed):
   ```bash
   # Install Docker Compose
   curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
   chmod +x /usr/local/bin/docker-compose
   ```

#### **Step 2: Create Application Directory**
```bash
# Create application directory
mkdir -p /mnt/pool/apps/readarr-revival
cd /mnt/pool/apps/readarr-revival

# Create subdirectories
mkdir -p {data,config,logs,init-scripts,ssl}
```

#### **Step 3: Download Configuration Files**
```bash
# Download Docker Compose file
wget https://raw.githubusercontent.com/your-org/readarr-revival/main/truenas-scale/docker-compose.yml

# Download initialization scripts
wget https://raw.githubusercontent.com/your-org/readarr-revival/main/truenas-scale/init-scripts/01-init-db.sql -O init-scripts/01-init-db.sql
```

#### **Step 4: Customize Configuration**
Edit the `docker-compose.yml` file to match your environment:

```yaml
# Update passwords (use strong, unique passwords)
environment:
  - POSTGRES_PASSWORD=your_secure_postgres_password
  - REDIS_PASSWORD=your_secure_redis_password

# Update book library path
volumes:
  - /mnt/pool/media/books:/books:ro  # Update to your book library path
```

#### **Step 5: Deploy with Docker Compose**
```bash
# Start the application
docker-compose up -d

# Check status
docker-compose ps

# View logs
docker-compose logs -f readarr-revival
```

### **Method 3: Using Kubernetes Manifests**

#### **Step 1: Create Namespace**
```bash
# Create namespace
kubectl create namespace readarr-revival
```

#### **Step 2: Apply Configuration**
```bash
# Apply the Kubernetes configuration
kubectl apply -f readarr-revival.yaml

# Check deployment status
kubectl get pods -n readarr-revival
kubectl get services -n readarr-revival
```

#### **Step 3: Access the Application**
```bash
# Port forward to access the application
kubectl port-forward -n readarr-revival svc/readarr-revival-service 8787:8787
```

## ⚙️ Configuration

### **Initial Setup**
1. **Access the Web Interface**: Navigate to `http://your-truenas-ip:8787`
2. **Complete First Run Setup**:
   - **Database Configuration**: Use the provided PostgreSQL settings
   - **Metadata Sources**: Configure your preferred providers
   - **Library Paths**: Set up your book library directories
   - **Download Clients**: Configure your download clients

### **Metadata Provider Configuration**
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

### **Advanced Features Configuration**
```json
{
  "realTimeUpdates": {
    "enabled": true,
    "webhookPort": 8788,
    "publisherIntegration": true,
    "autoRefresh": true
  },
  "communityFeatures": {
    "enabled": true,
    "requireModeration": true,
    "autoApproveTrustedUsers": true,
    "reputationThreshold": 100
  },
  "analytics": {
    "enabled": true,
    "dataRetentionDays": 90,
    "abTesting": true,
    "performanceMonitoring": true
  }
}
```

## 🔧 Post-Installation Setup

### **1. Configure Reverse Proxy (Optional)**
If you want to access Readarr Revival through a domain name with SSL:

#### **Using TrueNAS SCALE Ingress**
1. **Navigate to Apps** → **Installed Applications**
2. **Find your Readarr Revival app**
3. **Click "Edit"** → **Ingress**
4. **Configure your domain and SSL certificate**

#### **Using Nginx (Docker Compose method)**
```bash
# Create nginx configuration
cat > nginx.conf << 'EOF'
events {
    worker_connections 1024;
}

http {
    upstream readarr {
        server readarr-revival:8787;
    }

    server {
        listen 80;
        server_name your-domain.com;
        
        location / {
            proxy_pass http://readarr;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
}
EOF

# Restart with nginx
docker-compose up -d
```

### **2. Configure Backup Strategy**
```bash
# Create backup script
cat > /mnt/pool/apps/readarr-revival/backup.sh << 'EOF'
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR="/mnt/pool/backups/readarr-revival"

# Create backup directory
mkdir -p $BACKUP_DIR

# Backup PostgreSQL database
docker exec readarr-postgres pg_dump -U readarr readarr_revival > $BACKUP_DIR/db_backup_$DATE.sql

# Backup configuration
tar -czf $BACKUP_DIR/config_backup_$DATE.tar.gz /mnt/pool/apps/readarr-revival/config

# Clean up old backups (keep last 7 days)
find $BACKUP_DIR -name "*.sql" -mtime +7 -delete
find $BACKUP_DIR -name "*.tar.gz" -mtime +7 -delete
EOF

chmod +x /mnt/pool/apps/readarr-revival/backup.sh

# Add to crontab for daily backups
echo "0 2 * * * /mnt/pool/apps/readarr-revival/backup.sh" | crontab -
```

### **3. Configure Monitoring**
```bash
# Create health check script
cat > /mnt/pool/apps/readarr-revival/health-check.sh << 'EOF'
#!/bin/bash
HEALTH_URL="http://localhost:8787/health"
ALERT_EMAIL="your-email@example.com"

# Check application health
if ! curl -f $HEALTH_URL > /dev/null 2>&1; then
    echo "Readarr Revival is down!" | mail -s "Readarr Revival Alert" $ALERT_EMAIL
    # Restart the application
    docker-compose restart readarr-revival
fi
EOF

chmod +x /mnt/pool/apps/readarr-revival/health-check.sh

# Add to crontab for health monitoring
echo "*/5 * * * * /mnt/pool/apps/readarr-revival/health-check.sh" | crontab -
```

## 🔍 Troubleshooting

### **Common Issues**

#### **1. Application Won't Start**
```bash
# Check container logs
docker-compose logs readarr-revival

# Check resource usage
docker stats

# Verify network connectivity
docker exec readarr-revival ping postgres
docker exec readarr-revival ping redis
```

#### **2. Database Connection Issues**
```bash
# Check PostgreSQL status
docker-compose logs postgres

# Test database connection
docker exec readarr-postgres psql -U readarr -d readarr_revival -c "SELECT 1;"

# Check database size
docker exec readarr-postgres psql -U readarr -d readarr_revival -c "SELECT pg_size_pretty(pg_database_size('readarr_revival'));"
```

#### **3. Performance Issues**
```bash
# Check resource usage
docker stats

# Monitor logs for errors
docker-compose logs -f readarr-revival | grep ERROR

# Check disk space
df -h /mnt/pool/apps/readarr-revival
```

#### **4. Port Conflicts**
```bash
# Check what's using port 8787
netstat -tlnp | grep 8787

# Change port in docker-compose.yml if needed
ports:
  - "8788:8787"  # Use port 8788 instead
```

### **Log Locations**
- **Application Logs**: `/mnt/pool/apps/readarr-revival/logs/`
- **Database Logs**: `docker-compose logs postgres`
- **Redis Logs**: `docker-compose logs redis`
- **System Logs**: `/var/log/messages`

## 📊 Monitoring and Maintenance

### **Health Checks**
```bash
# Application health
curl -f http://localhost:8787/health

# Database health
docker exec readarr-postgres pg_isready -U readarr

# Redis health
docker exec readarr-redis redis-cli ping
```

### **Performance Monitoring**
```bash
# Monitor resource usage
docker stats --no-stream

# Check disk usage
du -sh /mnt/pool/apps/readarr-revival/*

# Monitor network connections
netstat -an | grep 8787
```

### **Regular Maintenance**
```bash
# Update application
docker-compose pull
docker-compose up -d

# Clean up old images
docker image prune -f

# Vacuum database
docker exec readarr-postgres psql -U readarr -d readarr_revival -c "VACUUM ANALYZE;"
```

## 🔒 Security Considerations

### **Password Security**
- **Use strong, unique passwords** for PostgreSQL and Redis
- **Change default passwords** immediately after installation
- **Use environment variables** for sensitive data
- **Regular password rotation** (every 90 days)

### **Network Security**
- **Use HTTPS** for external access
- **Configure firewall rules** to restrict access
- **Use VPN** for remote access
- **Monitor access logs** regularly

### **Data Protection**
- **Regular backups** of database and configuration
- **Encrypt sensitive data** at rest
- **Access control** for configuration files
- **Audit logging** for security events

## 📞 Support

### **Getting Help**
- **Documentation**: [Readarr Revival Wiki](https://github.com/your-org/readarr-revival/wiki)
- **Issues**: [GitHub Issues](https://github.com/your-org/readarr-revival/issues)
- **Community**: [Discord Server](https://discord.gg/readarr-revival)
- **Email**: support@readarr-revival.org

### **Useful Commands**
```bash
# View all logs
docker-compose logs -f

# Restart application
docker-compose restart readarr-revival

# Update application
docker-compose pull && docker-compose up -d

# Backup database
docker exec readarr-postgres pg_dump -U readarr readarr_revival > backup.sql

# Restore database
docker exec -i readarr-postgres psql -U readarr readarr_revival < backup.sql
```

---

**Congratulations!** You've successfully installed Readarr Revival on TrueNAS SCALE. Enjoy the enhanced book management experience! 🎉📚 