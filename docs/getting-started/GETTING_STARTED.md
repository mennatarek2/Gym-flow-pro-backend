# 🚀 GymFlowPro Quick Start Guide

**Version:** 1.0  
**Target:** New developers getting started with GymFlowPro  
**.NET 8 | ASP.NET Core | SQL Server**

---

## ⚡ 5-Minute Quick Start

### Step 1: Prerequisites Check ✅

```powershell
# Verify .NET 8 installed
dotnet --version
# Output should be: 8.0.x or higher

# Verify SQL Server (LocalDB)
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT @@VERSION"
# Should connect successfully
```

### Step 2: Clone & Navigate ✅

```powershell
cd D:\GMS\GMS
ls
# Should see: GMS.Api, GMS.Application, GMS.Infrastructure, GMS.Core, GMS.sln
```

### Step 3: Restore & Build ✅

```powershell
dotnet restore
dotnet build
# Should complete without errors
```

### Step 4: Setup Database ✅

```powershell
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
# Output: "Done" = Success ✅
```

### Step 5: Run API ✅

```powershell
dotnet run --project GMS.Api
# Output: "Now listening on: https://localhost:5001"
```

### Step 6: Open Swagger UI ✅

```
https://localhost:5001/swagger/ui
```

**You're done! 🎉**

---

## 📁 Project Structure at a Glance

```
GMS.sln
├── GMS.Core/              ← Domain Entities (NEVER has UI/DB dependencies)
│   ├── Entities/          - Tenant, GymMember, Membership, etc.
│   ├── Enums/             - GymMembershipType
│   └── Interfaces/        - IRepository, ITenantContext
│
├── GMS.Infrastructure/    ← Data Access & External Services
│   ├── Persistence/       - EF Core DbContext & Migrations
│   ├── Repositories/      - Generic Repository<T>
│   └── Services/          - Payment gateways, Tenant context
│
├── GMS.Application/       ← Business Logic
│   ├── Services/          - AuthService, CheckinService, etc.
│   ├── Interfaces/        - IAuthService, ICheckinService, etc.
│   └── DTOs/              - Request/Response objects
│
└── GMS.Api/               ← HTTP API Layer
    ├── Controllers/       - AuthController, AttendanceController, etc.
    ├── Middleware/        - TenantMiddleware
    ├── Hubs/              - SignalR (real-time notifications)
    ├── Program.cs         - DI container, middleware setup
    └── appsettings.*.json - Configuration
```

---

## 🔧 Common Commands

### Development

```powershell
# Run API (dev mode)
dotnet run --project GMS.Api

# Debug
dotnet run --project GMS.Api --configuration Debug

# Rebuild
dotnet clean && dotnet build

# Restore NuGet packages
dotnet restore
```

### Database

```powershell
# Create new migration
dotnet ef migrations add MigrationName --project GMS.Infrastructure --startup-project GMS.Api

# Update database to latest
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# Revert migration
dotnet ef migrations remove --project GMS.Infrastructure --startup-project GMS.Api

# Drop database (careful!)
dotnet ef database drop --project GMS.Infrastructure --startup-project GMS.Api

# List all migrations
dotnet ef migrations list --project GMS.Infrastructure --startup-project GMS.Api
```

### Testing

```powershell
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=InvitationServiceTests"

# Run with verbose output
dotnet test --verbosity detailed
```

### Build & Publish

```powershell
# Release build
dotnet build -c Release

# Publish to folder
dotnet publish -c Release -o ./publish

# Check output
ls ./publish
```

---

## 🔐 Authentication Overview

### Two Authentication Flows

#### **1️⃣ Staff Login (Email + Password)**

```
[Staff Opens App]
    ↓
POST /api/auth/login
    ├─ Email: manager@gym.com
    └─ Password: SecurePass123!
    ↓
[API Returns]
    ├─ accessToken (15 min expiry)
    ├─ refreshToken (30 days expiry)
    └─ tokenType: "Bearer"
    ↓
[All Subsequent Requests]
    Authorization: Bearer YOUR_ACCESS_TOKEN
```

