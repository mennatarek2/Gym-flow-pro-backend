# 🧪 GymFlowPro API - cURL Testing Commands

## Quick Copy-Paste Testing

### Prerequisites
```bash
# Set variables
$BASE_URL = "https://localhost:5001"
$OWNER_EMAIL = "owner@gymflow.test"
$OWNER_PASSWORD = "Test@1234"
$MANAGER_EMAIL = "manager@gymflow.test"
$MANAGER_PASSWORD = "Test@1234"
```

---

## 1️⃣ AUTHENTICATION

### Login as Owner (Get Token)
```bash
curl -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "owner@gymflow.test",
    "password": "YOUR_PASSWORD"
  }' \
  --insecure
```

**Response** (200 OK):
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "user": {"id": "...", "email": "owner@gymflow.test", "role": "Owner"}
}
```

### Login as Manager
```bash
curl -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "manager@gymflow.test",
    "password": "YOUR_PASSWORD"
  }' \
  --insecure
```

---

## 2️⃣ MEMBERSHIP PLANS

### List All Plans (AnyStaff)
```bash
curl -X GET "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + 3 plans (Monthly, Session Pack 20, Morning Pass)

### Get Plan Details
```bash
# First, get a plan ID from list request
# Then replace PLAN_ID in the URL

curl -X GET "$BASE_URL/api/membership-plans/PLAN_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + plan details with membership counts

### Create New Plan (OwnerOnly)
```bash
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Platinum Package",
    "nameAr": "الحزمة البلاتينية",
    "description": "All access premium",
    "descriptionAr": "وصول كامل متميز",
    "planType": "monthly_unlimited",
    "price": 999,
    "durationDays": 30,
    "invitationQuota": 5
  }' \
  --insecure
```

**Expected**: 201 Created + new plan object

### Update Plan (OwnerOnly)
```bash
curl -X PUT "$BASE_URL/api/membership-plans/PLAN_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Updated Platinum",
    "nameAr": "البلاتينية المحدثة",
    "description": "Updated description",
    "descriptionAr": "الوصف المحدث",
    "planType": "monthly_unlimited",
    "price": 1099,
    "durationDays": 30,
    "invitationQuota": 6
  }' \
  --insecure
```

**Expected**: 200 OK + updated plan

### Delete Plan (OwnerOnly)
```bash
curl -X DELETE "$BASE_URL/api/membership-plans/PLAN_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

**Expected**: 200 OK (if no active members) or 409 Conflict (if active members)

---

## 3️⃣ MEMBERSHIPS

### Get Current Membership
```bash
# Member ID = ID of a GymMember (e.g., Karim)
# Get member ID from a members list endpoint or database

curl -X GET "$BASE_URL/api/memberships/MEMBER_ID/current" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + current membership (Karim's active monthly plan)

### Get Membership History (Paginated)
```bash
curl -X GET "$BASE_URL/api/memberships/MEMBER_ID/history?page=1&pageSize=10" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + paginated membership history

### Assign Membership (ManagerOrAbove)
```bash
# Create new member in database first or use existing member
# Then assign a plan to them

curl -X POST "$BASE_URL/api/memberships/MEMBER_ID/assign" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planId": "PLAN_ID",
    "paymentMethod": "cash"
  }' \
  --insecure
```

**Payment Methods**: `"cash"`, `"paymob"`, `"fawry"`
**Expected**: 201 Created + membership object
**Error**: 409 Conflict if member already has active membership

### Renew Membership (ManagerOrAbove)
```bash
# Renews membership with continuous timeline
# Next StartDate = Previous EndDate

curl -X POST "$BASE_URL/api/memberships/MEMBER_ID/renew" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planId": "NEW_PLAN_ID",
    "paymentMethod": "cash"
  }' \
  --insecure
```

**Expected**: 201 Created + renewed membership (no gap between dates)

---

## 4️⃣ ADMIN - STAFF MANAGEMENT

