# 🚀 GymFlowPro API - Quick Start Guide

## ✅ Database Seeding - AUTOMATED

The DataSeeder has been integrated into `Program.cs` and runs automatically on startup in **development environment only**.

### What Gets Seeded Automatically

```
✅ Roles (3)
   - Owner
   - Manager
   - Trainer

✅ Tenant (1)
   - Name: Iron Zone Gym
   - GymCode: GYM-TEST-01
   - Email: info@ironzone.test

✅ Users (3)
   - owner@gymflow.test / Test@1234 (Role: Owner)
   - manager@gymflow.test / Test@1234 (Role: Manager)
   - trainer@gymflow.test / Test@1234 (Role: Trainer)

✅ Membership Plans (3)
   1. Monthly Unlimited (30 days, 500 EGP)
   2. Session Pack 20 (90 days, 800 EGP)
   3. Morning Pass (6 AM - 12 PM, 30 days, 300 EGP)

✅ Sample Member (1)
   - Name: Karim Member
   - Email: karim@gymflow.test
   - Status: Active membership (Monthly Unlimited, 30 days)
```

---

## 🎯 Step-by-Step Testing

### Step 1: Start the Application
```bash
cd D:\GMS\GMS\
dotnet run
```

**Expected Console Output**:
```
🌱 Starting database seed...
✅ Roles seeded
✅ Tenant seeded
✅ Users seeded
✅ Membership plans seeded
✅ Sample member seeded
🎉 Database seed completed successfully!

📝 Test Credentials:
   Owner:   owner@gymflow.test / Test@1234
   Manager: manager@gymflow.test / Test@1234
   Trainer: trainer@gymflow.test / Test@1234
   Gym Code: GYM-TEST-01
```

### Step 2: Open Postman
1. Download & Open Postman
2. Click **File** → **Import**
3. Choose **GymFlowPro_API.postman_collection.json** (in workspace root)

### Step 3: Login as Owner
1. In Postman, go to **Auth** folder
2. Click **Login as Owner** request
3. Click **Send** (blue button)
4. ✅ Verify response: 200 OK with JWT token
5. Token automatically saved to environment variable `TOKEN`

### Step 4: Test All Endpoints
1. **Membership Plans** folder
   - ✅ List All Plans → 200 OK
   - ✅ Get Plan Details → 200 OK
   - ✅ Create New Plan → 201 Created
   - ✅ Update Plan → 200 OK
   - ⚠️ Delete Plan → 409 Conflict (has active member "Karim")

