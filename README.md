# VOLWebHook

A production-quality webhook capture service designed for receiving and logging webhook events from external vendors. Built with ASP.NET Core 8.0, this service is designed to be externally accessible while maintaining security and reliability.

## Features

- **Webhook Capture**: Receives POST requests at `/webhook` and captures full request details
- **Flexible JSON Handling**: Accepts any JSON structure, handles malformed JSON gracefully
- **Comprehensive Logging**: Console and rolling file logs with daily rotation
- **Security Controls**: IP allowlisting, API key validation, HMAC signature verification, rate limiting
- **Persistence**: File-based storage with clean interface for SQL Server extension
- **Production-Ready**: Payload size limits, error handling, health checks

## Project Structure

```
VOLWebHook/
├── src/
│   └── VOLWebHook.Api/
│       ├── Controllers/
│       │   └── WebhookController.cs
│       ├── Middleware/
│       │   ├── RequestBufferingMiddleware.cs
│       │   ├── IpAllowlistMiddleware.cs
│       │   ├── ApiKeyAuthenticationMiddleware.cs
│       │   ├── HmacSignatureMiddleware.cs
│       │   └── RateLimitingMiddleware.cs
│       ├── Services/
│       │   ├── IWebhookPersistenceService.cs
│       │   ├── FileWebhookPersistenceService.cs
│       │   └── WebhookProcessingService.cs
│       ├── Models/
│       │   └── WebhookRequest.cs
│       ├── Configuration/
│       │   ├── WebhookSettings.cs
│       │   ├── SecuritySettings.cs
│       │   └── LoggingSettings.cs
│       ├── Logging/
│       │   └── RollingFileLoggerProvider.cs
│       ├── Program.cs
│       └── appsettings.json
├── Dockerfile
└── VOLWebHook.sln
```

## Prerequisites

- .NET 8.0 SDK
- (Optional) Docker for container deployment
- (Optional) IIS with ASP.NET Core Hosting Bundle for IIS deployment

## Local Development

### Running the Service

```bash
cd src/VOLWebHook.Api
dotnet restore
dotnet run
```

The service starts at:
- HTTPS: `https://localhost:5001`
- HTTP: `http://localhost:5000`

### Testing the Webhook

```bash
# Send a test webhook
curl -X POST https://localhost:5001/webhook \
  -H "Content-Type: application/json" \
  -d '{"event": "test", "data": {"id": 123}}'

# Check health endpoint
curl https://localhost:5001/health
```

## Configuration

All configuration is managed via `appsettings.json`. Environment-specific overrides can be placed in `appsettings.{Environment}.json`.

### Webhook Settings