**Expiration:** Access token expires → Use refresh token → Get new pair

---

#### **2️⃣ Member Login (Phone + OTP)**

```
[Member Opens App]
    ↓
Step 1: POST /api/auth/member-otp
    └─ phoneNumber: +201234567890
    ↓
[SMS Sent with 6-digit OTP]
    ↓
Step 2: POST /api/auth/member-verify
    ├─ phoneNumber: +201234567890
    └─ otp: 123456
    ↓
[API Returns]
    ├─ accessToken (1 hour expiry)
    └─ refreshToken (30 days)
```

---

### JWT Token Claims

```json
{
  "sub": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "email": "manager@gym.com",
  "tenant_id": "a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6",
  "role": "Manager",
  "exp": 1714746360,
  "iat": 1714745460
}
```

### Authorization Policies

| Policy | Roles | Use Case |
|--------|-------|----------|
| `AuthenticatedMember` | Member | QR check-in, view history |
| `AnyStaff` | Staff, Manager, Admin | Search members |
| `ManagerOrAbove` | Manager, Admin | Manual check-in |
| `AdminOnly` | Admin | System config |

---

## 📍 Core Endpoints Reference

### Health Check (Public)

```bash
GET https://localhost:5001/api/health

Response: { "status": "Healthy", "timestamp": "2026-05-02T14:50:00Z" }
```

### Auth (Public)

```bash
# Staff Login
POST /api/auth/login
{
  "email": "manager@gym.com",
  "password": "YOUR_PASSWORD"
}

# Member OTP Request
POST /api/auth/member-otp
{
  "phoneNumber": "+201234567890"
}

# Member OTP Verify
POST /api/auth/member-verify
{
  "phoneNumber": "+201234567890",
  "otp": "123456"
}
```

### Attendance (Requires Auth)

```bash
# QR Check-in (Member only)
POST /api/attendance/qr-checkin
Authorization: Bearer {token}
{
  "qrCodeData": "gym-2024-001"
}

# Manual Check-in (Manager+)
POST /api/attendance/manual-checkin
Authorization: Bearer {token}
{
  "memberPhoneNumber": "+201234567890",
  "notes": "Guest check-in"
}

# Search Members (Staff+)
GET /api/attendance/search-members?searchTerm=ahmed&limit=10
Authorization: Bearer {token}
```

### Invitations (Member only)

```bash
# Send Invitation
POST /api/invitation/send
Authorization: Bearer {token}
{
  "guestName": "Fatima Ali",
  "guestPhone": "+201098765432"
}

# Get Invitation History
GET /api/invitation/history
Authorization: Bearer {token}
```

---

## 🗄️ Database Schema at a Glance

### Main Tables

| Table | Purpose | Key Field |
|-------|---------|-----------|
| `Tenants` | Gym organizations | `Id` (GUID) |
| `AppUsers` | Staff/Admin accounts | `Id`, `Email` |
| `GymMembers` | Gym members | `Id`, `PhoneNumber` |
| `MembershipPlans` | Subscription tiers | `Name` (Standard/Premium/VIP) |
| `Memberships` | Member subscriptions | `MemberId`, `StartDate`, `EndDate` |
| `GymAttendance` | Check-in logs | `MemberId`, `CheckInTime` |
| `MemberInvitations` | Guest invites | `MemberId`, `SentAt` |

### Key Relationships

```
Tenant (1) ──────┐
                 ├──(N) GymMembers
                 ├──(N) AppUsers
                 └──(N) Memberships

GymMember (1) ───┬──(N) Memberships (active in .ActiveMembershipId)
                 ├──(N) GymAttendance
                 └──(N) MemberInvitations

MembershipPlan (1) ──(N) Memberships
```

---

## 🔑 Configuration Files