2. **Memberships** folder
   - ✅ Get Current Membership → 200 OK (Karim's membership)
   - ✅ Get Membership History → 200 OK (paginated)
   - ✅ Assign Membership → 201 Created (new member)
   - ✅ Renew Membership → 201 Created

3. **Admin - Staff Management** folder
   - ✅ List All Staff → 200 OK (Manager, Trainer)
   - ✅ Get Staff Details → 200 OK
   - ✅ Create New Staff → 201 Created
   - ✅ Update Staff → 200 OK
   - ✅ Reset Staff Password → 200 OK
   - ✅ Delete Staff → 200 OK

4. **Tenant Settings** folder
   - ✅ Get Tenant Settings → 200 OK
   - ✅ Update Settings → 200 OK
   - ✅ Get Gym Code → 200 OK
   - ✅ Get QR Poster → 200 OK
   - ✅ Update Invitation Quotas → 200 OK

### Step 5: Test Authorization (RBAC)
1. **Login as Manager**
   - Go to **Auth** folder
   - Click **Login as Manager** request
   - Token saved to `MANAGER_TOKEN`

2. **Authorization Tests** folder
   - ✅ Manager Cannot Create Plan → 403 Forbidden
   - ✅ Trainer Can List Plans → 200 OK

---

## 📊 Test Results Checklist

### ✅ Happy Path
- [ ] Login as Owner → 200 + JWT token
- [ ] Login as Manager → 200 + JWT token
- [ ] Login as Trainer → 200 + JWT token
- [ ] List plans (Manager) → 200 OK
- [ ] Get plan details → 200 OK
- [ ] Create plan (Owner) → 201 Created
- [ ] Update plan (Owner) → 200 OK
- [ ] Get current membership → 200 OK
- [ ] Get membership history → 200 OK + pagination
- [ ] Assign membership → 201 Created
- [ ] Renew membership → 201 Created
- [ ] List staff (Owner) → 200 OK
- [ ] Create staff (Owner) → 201 Created
- [ ] Get settings (Owner) → 200 OK

### ✅ Authorization Tests
- [ ] Owner can create plans → 201 ✅
- [ ] Manager cannot create plans → 403 ✅
- [ ] Trainer can list plans → 200 ✅
- [ ] Trainer cannot create plans → 403 ✅
- [ ] Staff cannot access settings → 403 ✅

### ✅ Conflict Tests
- [ ] Delete plan with active member → 409 ✅
- [ ] Assign second active membership → 409 ✅
- [ ] Create duplicate email staff → 400 ✅

### ✅ Business Logic Tests
- [ ] Renewal has continuous timeline (no gaps)
- [ ] Membership status = "active" (cash) or "pending" (gateway)
- [ ] Plan type validation (session count, time restrictions)
- [ ] Soft delete (plan still visible in DB, IsActive = false)

---

## 🔧 Troubleshooting

### Issue: "Connection string not found"
**Solution**: Check `GMS.Api/appsettings.json` has valid connection string
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=...;Database=GymFlowProDb;..."
}
```

### Issue: SSL Certificate Error
**Solution**: Bypass SSL in Postman
1. Postman → File → Settings
2. SSL certificate verification → OFF (for development)

### Issue: Empty Database After Seeding
**Solution**: Check if running in Development mode
1. Ensure `ASPNETCORE_ENVIRONMENT=Development`
2. Seeder only runs in development
3. Check console output for seed status

### Issue: Token Expired
**Solution**: Re-login to get fresh token
1. Click "Login as Owner" again
2. New token will be saved automatically

### Issue: Member ID / Plan ID Not Found
**Solution**: Variables must be populated
1. Copy ID from a List request response
2. Paste into Postman variable (PLAN_ID, MEMBER_ID, STAFF_ID)
3. Or use pre-seeded IDs if known

---

## 📝 File Changes Made

### 1. **GMS.Infrastructure/InfrastructureServiceExtensions.cs**
- Added: `services.AddScoped<DataSeeder>();`

### 2. **GMS.Api/Program.cs**
- Added: `using GMS.Infrastructure.Persistence;`
- Added: Database seeding block after `app.Build()`
  ```csharp
  if (app.Environment.IsDevelopment())
  {
      using (var scope = app.Services.CreateScope())
      {
          var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
          seeder.SeedAsync().GetAwaiter().GetResult();
      }
  }
  ```

### 3. **GMS.Infrastructure/Persistence/DataSeeder.cs**
- Created: 207-line comprehensive seeder
- Handles: Roles, Tenant, Users, Plans, Sample Member

### 4. **GymFlowPro_API.postman_collection.json** (NEW)
- Complete Postman collection
- 20+ pre-configured requests
- Environment variables
- Authorization tests
- Test scripts with assertions

### 5. **POSTMAN_TESTING_GUIDE.md** (NEW)
- Complete API documentation
- All endpoints with examples
- Request/response samples
- Testing scenarios
- Postman setup instructions

---

## 🎓 Understanding the Architecture

### Request Flow
```
Client
  ↓
[POST /api/auth/login] → Gets JWT token
  ↓
[Authenticated Request with Bearer token]
  ↓
AuthenticationMiddleware → Validates JWT
  ↓
TenantMiddleware → Sets tenant context (from claims)
  ↓
AuthorizationMiddleware → Checks policy (OwnerOnly, AnyStaff, etc.)
  ↓
Controller → Validates input with FluentValidation
  ↓
Service → Business logic + logging
  ↓
Repository → Data access with EF Core
  ↓
Database → Multi-tenant, soft delete, audit trails
  ↓
Result<T> → Success or error response
```

### Data Isolation (Multi-Tenancy)
```
All queries automatically filtered by TenantId:

SELECT * FROM MembershipPlans 
WHERE TenantId = @CurrentTenantId AND IsActive = 1
```

---

## 🚀 Next Steps

### After Verification
1. ✅ Run all Postman requests
2. ✅ Verify all endpoints respond correctly
3. ✅ Check authorization policies work
4. ✅ Confirm continuous timeline in renewals
5. ✅ Test error scenarios (409, 403, 400)

### Before Production
1. Change test credentials in seeder
2. Set appropriate JWT expiry times
3. Enable HTTPS certificate validation
4. Configure production database
5. Set rate limiting appropriately
6. Review audit logs and monitoring
7. Load testing
8. Security testing

---

## 📞 API Base URL
```
Development: https://localhost:5001
Swagger UI: https://localhost:5001/ (after /swagger redirect)
Health Check: https://localhost:5001/health
```

---

## 🎉 Ready to Test!

All prerequisites are set up. Run the application, import the Postman collection, and start testing!

**Questions?** Check POSTMAN_TESTING_GUIDE.md for complete endpoint documentation.
