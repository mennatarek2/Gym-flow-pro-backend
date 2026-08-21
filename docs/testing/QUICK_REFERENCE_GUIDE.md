# 🚀 QUICK REFERENCE - GymFlowPro API Guide

## All Endpoints at a Glance

### 📋 Membership Plans (5 endpoints)
```
GET    /api/membership-plans                  [AnyStaff]  → List plans
GET    /api/membership-plans/{id}             [AnyStaff]  → Get plan
POST   /api/membership-plans                  [OwnerOnly] → Create (201)
PUT    /api/membership-plans/{id}             [OwnerOnly] → Update (200)
DELETE /api/membership-plans/{id}             [OwnerOnly] → Delete (200/409)
```

### 💳 Memberships (4 endpoints)
```
GET    /api/memberships/{id}/current          [AnyStaff]  → Current membership
GET    /api/memberships/{id}/history          [AnyStaff]  → History (paginated)
POST   /api/memberships/{id}/assign           [Manager+]  → Create (201)
POST   /api/memberships/{id}/renew            [Manager+]  → Renew (200)
```

### 👥 Staff Management (6 endpoints)
```
GET    /api/admin/staff                       [OwnerOnly] → List staff
GET    /api/admin/staff/{id}                  [OwnerOnly] → Get staff
POST   /api/admin/staff                       [OwnerOnly] → Create (201)
PUT    /api/admin/staff/{id}                  [OwnerOnly] → Update (200)
DELETE /api/admin/staff/{id}                  [OwnerOnly] → Delete (200)
POST   /api/admin/staff/{id}/reset-password   [OwnerOnly] → Reset pwd (200)
```

### ⚙️ Settings (4 endpoints)
```
GET    /api/settings                          [OwnerOnly] → Get settings
PUT    /api/settings                          [OwnerOnly] → Update (200)
GET    /api/settings/gym-code                 [AnyStaff]  → Get code
GET    /api/settings/qr-poster                [AnyStaff]  → Get URL
```

---

## 🔐 Authorization Policies

| Policy | Who | Endpoints |
|--------|-----|-----------|
| `OwnerOnly` | Owner only | Staff CRUD, Settings, Plan CRUD |
| `ManagerOrAbove` | Manager, Owner | Create/Renew membership |
| `AnyStaff` | Manager, Trainer, Owner | List plans, Get membership |

---

## 📦 Request/Response Models

### Create Membership Plan
```bash
POST /api/membership-plans
{
  "name": "Monthly Unlimited",
  "nameAr": "غير محدود شهري",
  "planType": "monthly_unlimited",
  "price": 299.00,
  "durationDays": 30
}
→ 201 Created
```

### Create Staff User
```bash
POST /api/admin/staff
{
  "fullName": "Ahmed Hassan",
  "email": "ahmed@gym.com",
  "password": "YOUR_PASSWORD",
  "role": "manager"          # or "trainer" (NOT "owner")
}
→ 201 Created
```

### Assign Membership
```bash
POST /api/memberships/{memberId}/assign
{
  "planId": "550e8400-...",
  "startDate": "2026-05-03",
  "paymentMethod": "cash",    # or "paymob", "fawry", etc
  "amountPaid": 299.00
}
→ 201 Created (status="active" for cash, "pending" for gateway)
```

### Renew Membership
```bash
POST /api/memberships/{memberId}/renew
{
  "planId": null,              # null = keep same plan
  "paymentMethod": "cash",
  "amountPaid": 299.00
}
→ 200 OK (StartDate = previous EndDate - continuous!)
```

### Update Tenant Settings
```bash
PUT /api/settings
{
  "gymName": "Elite Fitness",
  "gymNameAr": "النخبة للياقة",
  "phoneNumber": "+20123456789",
  "address": "123 Main St"
}
→ 200 OK
```

---

## ✅ Status Codes

| Code | Meaning | Common Triggers |
|------|---------|-----------------|
| 200 | OK | GET, PUT, DELETE success |
| 201 | Created | POST success |
| 400 | Bad Request | Validation failure |
| 403 | Forbidden | Wrong role (e.g., Manager → OwnerOnly) |
| 404 | Not Found | Resource doesn't exist |
| 409 | Conflict | Business rule violation |

---

## 🎯 Common Scenarios

### Scenario 1: Manager Creates Staff
```bash
Manager JWT → POST /api/admin/staff
Expected: 403 Forbidden
Reason: OwnerOnly policy
```

