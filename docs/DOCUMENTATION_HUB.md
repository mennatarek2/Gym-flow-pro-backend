# 📚 GymFlowPro Documentation Hub

**Complete Documentation Suite for GymFlowPro API**  
**.NET 8 | ASP.NET Core | SQL Server | Multi-tenant Gym Management System**

---

## 📖 Documentation Overview

This suite provides comprehensive documentation for the GymFlowPro project. Choose the document that matches your need:

---

## 🚀 Getting Started (Start Here!)

### **[GETTING_STARTED.md](./GETTING_STARTED.md)**
**For:** First-time developers  
**Duration:** 5-10 minutes

**Contains:**
- ✅ 5-minute quick start setup
- ✅ Project structure overview
- ✅ Common PowerShell commands
- ✅ Authentication flow basics
- ✅ Key API endpoints
- ✅ Database schema overview
- ✅ Basic troubleshooting

**When to use:**
- You're new to the project
- You need to set up development environment
- You want quick reference for common tasks

---

## 📚 API Documentation

### **[API_DOCUMENTATION.md](./API_DOCUMENTATION.md)**
**For:** API consumers, frontend developers, testers  
**Duration:** 20-30 minutes to read, ongoing reference

**Contains:**
- ✅ Complete API endpoint reference
- ✅ All 7 endpoints with examples (Auth, Attendance, Invitations, Payments, Health)
- ✅ Request/response examples with curl, JavaScript, etc.
- ✅ Standard error responses
- ✅ Rate limiting details
- ✅ DTO reference (all request/response objects)
- ✅ SignalR real-time events
- ✅ Integration examples

**When to use:**
- You're calling the API from a client app
- You need endpoint specifications
- You're writing tests or integration code
- You need to understand request/response formats

**Example endpoints covered:**
```
POST /api/auth/login
POST /api/auth/member-otp
POST /api/attendance/qr-checkin
POST /api/attendance/manual-checkin
GET  /api/attendance/search-members
POST /api/invitation/send
GET  /api/invitation/history
POST /api/payments/paymob-webhook
POST /api/payments/fawry-webhook
GET  /api/health
```

---

## 👨‍💻 Developer Guide

### **[DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md)**
**For:** Backend developers, architects  
**Duration:** 30-45 minutes to read, ongoing reference

**Contains:**
- ✅ Full project structure breakdown
- ✅ Layered architecture explanation
- ✅ Design patterns used (Repository, DI, Result, Multi-tenancy, SignalR)
- ✅ Development environment setup
- ✅ Coding standards & conventions
- ✅ File naming, namespaces, class structure
- ✅ Async/await patterns
- ✅ Error handling best practices
- ✅ Database layer details (EF Core, Migrations, DbContext)
- ✅ Application layer patterns (Services, DTOs, Interfaces)
- ✅ API layer structure (Controllers, Base classes, Helpers)
- ✅ Common patterns (Multi-tenancy, Authentication, Rate Limiting)
- ✅ Testing structure
- ✅ Deployment checklist

**When to use:**
- You're developing new features
- You're working with the codebase
- You need to understand architecture decisions
- You're writing services or controllers
- You want to follow project conventions
- You're preparing for deployment

**Key patterns explained:**
- Repository pattern with generic `Repository<T>`
- Dependency Injection container
- Result pattern for error handling
- Multi-tenancy with `TenantMiddleware`
- JWT authentication flow
- Rate limiting implementation

---

## 🗄️ Database Schema Reference

### **[DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md)**
**For:** Database administrators, backend developers  
**Duration:** 20-30 minutes to read, ongoing reference

**Contains:**
- ✅ Entity Relationship Diagram (ERD)
- ✅ All 7 table definitions with column details
- ✅ Data types, constraints, nullable flags
- ✅ Foreign key relationships
- ✅ Cascade delete behavior
- ✅ Performance indexes
- ✅ Business rules & validation
- ✅ SQL queries for common operations
- ✅ Migration history

**Tables documented:**
1. `Tenants` - Gym organizations (multi-tenancy)
2. `AppUsers` - Staff/Admin accounts (ASP.NET Identity)
3. `GymMembers` - Gym members/customers
4. `MembershipPlans` - Subscription tiers (Standard, Premium, VIP)
5. `Memberships` - Individual member subscriptions
6. `GymAttendance` - Check-in logs
7. `MemberInvitations` - Guest invitation tracking

