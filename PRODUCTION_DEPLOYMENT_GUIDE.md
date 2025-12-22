# VOLWebHook - Production Deployment Guide

## Table of Contents
1. [Pre-Deployment Checklist](#pre-deployment-checklist)
2. [Environment Configuration](#environment-configuration)
3. [Security Hardening](#security-hardening)
4. [Deployment Options](#deployment-options)
5. [Post-Deployment Validation](#post-deployment-validation)
6. [Monitoring & Maintenance](#monitoring--maintenance)
7. [Troubleshooting](#troubleshooting)

---

## Pre-Deployment Checklist

### Critical (Must Complete)

- [ ] Dashboard API keys generated and stored securely
- [ ] Webhook authentication method selected and configured (IP/API Key/HMAC)
- [ ] Rate limiting enabled and tuned for expected load
- [ ] SSL/TLS certificate configured
- [ ] Storage directory created with correct permissions
- [ ] Log directory created with correct permissions
- [ ] Environment variables configured for all secrets
- [ ] Startup validation passes without errors
- [ ] Health check endpoint accessible

### Recommended

- [ ] Trusted proxy configuration (if behind load balancer)
- [ ] Log aggregation configured
- [ ] Monitoring/alerting configured
- [ ] Backup strategy defined
- [ ] Disaster recovery plan documented
- [ ] Runbook created for common operations

---

## Environment Configuration

### Required Environment Variables

```bash
# Dashboard Authentication (CRITICAL)
VOLWEBHOOK_Dashboard__RequireAuthentication=true
VOLWEBHOOK_Dashboard__ValidApiKeys__0=<generate-secure-key-here>
VOLWEBHOOK_Dashboard__ValidApiKeys__1=<backup-key-optional>

# Webhook Security (Choose at least ONE)

## Option 1: API Key Authentication
VOLWEBHOOK_Security__ApiKey__Enabled=true
VOLWEBHOOK_Security__ApiKey__ValidKeys__0=<webhook-api-key>
VOLWEBHOOK_Security__ApiKey__ValidKeys__1=<webhook-api-key-2>

## Option 2: HMAC Signature Verification
VOLWEBHOOK_Security__Hmac__Enabled=true
VOLWEBHOOK_Security__Hmac__SharedSecret=<hmac-shared-secret>

## Option 3: IP Allowlist (can combine with above)
VOLWEBHOOK_Security__IpAllowlist__Enabled=true
VOLWEBHOOK_Security__IpAllowlist__AllowedIps__0=52.12.34.56
VOLWEBHOOK_Security__IpAllowlist__AllowedCidrs__0=10.0.0.0/8

# Rate Limiting (RECOMMENDED)
VOLWEBHOOK_Security__RateLimit__Enabled=true
VOLWEBHOOK_Security__RateLimit__RequestsPerMinute=60
VOLWEBHOOK_Security__RateLimit__RequestsPerHour=500

# Storage Paths (adjust for your environment)
VOLWEBHOOK_Webhook__PayloadStoragePath=/var/data/volwebhook/webhooks
VOLWEBHOOK_FileLogging__LogDirectory=/var/log/volwebhook

# CORS (if dashboard UI used from different origin)
ALLOWED_ORIGIN=dashboard.yourdomain.com
```

### Generating Secure Keys

```bash
# Generate dashboard API key (Linux/macOS)
openssl rand -base64 32

# Generate dashboard API key (PowerShell)
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))

# Generate webhook API key with prefix
echo "vwh_$(openssl rand -base64 32 | tr -d '+/=' | cut -c1-40)"
```

---

## Security Hardening

### 1. Enable HTTPS Only

#### Azure App Service
```bash
az webapp update \
  --resource-group <rg-name> \
  --name <app-name> \
  --https-only true
```

#### IIS
```xml
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="HTTPS Redirect" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="off" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
```

### 2. Configure Firewall Rules

#### Azure Network Security Group
```bash
az network nsg rule create \
  --resource-group <rg-name> \
  --nsg-name <nsg-name> \
  --name AllowHTTPS \
  --priority 100 \
  --source-address-prefixes '*' \
  --source-port-ranges '*' \
  --destination-address-prefixes '*' \
  --destination-port-ranges 443 \
  --access Allow \
  --protocol Tcp
```

#### AWS Security Group
```bash
aws ec2 authorize-security-group-ingress \
  --group-id sg-xxxxxx \
  --protocol tcp \
  --port 443 \
  --cidr 0.0.0.0/0
```

### 3. Configure Trusted Proxies (if behind load balancer)

Update `appsettings.Production.json`:
```json
{
  "ForwardedHeaders": {
    "Enabled": true,
    "TrustedProxies": ["10.0.1.10"],
    "TrustedNetworks": ["10.0.0.0/16"],
    "ForwardLimit": 1,
    "ForwardedForEnabled": true,
    "ForwardedProtoEnabled": true
  }
}
```

### 4. Enable Rate Limiting

```json
{
  "Security": {
    "RateLimit": {
      "Enabled": true,
      "RequestsPerMinute": 60,
      "RequestsPerHour": 500,
      "PerIpAddress": true
    }
  }
}
```

---

## Deployment Options

### Option 1: Azure App Service (Recommended for Cloud)

#### 1. Create App Service
```bash
# Create resource group
az group create \
  --name volwebhook-rg \
  --location eastus

# Create App Service Plan
az appservice plan create \
  --name volwebhook-plan \
  --resource-group volwebhook-rg \
  --sku B1 \
  --is-linux

# Create Web App
az webapp create \
  --resource-group volwebhook-rg \
  --plan volwebhook-plan \
  --name volwebhook-prod \
  --runtime "DOTNETCORE:8.0"
```

#### 2. Configure Storage (Azure Files)
```bash
# Create storage account
az storage account create \
  --name volwebhookstorage \
  --resource-group volwebhook-rg \
  --sku Standard_LRS

# Create file share
az storage share create \
  --name webhooks \
  --account-name volwebhookstorage

# Mount to App Service
az webapp config storage-account add \
  --resource-group volwebhook-rg \
  --name volwebhook-prod \
  --custom-id WebhookStorage \
  --storage-type AzureFiles \
  --share-name webhooks \
  --account-name volwebhookstorage \
  --mount-path /var/data/volwebhook/webhooks
```

#### 3. Configure Environment Variables
```bash
az webapp config appsettings set \
  --resource-group volwebhook-rg \
  --name volwebhook-prod \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    VOLWEBHOOK_Dashboard__RequireAuthentication=true \
    VOLWEBHOOK_Dashboard__ValidApiKeys__0="@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/DashboardKey/)" \
    VOLWEBHOOK_Security__RateLimit__Enabled=true
```

#### 4. Deploy
```bash
# Build and publish
dotnet publish -c Release -o ./publish

# Deploy
az webapp deploy \
  --resource-group volwebhook-rg \
  --name volwebhook-prod \
  --src-path ./publish.zip \
  --type zip
```

### Option 2: Docker Container

#### Dockerfile (production-optimized)
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Create non-root user
RUN groupadd -r volwebhook && useradd -r -g volwebhook volwebhook

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/VOLWebHook.Api/VOLWebHook.Api.csproj", "src/VOLWebHook.Api/"]
RUN dotnet restore "src/VOLWebHook.Api/VOLWebHook.Api.csproj"
COPY . .
WORKDIR "/src/src/VOLWebHook.Api"
RUN dotnet build "VOLWebHook.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VOLWebHook.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set ownership
RUN chown -R volwebhook:volwebhook /app
RUN mkdir -p /var/data/volwebhook/webhooks /var/log/volwebhook
RUN chown -R volwebhook:volwebhook /var/data/volwebhook /var/log/volwebhook

# Switch to non-root user
USER volwebhook

ENTRYPOINT ["dotnet", "VOLWebHook.Api.dll"]
```

#### docker-compose.yml (production)
```yaml
version: '3.8'

services:
  volwebhook:
    build: .
    ports:
      - "443:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - VOLWEBHOOK_Dashboard__RequireAuthentication=true
      - VOLWEBHOOK_Dashboard__ValidApiKeys__0=${DASHBOARD_KEY}
      - VOLWEBHOOK_Security__ApiKey__Enabled=true
      - VOLWEBHOOK_Security__ApiKey__ValidKeys__0=${WEBHOOK_API_KEY}
      - VOLWEBHOOK_Security__RateLimit__Enabled=true
    volumes:
      - webhook-data:/var/data/volwebhook/webhooks
      - webhook-logs:/var/log/volwebhook
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 512M
        reservations:
          cpus: '0.5'
          memory: 256M

volumes:
  webhook-data:
  webhook-logs:
```

### Option 3: Kubernetes

#### deployment.yaml
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: volwebhook
  namespace: production
spec:
  replicas: 3
  selector:
    matchLabels:
      app: volwebhook
  template:
    metadata:
      labels:
        app: volwebhook
    spec:
      securityContext:
        runAsNonRoot: true
        runAsUser: 1000
        fsGroup: 1000
      containers:
      - name: volwebhook
        image: yourregistry.azurecr.io/volwebhook:latest
        ports:
        - containerPort: 8080
          name: http
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: VOLWEBHOOK_Dashboard__RequireAuthentication
          value: "true"
        - name: VOLWEBHOOK_Dashboard__ValidApiKeys__0
          valueFrom:
            secretKeyRef:
              name: volwebhook-secrets
              key: dashboard-key
        - name: VOLWEBHOOK_Security__ApiKey__Enabled
          value: "true"
        - name: VOLWEBHOOK_Security__ApiKey__ValidKeys__0
          valueFrom:
            secretKeyRef:
              name: volwebhook-secrets
              key: webhook-api-key
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
        volumeMounts:
        - name: webhook-storage
          mountPath: /var/data/volwebhook/webhooks
        - name: log-storage
          mountPath: /var/log/volwebhook
      volumes:
      - name: webhook-storage
        persistentVolumeClaim:
          claimName: volwebhook-data
      - name: log-storage
        emptyDir: {}
```

---

## Post-Deployment Validation

### 1. Verify Health Endpoint

```bash
curl https://your-domain.com/health
```

Expected response:
```json
{
  "status": "healthy",
  "service": "VOLWebHook",
  "timestamp": "2025-12-22T10:30:00Z",
  "version": "1.0.0"
}
```

### 2. Test Dashboard Authentication

```bash
# Should fail (401 Unauthorized)
curl -X GET https://your-domain.com/api/dashboard/stats

# Should succeed
curl -X GET https://your-domain.com/api/dashboard/stats \
  -H "X-Dashboard-Key: your-dashboard-key"
```

### 3. Test Webhook Endpoint

```bash
# With API key
curl -X POST https://your-domain.com/webhook \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-webhook-api-key" \
  -d '{"event": "test", "data": {"message": "Hello"}}'
```

Expected response:
```json
{
  "requestId": "a1b2c3d4...",
  "receivedAtUtc": "2025-12-22T10:30:00.123Z",
  "status": "received"
}
```

### 4. Test Rate Limiting

```bash
# Send rapid requests to trigger rate limit
for i in {1..100}; do
  curl -X POST https://your-domain.com/webhook \
    -H "Content-Type: application/json" \
    -H "X-API-Key: your-key" \
    -d "{\"test\": $i}"
done
```

Expected: Should receive `429 Too Many Requests` after limit is exceeded.

### 5. Verify Security Headers

```bash
curl -I https://your-domain.com/health
```

Expected headers:
```
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
Content-Security-Policy: default-src 'self'; ...
```

---

## Monitoring & Maintenance

### Application Insights (Azure)

```bash
# Add Application Insights
az monitor app-insights component create \
  --app volwebhook-insights \
  --location eastus \
  --resource-group volwebhook-rg

# Configure App Service
az webapp config appsettings set \
  --resource-group volwebhook-rg \
  --name volwebhook-prod \
  --settings APPLICATIONINSIGHTS_CONNECTION_STRING="<connection-string>"
```

### Key Metrics to Monitor

1. **Request Rate**
   - Webhook requests per minute
   - Dashboard API requests

2. **Error Rate**
   - 4xx errors (authentication failures)
   - 5xx errors (server errors)
   - Rate limit rejections (429)

3. **Latency**
   - P50, P95, P99 response times
   - Storage write latency

4. **Resource Usage**
   - CPU utilization
   - Memory usage
   - Disk space (webhook storage)

5. **Security Events**
   - Failed authentication attempts
   - IP allowlist rejections
   - HMAC signature failures

### Alerting Rules

```yaml
# Example: Azure Monitor Alert
- name: High Error Rate
  condition: Failed requests > 10 in 5 minutes
  severity: High
  action: Email + PagerDuty

- name: Authentication Failures
  condition: 401 responses > 20 in 5 minutes
  severity: Medium
  action: Email

- name: Storage Full
  condition: Disk usage > 90%
  severity: Critical
  action: Email + PagerDuty
```

### Log Retention

Configure log cleanup job:

```bash
# Crontab entry (daily at 2 AM)
0 2 * * * /usr/local/bin/cleanup-webhooks.sh

# cleanup-webhooks.sh
#!/bin/bash
curl -X POST https://your-domain.com/api/dashboard/cleanup?retentionDays=90 \
  -H "X-Dashboard-Key: your-key"
```

---

## Troubleshooting

### Issue: Service won't start

**Check:**
1. Configuration validation errors in logs
2. Storage directory permissions
3. Port conflicts

```bash
# View logs
docker logs volwebhook
# or
journalctl -u volwebhook -f
```

### Issue: Authentication failures

**Check:**
1. Environment variables loaded correctly
2. API key format (no leading/trailing spaces)
3. Header name matches configuration

```bash
# Test environment variable
echo $VOLWEBHOOK_Dashboard__ValidApiKeys__0
```

### Issue: Rate limiting too aggressive

**Adjust:**
```bash
# Increase limits
VOLWEBHOOK_Security__RateLimit__RequestsPerMinute=120
VOLWEBHOOK_Security__RateLimit__RequestsPerHour=2000
```

### Issue: Webhooks not persisting

**Check:**
1. Storage directory exists and is writable
2. `EnablePayloadPersistence: true` in config
3. Disk space available

```bash
# Check permissions
ls -la /var/data/volwebhook/webhooks

# Check disk space
df -h /var/data/volwebhook
```

---

## Backup & Disaster Recovery

### Backup Strategy

```bash
# Daily backup of webhook data
0 3 * * * tar -czf /backups/webhooks-$(date +\%Y\%m\%d).tar.gz /var/data/volwebhook/webhooks

# Retain backups for 30 days
find /backups -name "webhooks-*.tar.gz" -mtime +30 -delete
```

### Restore Procedure

```bash
# Stop service
systemctl stop volwebhook

# Restore data
tar -xzf /backups/webhooks-20251222.tar.gz -C /

# Start service
systemctl start volwebhook
```

---

## Security Incident Response

### If API Keys Compromised

1. Generate new keys immediately:
```bash
NEW_KEY=$(openssl rand -base64 32)
```

2. Update environment variables with new keys

3. Restart application

4. Review logs for unauthorized access:
```bash
grep "401\|403" /var/log/volwebhook/*.log
```

5. Rotate all keys on regular schedule (quarterly recommended)

---

## Maintenance Windows

### Recommended Schedule

- **Daily:** Log cleanup, health checks
- **Weekly:** Review security logs, check disk usage
- **Monthly:** Apply security patches, rotate keys
- **Quarterly:** Full security audit, penetration test

---

**Document Version:** 1.0
**Last Updated:** 2025-12-22