### Scenario 2: Duplicate Active Membership
```bash
POST /api/memberships/{id}/assign (member already has active membership)
Expected: 409 Conflict
Reason: Business rule prevents 2 active memberships
```

### Scenario 3: Plan with Active Members
```bash
DELETE /api/membership-plans/{id} (plan has 5 active memberships)
Expected: 409 Conflict
Reason: Cannot delete plan with active members
```

### Scenario 4: Continuous Renewal
```bash
Member 1: Expires 2026-05-30
POST /renew
Member 1: Now starts 2026-05-30 (no gap!)
```

### Scenario 5: Email Uniqueness per Tenant
```bash
Tenant A: CREATE staff with email "test@gym.com" → OK
Tenant B: CREATE staff with email "test@gym.com" → OK (different tenant)
```

---

## 🛡️ Multi-Tenancy

All requests automatically scoped to requesting user's tenant:

```csharp
var tenantId = _tenantContext.TenantId;  // From JWT claims

// ✅ Only returns current tenant's staff
var staff = await _adminService.GetStaffUsersAsync(tenantId);

// ✅ Can't access other tenant's data
// Even if you know the ID, TenantId filter blocks it
```

---

## 🔍 Error Response Format

All errors return consistent format:

```json
{
  "error": "Short description / الوصف الموجز",
  "message": "Detailed explanation with context"
}
```

---

## 🧪 Test Commands

### List Staff
```bash
curl -X GET "https://localhost:5001/api/admin/staff" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### Create Staff
```bash
curl -X POST "https://localhost:5001/api/admin/staff" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fullName": "Test User",
    "email": "test@gym.com",
    "password": "YOUR_PASSWORD",
    "role": "manager"
  }'
```

### Get Settings
```bash
curl -X GET "https://localhost:5001/api/settings" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

---

## 📝 Plan Types & Validation

| Type | Required Fields | Validation |
|------|-----------------|-----------|
| `monthly_unlimited` | price, durationDays | N/A |
| `session_pack` | price, durationDays, sessionCount | sessionCount ∈ {10,20,50} |
| `time_limited` | price, durationDays, timeStart, timeEnd | End > Start |
| `pt_credits` | price, durationDays, sessionCount | sessionCount > 0 |
| `family` | price, durationDays, invitationQuota | quota >= 0 |

---

## 🔄 Membership Status Transitions

```
[Created]
  ├─ Cash → [Active] immediately
  └─ Gateway → [Pending] until webhook

[Active]
  ├─ Expired (EndDate passed) → [Expired]
  └─ Frozen → [Frozen]

[Expired]
  └─ Renew → [Active] (new membership)
```

---

## 💾 Database Relationships

```
Tenant
  ├─ MembershipPlans (1:N)
  ├─ Memberships (1:N)
  ├─ ApplicationUsers / Staff (1:N)
  └─ Settings (implicit)

MembershipPlan
  └─ Memberships (1:N)

ApplicationUser (Identity)
  └─ RefreshTokens (1:N)
```

---

## ⚠️ Common Mistakes

❌ **Don't**: Use global email uniqueness  
✅ **Do**: Filter by TenantId for uniqueness  

❌ **Don't**: Create owner role via AdminController  
✅ **Do**: Use only "manager" or "trainer"  

❌ **Don't**: Hard delete staff or plans  
✅ **Do**: Use soft delete (sets IsActive=false)  

❌ **Don't**: Forget to revoke tokens on deactivation  
✅ **Do**: Let AdminService handle it automatically  

❌ **Don't**: Query without TenantId filter  
✅ **Do**: Let service layer auto-scope by tenant  

---

## 🚀 Deployment Checklist

- [ ] Build successful: `dotnet build`
- [ ] Run migrations: `dotnet ef database update`
- [ ] Verify endpoints in Swagger
- [ ] Test with owner JWT token
- [ ] Test authorization (403 for non-owners)
- [ ] Monitor logs for errors
- [ ] Check multi-tenancy isolation

---

## 📞 Support

### Documentation
- Detailed Guide: `ADMIN_AND_SETTINGS_IMPLEMENTATION.md`
- API Spec: `MEMBERSHIP_PLANS_IMPLEMENTATION.md`
- Verification: `FINAL_TECHNICAL_VERIFICATION.md`

### Quick Links
- Build Status: Run `dotnet build`
- Run Tests: Use Visual Studio Test Explorer
- View Logs: Check application output window

---

**Last Updated**: May 5, 2026  
**Version**: 1.0.0  
**Status**: ✅ Production Ready