**When to use:**
- You need to understand database structure
- You're writing database queries
- You need to create migrations
- You're troubleshooting database issues
- You need to understand constraints and rules
- You're optimizing database performance

**Key business rules:**
- Monthly invitation quota: 3 per member, resets monthly
- Membership session limit: tracked per plan
- Frozen membership blocks check-in
- Payment gateway idempotency via ExternalRef

---

## 🔧 Troubleshooting & FAQ

### **[TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md)**
**For:** Everyone (especially troubleshooting issues)  
**Duration:** 5-60 minutes depending on issue

**Contains:**
- ✅ Common setup issues with solutions
- ✅ Database problems & fixes
- ✅ Authentication errors
- ✅ API runtime errors
- ✅ Performance issues
- ✅ Deployment issues
- ✅ 20+ FAQ items with answers

**Issues covered:**
- .NET 8 SDK not found
- SQL Server connection failed
- Port 5001 already in use
- HTTPS certificate errors
- Migrations not applied
- 401/403/429/500 errors
- Memory leaks
- CORS issues
- Certificate expiration

**When to use:**
- Something broke and you need quick fix
- You're getting an error message
- You have a common question
- You need troubleshooting steps
- You're deploying to production

---

## 🏗️ Architecture & Standards

### **Quick Reference Tables:**

#### **HTTP Status Codes**
```
200 OK                  ✅ Success
400 Bad Request         ❌ Invalid input
401 Unauthorized        🔒 Missing/invalid auth
403 Forbidden          🚫 Insufficient permissions
429 Too Many Requests   ⏱️  Rate limit exceeded
500 Server Error        💥 Internal error
```

#### **Authentication Flows**
```
Staff:       Email + Password → JWT (15 min) + Refresh (30 days)
Member:      Phone + OTP → JWT (1 hour) + Refresh (30 days)
```

#### **Authorization Policies**
```
AuthenticatedMember  → Members only
AnyStaff            → Staff/Manager/Admin
ManagerOrAbove      → Manager/Admin
AdminOnly           → Admin only
```

#### **Database Relationships**
```
Tenant (1) ─────N─ GymMembers
Tenant (1) ─────N─ AppUsers
Tenant (1) ─────N─ Memberships

GymMember (1) ─────N─ Memberships
GymMember (1) ─────N─ GymAttendance
GymMember (1) ─────N─ MemberInvitations

MembershipPlan (1) ─────N─ Memberships
```

---

## 📋 Document Quick Links

| Document | Purpose | Audience | Time |
|----------|---------|----------|------|
| [GETTING_STARTED.md](./GETTING_STARTED.md) | Quick setup & basics | All developers | 5-10 min |
| [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) | API reference & examples | API consumers, Testers | 20-30 min |
| [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) | Architecture & coding standards | Backend developers | 30-45 min |
| [DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md) | Database schema & queries | DBAs, Backend devs | 20-30 min |
| [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) | Problem solving | Everyone | 5-60 min |

---

## 🎯 Getting Started by Role

### 👨‍💼 Project Manager
1. Read overview of this document
2. Understand core entities: Members, Memberships, Attendance, Invitations
3. Reference [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) for feature overview

### 🧑‍💻 Frontend Developer
1. Start with [GETTING_STARTED.md](./GETTING_STARTED.md)
2. Read [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) for all endpoints
3. Check [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) for 401/403 errors

### 🏗️ Backend Developer
1. Start with [GETTING_STARTED.md](./GETTING_STARTED.md)
2. Read [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) for architecture
3. Reference [DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md)
4. Keep [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) handy

### 🗄️ Database Administrator
1. Start with [DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md)
2. Understand backup/restore procedures
3. Monitor performance indexes
4. Check [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) for DB issues

### 🚀 DevOps / Deployment
1. Read [GETTING_STARTED.md](./GETTING_STARTED.md) setup section
2. Reference deployment section in [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md)
3. Check [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) deployment issues
4. Use [DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md) for migrations