### List All Staff (OwnerOnly)
```bash
curl -X GET "$BASE_URL/api/admin/staff" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + list of staff (Manager, Trainer, excludes Owner)

### Get Staff Details (OwnerOnly)
```bash
curl -X GET "$BASE_URL/api/admin/staff/STAFF_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + staff details

### Create New Staff (OwnerOnly)
```bash
curl -X POST "$BASE_URL/api/admin/staff" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "newstaffmember@gymflow.test",
    "firstName": "John",
    "lastName": "Trainer",
    "role": "Trainer",
    "password": "YOUR_PASSWORD"
  }' \
  --insecure
```

**Valid Roles**: `"Manager"`, `"Trainer"` (NOT "Owner")
**Expected**: 201 Created + new staff object
**Error**: 400 Bad Request if email already exists in tenant

### Update Staff (OwnerOnly)
```bash
curl -X PUT "$BASE_URL/api/admin/staff/STAFF_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "Jane",
    "lastName": "SuperTrainer"
  }' \
  --insecure
```

**Expected**: 200 OK + updated staff

### Reset Staff Password (OwnerOnly)
```bash
curl -X POST "$BASE_URL/api/admin/staff/STAFF_ID/reset-password" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "newPassword": "ResetPassword@789"
  }' \
  --insecure
```

**Expected**: 200 OK + confirmation message

### Delete Staff (OwnerOnly - Soft Delete)
```bash
curl -X DELETE "$BASE_URL/api/admin/staff/STAFF_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

**Expected**: 200 OK + confirmation message

---

## 5️⃣ TENANT SETTINGS

### Get Tenant Settings (OwnerOnly)
```bash
curl -X GET "$BASE_URL/api/settings" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + tenant settings (Iron Zone Gym)

### Update Tenant Settings (OwnerOnly)
```bash
curl -X PUT "$BASE_URL/api/settings" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "gymName": "Iron Zone Elite",
    "gymNameAr": "صالة حديد زون النخبة",
    "city": "Cairo",
    "address": "456 Elite Street",
    "phone": "+201555555555",
    "email": "elite@ironzone.test"
  }' \
  --insecure
```

**Expected**: 200 OK + updated settings

### Get Gym Code (AnyStaff)
```bash
curl -X GET "$BASE_URL/api/settings/gym-code" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + `{"gymCode": "GYM-TEST-01"}`

### Get QR Poster URL (AnyStaff)
```bash
curl -X GET "$BASE_URL/api/settings/qr-poster" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK + `{"qrPosterUrl": "https://localhost:5001/uploads/..."}`

### Update Invitation Quotas (OwnerOnly)
```bash
curl -X PUT "$BASE_URL/api/settings/invitation-quotas" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "monthlyUnlimited": 4,
    "sessionPack": 2,
    "timeLimited": 1,
    "ptCredits": 0,
    "family": 5
  }' \
  --insecure
```

**Expected**: 200 OK + updated quotas

---

## 🔐 AUTHORIZATION TESTS

### Test 1: Manager Cannot Create Plan (403)
```bash
# First login as manager and get their token

# Then try to create a plan
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Plan",
    "nameAr": "خطة اختبار",
    "planType": "monthly_unlimited",
    "price": 500,
    "durationDays": 30
  }' \
  --insecure
```

**Expected**: 403 Forbidden ✅

### Test 2: Trainer Can List Plans (200)
```bash
# First login as trainer and get their token

curl -X GET "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Accept: application/json" \
  --insecure
```

**Expected**: 200 OK ✅

### Test 3: Trainer Cannot Create Plan (403)
```bash
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test",
    "nameAr": "اختبار",
    "planType": "monthly_unlimited",
    "price": 500,
    "durationDays": 30
  }' \
  --insecure
```

**Expected**: 403 Forbidden ✅

---

## ❌ ERROR SCENARIOS

### Test: Delete Plan with Active Members (409)
```bash
# Get a plan ID with active members (e.g., Karim's monthly plan)
# Then try to delete it

curl -X DELETE "$BASE_URL/api/membership-plans/PLAN_ID" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

**Expected**: 409 Conflict
```json
{
  "error": "Cannot delete plan with 1 active memberships",
  "message": "This plan has 1 active members"
}
```

### Test: Assign Second Active Membership (409)
```bash
# Try to assign a different plan to Karim (who already has active membership)