### `appsettings.json` (Default)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Integrated Security=true;"
  },
  "Jwt": {
    "Secret": "your-256-bit-secret-key-minimum-32-characters-long",
    "Issuer": "https://localhost:5001",
    "Audience": "https://localhost:5001",
    "ExpirationMinutes": 15,
    "RefreshExpirationDays": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### `appsettings.Development.json` (Override)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

---

## 🧪 Testing Your API

### Using cURL

```bash
# 1. Login as staff
curl -X POST "https://localhost:5001/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"manager@gym.com","password": "YOUR_PASSWORD"}'

# Copy accessToken from response

# 2. Search members with token
curl -X GET "https://localhost:5001/api/attendance/search-members?searchTerm=ahmed" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

### Using Swagger UI

1. Open: `https://localhost:5001/swagger/ui`
2. Click **"Authorize"** button (top right)
3. Paste: `Bearer YOUR_ACCESS_TOKEN` (from login response)
4. Click endpoints to test

### Using Postman

1. **Create Collection** → Name: "GymFlowPro"

2. **Create Login Request:**
   - Method: `POST`
   - URL: `https://localhost:5001/api/auth/login`
   - Body (JSON):
     ```json
     {
       "email": "manager@gym.com",
       "password": "YOUR_PASSWORD"
     }
     ```

3. **Extract Token:**
   - Save response body's `accessToken` to Postman variable

4. **Test Protected Endpoints:**
   - Add header: `Authorization: Bearer YOUR_ACCESS_TOKEN`

---

## 🛠️ Troubleshooting

### Problem: "Connection string not found"

```powershell
# Solution: Check appsettings.json exists and has ConnectionStrings
cat GMS.Api\appsettings.json | findstr "DefaultConnection"
```

### Problem: "Port 5001 already in use"

```powershell
# Solution: Kill process on port 5001
netstat -ano | findstr :5001
taskkill /PID {PID} /F

# Or: Run on different port
dotnet run --project GMS.Api --launch-profile Https
```

### Problem: "Migration failed"

```powershell
# Solution: Drop and recreate database
dotnet ef database drop --project GMS.Infrastructure --startup-project GMS.Api -f
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

### Problem: HTTPS certificate warning

```powershell
# Solution: Trust development certificate
dotnet dev-certs https --trust
```

---

## 📚 Documentation Files

| Document | Purpose |
|----------|---------|
| `API_DOCUMENTATION.md` | Full API reference with all endpoints |
| `DEVELOPER_GUIDE.md` | Architecture, patterns, coding standards |
| `DATABASE_SCHEMA_REFERENCE.md` | Complete DB schema with ERD |
| `GETTING_STARTED.md` | Getting started guide |

---

## 🎯 Next Steps

### For API Development

1. ✅ Read `DEVELOPER_GUIDE.md`
2. ✅ Study `DATABASE_SCHEMA_REFERENCE.md`
3. ✅ Explore existing controllers in `GMS.Api/Controllers/`
4. ✅ Look at services in `GMS.Application/Services/`

### For Database Work

1. ✅ Understand schema in `DATABASE_SCHEMA_REFERENCE.md`
2. ✅ Create entities in `GMS.Core/Entities/`
3. ✅ Configure in `GMS.Infrastructure/Persistence/Configurations/`
4. ✅ Run migrations

### For Testing

1. ✅ Use Swagger UI: `https://localhost:5001/swagger/ui`
2. ✅ Or use cURL/Postman
3. ✅ Check response codes and error messages

---

## ☎️ Support Resources

- **ASP.NET Core Docs:** https://learn.microsoft.com/en-us/aspnet/core/
- **Entity Framework Core:** https://learn.microsoft.com/en-us/ef/core/
- **JWT Authentication:** https://jwt.io/

---

**Ready to code? 🚀 Let's go!**

**Last Updated:** May 2, 2026  
**Version:** 1.0.0
