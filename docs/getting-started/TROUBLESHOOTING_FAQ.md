# 🔧 GymFlowPro API Troubleshooting & FAQ

**Version:** 1.0  
**Last Updated:** May 2, 2026

---

## 📑 Table of Contents

1. [Common Setup Issues](#common-setup-issues)
2. [Database Problems](#database-problems)
3. [Authentication Issues](#authentication-issues)
4. [API Runtime Errors](#api-runtime-errors)
5. [Performance Issues](#performance-issues)
6. [Deployment Issues](#deployment-issues)
7. [FAQ](#faq)

---

## Common Setup Issues

### Issue: ".NET 8 SDK not found"

**Error Message:**
```
A compatible .NET 8 SDK could not be found. Please install .NET 8.
```

**Solution:**

```powershell
# Check current .NET version
dotnet --version

# Download .NET 8 from https://dotnet.microsoft.com/download
# Install it, then verify
dotnet --version
# Should output: 8.0.x
```

---

### Issue: "SQL Server connection failed"

**Error Message:**
```
Server=(localdb)\mssqllocaldb;Database=GymFlowProDb;Integrated Security=true;
Named Pipes Provider, error: 40 - Could not open a connection to SQL Server
```

**Solution 1: Check SQL Server is running**

```powershell
# List SQL Server instances
sqllocaldb info

# Start LocalDB
sqllocaldb start mssqllocaldb

# Verify connection
sqlcmd -S "(localdb)\mssqllocaldb"
# Should show: 1>
# Type: exit
# To quit
```

**Solution 2: Fix connection string**

```powershell
# Open appsettings.json
cat GMS.Api\appsettings.json | findstr ConnectionStrings

# Ensure it matches:
# "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Integrated Security=true;"
```

**Solution 3: Use SQL Server Authentication**

```json
// If integrated auth fails, try:
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;User Id=sa;Password=YOUR_PASSWORD;"
}
```

---

### Issue: "Port 5001 already in use"

**Error Message:**
```
fail: Microsoft.AspNetCore.Server.Kestrel[0]
      Unable to start Kestrel.
System.Net.Sockets.SocketException: Only one usage of each socket address... is normally permitted
```

**Solution 1: Kill existing process**

```powershell
# Find process on port 5001
netstat -ano | findstr :5001

# Kill process (replace PID)
taskkill /PID 12345 /F
```

**Solution 2: Use different port**

```powershell
# Modify launchSettings.json
# Change "applicationUrl": "https://localhost:5001" to 5002
cat GMS.Api\Properties\launchSettings.json

# Or run with custom port
dotnet run --project GMS.Api -- --urls="https://localhost:5002"
```

**Solution 3: Stop IIS**

```powershell
# If IIS is using port 5001
net stop was /y  # Stop IIS
net start w3svc  # Restart later
```

---

### Issue: "HTTPS Certificate Error"

**Error Message:**
```
System.Security.Cryptography.CryptographyException: The certificate chain was issued by an authority that is not trusted.
```

**Solution:**

```powershell
# Trust development certificate
dotnet dev-certs https --trust

# If that fails, regenerate it
dotnet dev-certs https --clean
dotnet dev-certs https --trust

# Verify it's installed
dotnet dev-certs https --check
# Output: A valid certificate was found
```

---

## Database Problems

### Issue: "Connection string 'DefaultConnection' not found"

**Error Message:**
```
InvalidOperationException: Connection string 'DefaultConnection' not found.
```

**Solution:**

```powershell
# Verify appsettings.json exists
ls GMS.Api\appsettings.json

# Check it has ConnectionStrings section
cat GMS.Api\appsettings.json
```

**Expected Content:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Integrated Security=true;"
  }
}
```

---

### Issue: "Migrations not applied"

**Error Message:**
```
Invalid object name 'dbo.GymMembers'
```

**Solution:**

```powershell
# Check current state
dotnet ef migrations list --project GMS.Infrastructure --startup-project GMS.Api

# Apply migrations
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# Verify tables exist
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT name FROM sys.tables"
```

**Expected tables:**
```
Tenants
GymMembers
MembershipPlans
Memberships
GymAttendance
MemberInvitations
AppUsers
```

---

### Issue: "Migration already exists"

**Error Message:**
```
There is already a pending migration 'InitialCreate'. Please remove it before adding a new one.
```

**Solution:**

```powershell
# Remove the pending migration
dotnet ef migrations remove --project GMS.Infrastructure --startup-project GMS.Api

# Or: Check migrations state
dotnet ef migrations list --project GMS.Infrastructure --startup-project GMS.Api

# And revert to specific migration
dotnet ef database update [MigrationName] --project GMS.Infrastructure --startup-project GMS.Api
```

---

### Issue: "Cannot drop database (locked)"

**Error Message:**
```
Cannot drop database "GymFlowProDb" because it is currently in use.
```

**Solution:**

```powershell
# Kill connections to database
sqlcmd -S "(localdb)\mssqllocaldb" -Q "ALTER DATABASE GymFlowProDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE GymFlowProDb;"

# Then recreate
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

---

## Authentication Issues

### Issue: "401 Unauthorized - Missing token"

**Error Message:**
```
{
  "error": "Missing or invalid authorization header"
}
```

**Solution:**

```bash
# ❌ Wrong - No Authorization header
curl -X GET "https://localhost:5001/api/attendance/search-members?searchTerm=ahmed"

# ✅ Correct - Include Authorization header
curl -X GET "https://localhost:5001/api/attendance/search-members?searchTerm=ahmed" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

---

### Issue: "401 Unauthorized - Invalid token"

**Error Message:**
```
{
  "error": "Invalid token or token expired"
}
```

**Solution:**

```bash
# 1. Get new token
curl -X POST "https://localhost:5001/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"manager@gym.com","password": "YOUR_PASSWORD"}'

# 2. Copy the accessToken from response

# 3. Use token (valid for 15 minutes)
curl -X GET "https://localhost:5001/api/attendance/search-members?searchTerm=ahmed" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

---

### Issue: "403 Forbidden - Insufficient permissions"

**Error Message:**
```
{
  "error": "User does not have permission to perform this action"
}
```

**Solution:**

Check your user role and required policy:

| Endpoint | Required Policy | Required Role |
|----------|-----------------|---------------|
| `POST /api/attendance/manual-checkin` | `ManagerOrAbove` | Manager or Admin |
| `POST /api/attendance/qr-checkin` | `AuthenticatedMember` | Member |
| `GET /api/attendance/search-members` | `AnyStaff` | Staff/Manager/Admin |
| `POST /api/invitation/send` | `AuthenticatedMember` | Member |

**Solution:**

```powershell
# Verify your JWT token contains correct role claim
# Decode at https://jwt.io and check "role" claim
```

---

### Issue: "OTP invalid or expired"

**Error Message:**
```
{
  "error": "OTP invalid or expired. Please request a new one."
}
```

**Solution:**

1. **OTP expires after 5 minutes** - Request a new one if expired:
   ```bash
   POST /api/auth/member-otp
   { "phoneNumber": "+201234567890" }
   ```

2. **OTP is 6 digits** - Ensure you're sending 6 digits:
   ```bash
   POST /api/auth/member-verify
   {
     "phoneNumber": "+201234567890",
     "otp": "123456"  # Must be exactly 6 digits
   }
   ```

---

## API Runtime Errors

### Issue: "400 Bad Request - Missing required field"

**Error Message:**
```
{
  "error": "The request body is invalid or missing required fields."
}
```

**Solution: Check request body**

```bash
# ❌ Wrong - Missing "guestPhone"
curl -X POST "https://localhost:5001/api/invitation/send" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"guestName": "Ahmed"}'

# ✅ Correct - Include all required fields
curl -X POST "https://localhost:5001/api/invitation/send" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"guestName": "Ahmed", "guestPhone": "+201234567890"}'
```

---

### Issue: "400 Bad Request - Monthly quota exceeded"

**Error Message:**
```
{
  "error": "Monthly invitation quota exceeded (3/3)"
}
```

**Solution:**

- Each member can send max **3 invitations per month**
- Quota resets on the **1st of the next month**
- Wait until next month or use a different member account

**Check remaining quota:**
```bash
GET /api/invitation/history
# Response includes remaining quota
```

---

### Issue: "400 Bad Request - Membership expired"

**Error Message:**
```
{
  "error": "Membership expired"
}
```

**Solution:**

Member needs to renew membership before check-in. Contact gym admin to:
1. Check membership end date
2. Process renewal payment
3. Activate new membership

---

### Issue: "400 Bad Request - Membership frozen"

**Error Message:**
```
{
  "error": "Membership is frozen until 2026-05-10"
}
```

**Solution:**

- Member's membership is temporarily frozen
- They cannot check in until freeze expires
- Contact gym admin to unfreeze membership

---

### Issue: "429 Too Many Requests"

**Error Message:**
```
{
  "error": "Too many check-in attempts. Please try again in 30 seconds."
}
```

**Solution:**

- Check-in is rate limited to **10 attempts per minute per member**
- Wait 30 seconds before trying again
- This is by design to prevent abuse

---

### Issue: "500 Internal Server Error"

**Error Message:**
```
{
  "error": "An unexpected error occurred",
  "traceId": "0HN1GBDV5RSQR:00000001"
}
```

**Solution:**

```powershell
# 1. Check API logs
# Look for errors in console output or event log

# 2. Enable detailed logging
# Set logging level to Debug in appsettings.Development.json:
#{
#  "Logging": {
#    "LogLevel": {
#      "Default": "Debug"
#    }
#  }
#}

# 3. Try request again and capture full error trace
```

---

## Performance Issues

### Issue: "Slow check-in response"

**Symptom:** Check-in takes > 3 seconds

**Solution:**

```powershell
# 1. Check database indexes
# Ensure IX_GymAttendance_MemberId_CheckInTime index exists

sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('GymAttendance')"

# 2. Analyze query performance
# Enable profiler or query insights

# 3. Increase connection pool size
# In appsettings.json:
# "DefaultConnection": "...;Max Pool Size=100;"
```

---

### Issue: "Memory leak - process grows over time"

**Solution:**

```csharp
// Ensure proper disposal of DbContext
public class SomeService : IDisposable
{
    private readonly IRepository<T> _repo;
    
    public void Dispose()
    {
        _repo?.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Or use dependency injection (automatic disposal)
```

---

### Issue: "Database connection pool exhausted"

**Error Message:**
```
Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.
```

**Solution:**

```json
// Increase connection pool in appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Max Pool Size=100;Connection Lifetime=300;"
  }
}
```

---

## Deployment Issues

### Issue: "Connection string has sensitive data in logs"

**Solution: Use User Secrets (Development)**

```powershell
# Initialize user secrets
dotnet user-secrets init --project GMS.Api

# Store connection string
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Password=..." --project GMS.Api

# In appsettings.json, use placeholder:
# "DefaultConnection": "Development value"
```

---

### Issue: "CORS errors in browser"

**Error Message:**
```
Access to XMLHttpRequest at 'https://api.example.com/...' has been blocked by CORS policy
```

**Solution:**

```csharp
// In Program.cs, configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .WithOrigins("https://app.example.com", "http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

app.UseCors("AllowFrontend");
```

---

### Issue: "SSL/TLS certificate expired in production"

**Solution:**

```powershell
# Update certificate via your hosting provider
# For Azure App Service:
# 1. Go to Settings > TLS/SSL settings
# 2. Upload new certificate

# For IIS:
# 1. Open IIS Manager
# 2. Select server, double-click "Server Certificates"
# 3. Import new certificate

# Verify certificate
Invoke-RestMethod -Uri https://api.example.com/api/health -SkipCertificateCheck
```

---

## FAQ

### Q: Can members check in multiple times per day?

**A:** Yes, but they're limited by:
1. **Session quota** - e.g., "Premium plan allows 12 sessions/month"
2. **Session tracking** - Resets on 1st of month
3. **Rate limiting** - Max 10 check-ins per minute per member

---

### Q: What happens if a membership expires mid-month?

**A:** 
- Member cannot check in
- Error: "Membership expired"
- They must renew before checking in again
- No partial refunds

---

### Q: Can staff manually check in frozen members?

**A:** No. Even staff cannot bypass freeze. The freeze must be lifted by admin first.

---

### Q: How are invitations tracked?

**A:**
- Each invitation has 7-day expiration
- Monthly quota resets on 1st of month
- Invitations can be: Active, Expired, Used, Revoked

---

### Q: What's the difference between member and staff JWT tokens?

**A:**

| Property | Member Token | Staff Token |
|----------|--------------|-------------|
| Expiry | 1 hour | 15 minutes |
| Claims | sub, tenant_id, phone | sub, tenant_id, email, role |
| Refresh Token Expiry | 30 days | 30 days |
| Endpoints | /qr-checkin, /invitation/* | /manual-checkin, /search-members |

---

### Q: How do I reset a user's password?

**A:**

```csharp
// Use IdentityService
public async Task ResetPasswordAsync(string userId, string newPassword)
{
    var user = await _userManager.FindByIdAsync(userId);
    var result = await _userManager.RemovePasswordAsync(user);
    result = await _userManager.AddPasswordAsync(user, newPassword);
    return result.Succeeded;
}
```

---

### Q: How do I view payment webhook logs?

**A:**

```powershell
# Check logs
Get-Content -Path GMS.Api/logs/*.txt | Select-String "Webhook"

# Or check database for payment records
sqlcmd -S "(localdb)\mssqllocaldb" -d GymFlowProDb -Q "SELECT * FROM PaymentTransactions ORDER BY CreatedAt DESC"
```

---

### Q: How do I test Paymob/Fawry webhooks locally?

**A:**

1. **Use ngrok to expose localhost:**
   ```powershell
   # Download ngrok from ngrok.com
   ngrok http 5001
   # Output: Forwarding https://abc123.ngrok.io -> localhost:5001
   ```

2. **Configure webhook URL in Paymob/Fawry:**
   ```
   https://abc123.ngrok.io/api/payments/paymob-webhook
   ```

3. **Send test webhook:**
   ```bash
   curl -X POST "https://localhost:5001/api/payments/paymob-webhook" \
     -H "Content-Type: application/json" \
     -H "X-Hmac: {signature}" \
     -d '{...webhook payload...}'
   ```

---

### Q: How do I backup the database?

**A:**

```powershell
# Backup LocalDB
sqlcmd -S "(localdb)\mssqllocaldb" -Q "BACKUP DATABASE GymFlowProDb TO DISK='D:\Backup\GymFlowProDb.bak'"

# Restore from backup
sqlcmd -S "(localdb)\mssqllocaldb" -Q "RESTORE DATABASE GymFlowProDb FROM DISK='D:\Backup\GymFlowProDb.bak'"
```

---

### Q: How do I enable query logging?

**A:**

```csharp
// In Program.cs
var logger = LoggerFactory.Create(builder => builder.AddConsole())
    .CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

options.LogTo(logger.Log)
       .EnableDetailedErrors()
       .EnableSensitiveDataLogging(); // Dev only!
```

---

## Getting Help

If your issue isn't listed here:

1. **Check the logs:**
   ```powershell
   # Full console output (contains error details)
   dotnet run --project GMS.Api 2>&1 | tee debug.log
   ```

2. **Check the trace ID:**
   ```
   Every error response includes "traceId"
   Search logs for this trace ID
   ```

3. **Enable debug mode:**
   ```json
   {
     "Logging": {
       "LogLevel": { "Default": "Debug" }
     }
   }
   ```

4. **Review source code:**
   - Controllers: `GMS.Api/Controllers/`
   - Services: `GMS.Application/Services/`
   - Entities: `GMS.Core/Entities/`

---

**Last Updated:** May 2, 2026  
**Version:** 1.0.0