curl -X POST "$BASE_URL/api/memberships/KARIM_ID/assign" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planId": "DIFFERENT_PLAN_ID",
    "paymentMethod": "cash"
  }' \
  --insecure
```

**Expected**: 409 Conflict
```json
{
  "error": "Member already has an active membership / العضو لديه عضوية نشطة بالفعل"
}
```

### Test: Invalid Plan Type (400)
```bash
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Bad Plan",
    "nameAr": "خطة سيئة",
    "planType": "invalid_type",
    "price": 500,
    "durationDays": 30
  }' \
  --insecure
```

**Expected**: 400 Bad Request
```json
{
  "errors": {
    "planType": ["Plan type 'invalid_type' is not valid"]
  }
}
```

### Test: Negative Price (400)
```bash
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Cheap Plan",
    "nameAr": "خطة رخيصة",
    "planType": "monthly_unlimited",
    "price": -100,
    "durationDays": 30
  }' \
  --insecure
```

**Expected**: 400 Bad Request
```json
{
  "errors": {
    "price": ["Price must be greater than 0"]
  }
}
```

### Test: Session Pack with Invalid SessionCount (400)
```bash
curl -X POST "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Bad Sessions",
    "nameAr": "جلسات سيئة",
    "planType": "session_pack",
    "price": 500,
    "durationDays": 60,
    "sessionCount": 15
  }' \
  --insecure
```

**Expected**: 400 Bad Request
```json
{
  "errors": {
    "sessionCount": ["Session count must be 10, 20, or 50"]
  }
}
```

---

## 🔄 FULL WORKFLOW EXAMPLE

```bash
#!/bin/bash

BASE_URL="https://localhost:5001"

echo "🔓 Step 1: Login as Owner..."
LOGIN_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "owner@gymflow.test",
    "password": "YOUR_PASSWORD"
  }' \
  --insecure)

TOKEN=$(echo $LOGIN_RESPONSE | jq -r '.token')
echo "✅ Token: ${TOKEN:0:20}..."

echo ""
echo "📋 Step 2: List all plans..."
curl -s -X GET "$BASE_URL/api/membership-plans" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  --insecure | jq '.[0] | {id, name, price}'

echo ""
echo "✅ All tests completed!"
```

---

## 💡 Tips

### Save Token to Variable (PowerShell)
```powershell
$response = Invoke-WebRequest -Uri "https://localhost:5001/api/auth/login" `
  -Method POST `
  -Headers @{"Content-Type"="application/json"} `
  -Body '{"email":"owner@gymflow.test","password": "YOUR_PASSWORD"}' `
  -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
echo "Token: $token"

# Use in next requests
$headers = @{"Authorization"="Bearer $token"}
```

### Pretty Print JSON Response
```bash
curl -s ... | jq '.'  # jq must be installed
# OR
curl -s ... | python -m json.tool
```

### Save Response to File
```bash
curl -s ... -o response.json
cat response.json | jq '.'
```

---

## ✅ Verification Checklist

Run these commands in order:

- [ ] `Login as Owner` → 200 + token
- [ ] `List All Plans` → 200 + 3 plans
- [ ] `Get Plan Details` → 200 + details
- [ ] `Create New Plan` → 201 + new plan
- [ ] `Manager Cannot Create Plan` → 403 ✅
- [ ] `Get Current Membership` → 200 + Karim's membership
- [ ] `List Staff` → 200 + Manager, Trainer
- [ ] `Get Settings` → 200 + gym details
- [ ] `Update Settings` → 200 + updated
- [ ] `Delete Plan (with members)` → 409 ✅

**All tests pass? Ready for production! 🚀**

---

**Remember**: 
- Replace `YOUR_TOKEN_HERE` with actual token from login
- Replace `PLAN_ID`, `MEMBER_ID`, `STAFF_ID` with real IDs from responses
- Use `--insecure` flag for development (self-signed SSL)
- All test credentials: username/password format