### 🧪 QA / Tester
1. Start with [GETTING_STARTED.md](./GETTING_STARTED.md)
2. Use [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) for test cases
3. Reference [TROUBLESHOOTING_FAQ.md](./TROUBLESHOOTING_FAQ.md) for common errors
4. Check rate limiting in [API_DOCUMENTATION.md](./API_DOCUMENTATION.md)

---

## 💾 Project Structure

```
D:\GMS\GMS\
├── GMS.Api/                    ← API Layer
├── GMS.Application/            ← Business Logic
├── GMS.Infrastructure/         ← Data Access
├── GMS.Core/                   ← Domain Entities
├── GMS.sln
│
├── GETTING_STARTED.md          ← 📖 Start here!
├── API_DOCUMENTATION.md        ← 📚 API Reference
├── DEVELOPER_GUIDE.md          ← 👨‍💻 Architecture & Coding
├── DATABASE_SCHEMA_REFERENCE.md ← 🗄️ Database Design
├── TROUBLESHOOTING_FAQ.md      ← 🔧 Problem Solving
└── DOCUMENTATION_HUB.md        ← This file (index)
```

---

## 🔗 External Resources

### Official .NET Documentation
- **ASP.NET Core:** https://learn.microsoft.com/en-us/aspnet/core/
- **Entity Framework Core:** https://learn.microsoft.com/en-us/ef/core/
- **.NET 8:** https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8

### Security & Authentication
- **JWT.io:** https://jwt.io/ (Decode tokens)
- **RFC 7519:** https://tools.ietf.org/html/rfc7519 (JWT spec)
- **OAuth 2.0:** https://oauth.net/2/

### Tools & Testing
- **Swagger/OpenAPI:** https://swagger.io/
- **Postman:** https://www.postman.com/
- **cURL:** https://curl.se/

---

## ✅ Documentation Checklist

This documentation suite covers:

- ✅ Project structure and organization
- ✅ Layered architecture explanation
- ✅ All 7 database tables and relationships
- ✅ All 10 API endpoints with examples
- ✅ Authentication flows (Staff JWT + Member OTP)
- ✅ Authorization policies and roles
- ✅ Deployment procedures
- ✅ Common issues and solutions
- ✅ FAQ for frequent questions
- ✅ Design patterns used
- ✅ Coding standards and conventions
- ✅ Database migration procedures
- ✅ Performance optimization tips
- ✅ Troubleshooting guides

---

## 📞 Support

### Internal Resources
- **Project Lead:** [Contact]
- **Slack Channel:** #gymflowpro-dev
- **Wiki:** [Internal Wiki]
- **Issue Tracker:** [GitHub Issues]

### External Resources
- **Stack Overflow:** Tag: `asp.net-core`, `entity-framework-core`
- **GitHub Discussions:** [Project Repo]
- **Microsoft Q&A:** https://docs.microsoft.com/en-us/answers/

---

## 📝 Documentation Versions

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | May 2, 2026 | Initial comprehensive documentation suite |

---

## 🎓 Training Path

### Beginner (Week 1)
1. [GETTING_STARTED.md](./GETTING_STARTED.md) - Setup & basics
2. [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) - Learn endpoints
3. Test endpoints using Swagger UI

### Intermediate (Week 2-3)
1. [DEVELOPER_GUIDE.md](./DEVELOPER_GUIDE.md) - Architecture deep-dive
2. [DATABASE_SCHEMA_REFERENCE.md](./DATABASE_SCHEMA_REFERENCE.md) - DB design
3. Write first feature/controller

### Advanced (Week 4+)
1. Implement complex features
2. Optimize performance
3. Deploy to production
4. Reference all docs as needed

---

## 🏁 Quick Start Summary

```powershell
# 1. Setup (5 min)
cd D:\GMS\GMS
dotnet restore
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# 2. Run (2 min)
dotnet run --project GMS.Api

# 3. Test (2 min)
# Open: https://localhost:5001/swagger/ui
# Click Authorize and test endpoints

# Done! 🎉
```

---

**Documentation Version:** 1.0.0  
**Last Updated:** May 2, 2026  
**Status:** ✅ Complete

**Ready to build amazing things? Start with [GETTING_STARTED.md](./GETTING_STARTED.md)! 🚀**