```json
{
  "Webhook": {
    "MaxPayloadSizeBytes": 10485760,
    "AlwaysReturn200": true,
    "PayloadStoragePath": "./data/webhooks",
    "EnablePayloadPersistence": true,
    "PayloadRetentionDays": 30
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `MaxPayloadSizeBytes` | Maximum request body size | 10 MB |
| `AlwaysReturn200` | Return 200 even on internal errors | `true` |
| `PayloadStoragePath` | Directory for webhook payloads | `./data/webhooks` |
| `EnablePayloadPersistence` | Save webhooks to disk | `true` |
| `PayloadRetentionDays` | Days to retain webhook data | 30 |

### Security Settings

```json
{
  "Security": {
    "IpAllowlist": {
      "Enabled": false,
      "AllowedIps": ["203.0.113.50"],
      "AllowedCidrs": ["10.0.0.0/8"],
      "AllowPrivateNetworks": true
    },
    "ApiKey": {
      "Enabled": false,
      "HeaderName": "X-API-Key",
      "ValidKeys": ["your-secure-api-key"]
    },
    "Hmac": {
      "Enabled": false,
      "HeaderName": "X-Signature",
      "Algorithm": "HMACSHA256",
      "SharedSecret": null
    },
    "RateLimit": {
      "Enabled": false,
      "RequestsPerMinute": 100,
      "RequestsPerHour": 1000,
      "PerIpAddress": true
    }
  }
}
```

### Logging Settings

```json
{
  "FileLogging": {
    "Enabled": true,
    "LogDirectory": "./logs",
    "FileNamePattern": "webhook-{date}.log",
    "RetentionDays": 30,
    "MaxFileSizeBytes": 104857600
  }
}
```

## Production Deployment

### Azure App Service

1. **Create App Service**
   ```bash
   az webapp create \
     --resource-group MyResourceGroup \
     --plan MyAppServicePlan \
     --name volwebhook-prod \
     --runtime "DOTNETCORE:8.0"
   ```

2. **Configure HTTPS**
   - Enable HTTPS Only in Azure Portal
   - Configure custom domain with SSL certificate
   - Or use Azure-managed certificate for custom domains

3. **Set Environment Variables**
   ```bash
   az webapp config appsettings set \
     --resource-group MyResourceGroup \
     --name volwebhook-prod \
     --settings ASPNETCORE_ENVIRONMENT=Production
   ```

4. **Configure Storage**
   - Mount Azure File Share for persistent webhook storage
   - Or use Azure Blob Storage (requires custom implementation)

5. **Deploy**
   ```bash
   dotnet publish -c Release
   az webapp deploy \
     --resource-group MyResourceGroup \
     --name volwebhook-prod \
     --src-path bin/Release/net8.0/publish
   ```

### Docker Deployment

1. **Build Image**
   ```bash
   docker build -t volwebhook:latest .
   ```

2. **Run Container**
   ```bash
   docker run -d \
     --name volwebhook \
     -p 8080:8080 \
     -v /host/data:/var/data/volwebhook \
     -v /host/logs:/var/log/volwebhook \
     -e ASPNETCORE_ENVIRONMENT=Production \
     volwebhook:latest
   ```

3. **With Docker Compose**
   ```yaml
   version: '3.8'
   services:
     volwebhook:
       build: .
       ports:
         - "8080:8080"
       volumes:
         - ./data:/var/data/volwebhook
         - ./logs:/var/log/volwebhook
       environment:
         - ASPNETCORE_ENVIRONMENT=Production
       restart: unless-stopped
   ```

### IIS Deployment

1. **Install Prerequisites**
   - Install ASP.NET Core 8.0 Hosting Bundle
   - Enable IIS URL Rewrite module

2. **Publish Application**
   ```bash
   dotnet publish -c Release -o C:\inetpub\volwebhook
   ```

3. **Create IIS Site**
   - Create new site pointing to publish folder
   - Configure application pool for "No Managed Code"
   - Bind to desired hostname and port

4. **Configure web.config** (auto-generated, customize if needed)
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <location path="." inheritInChildApplications="false">
       <system.webServer>
         <handlers>
           <add name="aspNetCore" path="*" verb="*"
                modules="AspNetCoreModuleV2" resourceType="Unspecified" />
         </handlers>
         <aspNetCore processPath="dotnet"
                     arguments=".\VOLWebHook.Api.dll"
                     stdoutLogEnabled="true"
                     stdoutLogFile=".\logs\stdout"
                     hostingModel="inprocess">
           <environmentVariables>
             <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
           </environmentVariables>
         </aspNetCore>
       </system.webServer>
     </location>
   </configuration>
   ```

### Reverse Proxy Configuration

#### Nginx

```nginx
upstream volwebhook {
    server 127.0.0.1:8080;
}

server {
    listen 443 ssl http2;
    server_name webhook.example.com;

    ssl_certificate /etc/nginx/ssl/webhook.crt;
    ssl_certificate_key /etc/nginx/ssl/webhook.key;

    location / {
        proxy_pass http://volwebhook;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Webhook-specific settings
        proxy_read_timeout 30s;
        proxy_send_timeout 30s;
        client_max_body_size 10M;
    }
}
```

#### Azure Application Gateway / Front Door

Configure with:
- Backend pool pointing to App Service or VM
- Health probe to `/health`
- HTTPS listener with managed certificate
- Routing rule to forward `/webhook` to backend

## Securing the Endpoint

### Enable IP Allowlisting

Restrict access to known VOL IP addresses:

```json
{
  "Security": {
    "IpAllowlist": {
      "Enabled": true,
      "AllowedIps": ["203.0.113.50", "203.0.113.51"],
      "AllowedCidrs": ["198.51.100.0/24"],
      "AllowPrivateNetworks": false
    }
  }
}
```

### Enable API Key Authentication

Require an API key header from the webhook sender:

```json
{
  "Security": {
    "ApiKey": {
      "Enabled": true,
      "HeaderName": "X-API-Key",
      "ValidKeys": ["your-long-random-api-key-here"]
    }
  }
}
```

Provide this key to VOL for their webhook configuration.

### Enable HMAC Signature Verification

For vendors supporting HMAC signatures:

```json
{
  "Security": {
    "Hmac": {
      "Enabled": true,
      "HeaderName": "X-Signature",
      "Algorithm": "HMACSHA256",
      "SharedSecret": "your-shared-secret"
    }
  }
}
```

### Enable Rate Limiting

Protect against abuse:

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

### Network Security

- Place behind a WAF (Azure WAF, AWS WAF, Cloudflare)
- Use private endpoints in Azure
- Configure NSG rules for VM deployments
- Enable DDoS protection

## Extending for SQL Server Persistence

The service uses `IWebhookPersistenceService` interface for storage abstraction. To add SQL Server support:

1. **Create SQL Implementation**

```csharp
public sealed class SqlWebhookPersistenceService : IWebhookPersistenceService
{
    private readonly string _connectionString;

    public SqlWebhookPersistenceService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("WebhookDb");
    }

    public async Task SaveAsync(WebhookRequest request, CancellationToken ct = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = @"
            INSERT INTO Webhooks (Id, ReceivedAtUtc, HttpMethod, Path,
                SourceIpAddress, Headers, RawBody, ContentType, IsValidJson)
            VALUES (@Id, @ReceivedAtUtc, @HttpMethod, @Path,
                @SourceIpAddress, @Headers, @RawBody, @ContentType, @IsValidJson)";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", request.Id);
        command.Parameters.AddWithValue("@ReceivedAtUtc", request.ReceivedAtUtc);
        command.Parameters.AddWithValue("@HttpMethod", request.HttpMethod);
        command.Parameters.AddWithValue("@Path", request.Path);
        command.Parameters.AddWithValue("@SourceIpAddress", request.SourceIpAddress);
        command.Parameters.AddWithValue("@Headers", JsonSerializer.Serialize(request.Headers));
        command.Parameters.AddWithValue("@RawBody", request.RawBody);
        command.Parameters.AddWithValue("@ContentType", request.ContentType ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@IsValidJson", request.IsValidJson);

        await command.ExecuteNonQueryAsync(ct);
    }

    // Implement other interface methods...
}
```

2. **Create Database Table**

```sql
CREATE TABLE Webhooks (
    Id NVARCHAR(32) PRIMARY KEY,
    ReceivedAtUtc DATETIME2 NOT NULL,
    HttpMethod NVARCHAR(10) NOT NULL,
    Path NVARCHAR(500) NOT NULL,
    QueryString NVARCHAR(2000) NULL,
    SourceIpAddress NVARCHAR(45) NOT NULL,
    SourcePort INT NOT NULL,
    Headers NVARCHAR(MAX) NOT NULL,
    RawBody NVARCHAR(MAX) NOT NULL,
    ContentLength INT NOT NULL,
    ContentType NVARCHAR(200) NULL,
    IsValidJson BIT NOT NULL,
    JsonParseError NVARCHAR(500) NULL,
    INDEX IX_Webhooks_ReceivedAtUtc (ReceivedAtUtc DESC)
);
```

3. **Register in DI**

```csharp
// In Program.cs
if (useSqlPersistence)
{
    builder.Services.AddSingleton<IWebhookPersistenceService, SqlWebhookPersistenceService>();
}
else
{
    builder.Services.AddSingleton<IWebhookPersistenceService, FileWebhookPersistenceService>();
}
```

## Webhook Payload Structure

Each captured webhook is stored with the following structure:

```json
{
  "id": "a1b2c3d4e5f6...",
  "receivedAtUtc": "2024-01-15T10:30:00.123Z",
  "httpMethod": "POST",
  "path": "/webhook",
  "queryString": null,
  "sourceIpAddress": "203.0.113.50",
  "sourcePort": 54321,
  "headers": {
    "Content-Type": ["application/json"],
    "X-Request-Id": ["abc123"],
    "User-Agent": ["VOL-Webhook/1.0"]
  },
  "rawBody": "{\"event\":\"order.created\",\"data\":{...}}",
  "contentLength": 1234,
  "contentType": "application/json",
  "isValidJson": true,
  "parsedJson": { ... },
  "jsonParseError": null
}
```

## Monitoring

### Health Check

```bash
curl https://webhook.example.com/health
```

Response:
```json
{
  "status": "healthy",
  "timestamp": "2024-01-15T10:30:00Z",
  "service": "VOLWebHook"
}
```

### Log Analysis

Logs are structured for easy parsing:

```
2024-01-15 10:30:00.123 UTC | INFORMATION | WebhookProcessingService | Webhook received: [a1b2c3d4] 2024-01-15 10:30:00.123 UTC | POST /webhook | Source: 203.0.113.50:54321 | Size: 1234 bytes | Valid JSON: True
```

## Troubleshooting

### Common Issues

1. **403 Forbidden**
   - Check IP allowlist configuration
   - Verify source IP matches allowed list
   - Check if behind proxy (X-Forwarded-For header)

2. **401 Unauthorized**
   - Verify API key header is present and correct
   - Check header name matches configuration

3. **413 Payload Too Large**
   - Increase `MaxPayloadSizeBytes` in configuration
   - Check reverse proxy limits (nginx `client_max_body_size`)

4. **429 Too Many Requests**
   - Rate limit exceeded
   - Check `Retry-After` header for wait time
   - Adjust rate limit settings if needed

## License

Internal use only. Not for redistribution.
