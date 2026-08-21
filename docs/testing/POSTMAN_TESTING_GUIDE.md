# 🚀 GymFlowPro API - Postman Testing Guide

## 📋 Quick Start

### 1. **Test Credentials** (Auto-Seeded)
```
Tenant: Iron Zone Gym (GYM-TEST-01)

Owner:   owner@gymflow.test / Test@1234
Manager: manager@gymflow.test / Test@1234
Trainer: trainer@gymflow.test / Test@1234

Sample Member: karim@gymflow.test (member, created with active membership)
```

### 2. **API Base URL**
```
https://localhost:5001
```

### 3. **Authentication**
All protected endpoints require Bearer token from `/api/auth/login`

---

## 🔐 Authentication Endpoints

### POST /api/auth/login
**Description**: Get JWT token for API access
**Auth**: None (public)
**Request Body**:
```json
{
  "email": "owner@gymflow.test",
  "password": "YOUR_PASSWORD"
}
```
**Response** (200 OK):
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "user": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "email": "owner@gymflow.test",
    "firstName": "Ahmed",
    "lastName": "Owner",
    "role": "Owner"
  }
}
```

---

## 📋 Membership Plans Endpoints (5)

### 1. GET /api/membership-plans
**Description**: List all active plans
**Auth**: AnyStaff (Owner/Manager/Trainer)
**Response** (200 OK):
```json
[
  {
    "id": "660e8400-e29b-41d4-a716-446655440001",
    "name": "Monthly Unlimited",
    "nameAr": "شهري غير محدود",
    "planType": "monthly_unlimited",
    "price": 500,
    "currency": "EGP",
    "durationDays": 30,
    "isActive": true,
    "createdAtUtc": "2026-05-03T12:00:00Z"
  },
  {
    "id": "660e8400-e29b-41d4-a716-446655440002",
    "name": "Session Pack 20",
    "nameAr": "باقة 20 جلسة",
    "planType": "session_pack",
    "price": 800,
    "currency": "EGP",
    "durationDays": 90,
    "sessionCount": 20,
    "isActive": true
  },
  {
    "id": "660e8400-e29b-41d4-a716-446655440003",
    "name": "Morning Pass",
    "nameAr": "تذكرة الصباح",
    "planType": "time_limited",
    "price": 300,
    "currency": "EGP",
    "durationDays": 30,
    "timeRestrictionStart": "06:00",
    "timeRestrictionEnd": "12:00",
    "isActive": true
  }
]
```

### 2. GET /api/membership-plans/{planId}
**Description**: Get plan details with membership counts
**Auth**: AnyStaff
**URL Parameter**: `planId` (UUID from list)
**Response** (200 OK):
```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "name": "Monthly Unlimited",
  "nameAr": "شهري غير محدود",
  "description": "Unlimited gym access for 30 days",
  "descriptionAr": "وصول غير محدود للصالة لمدة 30 يوم",
  "planType": "monthly_unlimited",
  "price": 500,
  "currency": "EGP",
  "durationDays": 30,
  "sessionCount": null,
  "timeRestrictionStart": null,
  "timeRestrictionEnd": null,
  "invitationQuota": 2,
  "isActive": true,
  "activeMemberships": 1,
  "totalMemberships": 1,
  "createdAtUtc": "2026-05-03T12:00:00Z"
}
```

### 3. POST /api/membership-plans
**Description**: Create new plan
**Auth**: OwnerOnly
**Request Body**:
```json
{
  "name": "PT Credits 10",
  "nameAr": "10 جلسات تدريب شخصي",
  "description": "Personal training sessions",
  "descriptionAr": "جلسات التدريب الشخصي",
  "planType": "pt_credits",
  "price": 1200,
  "durationDays": 90,
  "sessionCount": 10,
  "invitationQuota": 0
}
```
**Response** (201 Created):
```json
{
  "id": "770e8400-e29b-41d4-a716-446655440004",
  "name": "PT Credits 10",
  "nameAr": "10 جلسات تدريب شخصي",
  "planType": "pt_credits",
  "price": 1200,
  "currency": "EGP",
  "durationDays": 90,
  "sessionCount": 10,
  "isActive": true,
  "activeMemberships": 0,
  "totalMemberships": 0,
  "createdAtUtc": "2026-05-03T14:30:00Z"
}
```

### 4. PUT /api/membership-plans/{planId}
**Description**: Update plan
**Auth**: OwnerOnly
**URL Parameter**: `planId`
**Request Body**:
```json
{
  "name": "Monthly Unlimited Plus",
  "nameAr": "شهري غير محدود بلس",
  "description": "Unlimited access + guest passes",
  "descriptionAr": "وصول غير محدود + تذاكر الضيف",
  "planType": "monthly_unlimited",
  "price": 600,
  "durationDays": 30,
  "invitationQuota": 3
}
```
**Response** (200 OK):
```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "name": "Monthly Unlimited Plus",
  "nameAr": "شهري غير محدود بلس",
  "planType": "monthly_unlimited",
  "price": 600,
  "currency": "EGP",
  "durationDays": 30,
  "isActive": true,
  "activeMemberships": 1,
  "totalMemberships": 1,
  "updatedAtUtc": "2026-05-03T15:00:00Z"
}
```

### 5. DELETE /api/membership-plans/{planId}
**Description**: Soft delete plan
**Auth**: OwnerOnly
**URL Parameter**: `planId`
**Response** (200 OK):
```json
{
  "message": "Plan deleted successfully"
}
```
**Error** (409 Conflict - if active memberships):
```json
{
  "error": "Cannot delete plan with 1 active memberships / لا يمكن حذف خطة بها أعضاء نشطين",
  "message": "This plan has 1 active members"
}
```

---

## 👥 Membership Endpoints (4)

### 1. GET /api/memberships/{memberId}/current
**Description**: Get current or last membership
**Auth**: AnyStaff
**URL Parameter**: `memberId` (UUID of gym member)
**Response** (200 OK):
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440005",
  "memberId": "990e8400-e29b-41d4-a716-446655440006",
  "planId": "660e8400-e29b-41d4-a716-446655440001",
  "planName": "Monthly Unlimited",
  "status": "active",
  "startDate": "2026-05-03",
  "endDate": "2026-06-02",
  "remainingDays": 30,
  "paymentMethod": "cash",
  "paymentDate": "2026-05-03T12:00:00Z",
  "createdAtUtc": "2026-05-03T12:00:00Z"
}
```

### 2. GET /api/memberships/{memberId}/history
**Description**: Get membership history (paginated)
**Auth**: AnyStaff
**URL Parameters**:
- `memberId`: UUID of gym member
- `page` (optional): Default 1
- `pageSize` (optional): Default 10
**Request**: `GET /api/memberships/{memberId}/history?page=1&pageSize=5`
**Response** (200 OK):
```json
{
  "items": [
    {
      "id": "880e8400-e29b-41d4-a716-446655440005",
      "planName": "Monthly Unlimited",
      "planType": "monthly_unlimited",
      "status": "active",
      "startDate": "2026-05-03",
      "endDate": "2026-06-02",
      "paymentMethod": "cash",
      "paymentDate": "2026-05-03T12:00:00Z",
      "createdAtUtc": "2026-05-03T12:00:00Z"
    },
    {
      "id": "880e8400-e29b-41d4-a716-446655440007",
      "planName": "Session Pack 20",
      "planType": "session_pack",
      "status": "expired",
      "startDate": "2026-04-01",
      "endDate": "2026-04-30",
      "paymentMethod": "cash",
      "paymentDate": "2026-04-01T10:00:00Z",
      "createdAtUtc": "2026-04-01T10:00:00Z"
    }
  ],
  "totalCount": 2,
  "pageNumber": 1,
  "pageSize": 5,
  "totalPages": 1
}
```

### 3. POST /api/memberships/{memberId}/assign
**Description**: Assign membership to member (create new)
**Auth**: ManagerOrAbove (Owner/Manager)
**URL Parameter**: `memberId`
**Request Body**:
```json
{
  "planId": "660e8400-e29b-41d4-a716-446655440002",
  "paymentMethod": "cash"
}
```
**Valid paymentMethods**: `"cash"`, `"paymob"`, `"fawry"`
**Response** (201 Created):
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440008",
  "memberId": "990e8400-e29b-41d4-a716-446655440006",
  "planId": "660e8400-e29b-41d4-a716-446655440002",
  "planName": "Session Pack 20",
  "status": "active",
  "startDate": "2026-05-03",
  "endDate": "2026-07-01",
  "remainingDays": 59,
  "paymentMethod": "cash",
  "paymentDate": "2026-05-03T13:30:00Z",
  "createdAtUtc": "2026-05-03T13:30:00Z"
}
```
**Error** (409 Conflict - if active membership exists):
```json
{
  "error": "Member already has an active membership / العضو لديه عضوية نشطة بالفعل"
}
```

### 4. POST /api/memberships/{memberId}/renew
**Description**: Renew membership (continuous timeline)
**Auth**: ManagerOrAbove
**URL Parameter**: `memberId`
**Request Body**:
```json
{
  "planId": "660e8400-e29b-41d4-a716-446655440001",
  "paymentMethod": "cash"
}
```
**Response** (201 Created):
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440009",
  "memberId": "990e8400-e29b-41d4-a716-446655440006",
  "planId": "660e8400-e29b-41d4-a716-446655440001",
  "planName": "Monthly Unlimited",
  "status": "active",
  "startDate": "2026-07-02",
  "endDate": "2026-08-01",
  "remainingDays": 31,
  "paymentMethod": "cash",
  "paymentDate": "2026-07-02T09:00:00Z",
  "createdAtUtc": "2026-07-02T09:00:00Z"
}
```
**Note**: `startDate` = previous membership `endDate` (continuous timeline, no gaps)

---

## 👨‍💼 Admin (Staff Management) Endpoints (6)

### 1. GET /api/admin/staff
**Description**: List all staff (excludes owner)
**Auth**: OwnerOnly
**Response** (200 OK):
```json
[
  {
    "id": "aa0e8400-e29b-41d4-a716-446655440010",
    "email": "manager@gymflow.test",
    "firstName": "Sara",
    "lastName": "Manager",
    "role": "Manager",
    "isActive": true,
    "createdAtUtc": "2026-05-03T12:00:00Z"
  },
  {
    "id": "bb0e8400-e29b-41d4-a716-446655440011",
    "email": "trainer@gymflow.test",
    "firstName": "Omar",
    "lastName": "Trainer",
    "role": "Trainer",
    "isActive": true,
    "createdAtUtc": "2026-05-03T12:00:00Z"
  }
]
```

### 2. GET /api/admin/staff/{staffId}
**Description**: Get staff details
**Auth**: OwnerOnly
**URL Parameter**: `staffId`
**Response** (200 OK):
```json
{
  "id": "aa0e8400-e29b-41d4-a716-446655440010",
  "email": "manager@gymflow.test",
  "firstName": "Sara",
  "lastName": "Manager",
  "role": "Manager",
  "isActive": true,
  "createdAtUtc": "2026-05-03T12:00:00Z",
  "updatedAtUtc": null
}
```

### 3. POST /api/admin/staff
**Description**: Create new staff (Manager or Trainer only)
**Auth**: OwnerOnly
**Request Body**:
```json
{
  "email": "newtrainer@gymflow.test",
  "firstName": "Layla",
  "lastName": "Trainer",
  "role": "Trainer",
  "password": "YOUR_PASSWORD"
}
```
**Valid roles**: `"Manager"`, `"Trainer"` (NOT "Owner")
**Response** (201 Created):
```json
{
  "id": "cc0e8400-e29b-41d4-a716-446655440012",
  "email": "newtrainer@gymflow.test",
  "firstName": "Layla",
  "lastName": "Trainer",
  "role": "Trainer",
  "isActive": true,
  "createdAtUtc": "2026-05-03T15:00:00Z"
}
```
**Error** (400 - if email already exists in tenant):
```json
{
  "error": "Email already exists in your gym / البريد الإلكتروني موجود بالفعل"
}
```

### 4. PUT /api/admin/staff/{staffId}
**Description**: Update staff
**Auth**: OwnerOnly
**URL Parameter**: `staffId`
**Request Body**:
```json
{
  "firstName": "Sara",
  "lastName": "Manager Pro"
}
```
**Response** (200 OK):
```json
{
  "id": "aa0e8400-e29b-41d4-a716-446655440010",
  "email": "manager@gymflow.test",
  "firstName": "Sara",
  "lastName": "Manager Pro",
  "role": "Manager",
  "isActive": true,
  "updatedAtUtc": "2026-05-03T15:30:00Z"
}
```

### 5. DELETE /api/admin/staff/{staffId}
**Description**: Soft delete staff
**Auth**: OwnerOnly
**URL Parameter**: `staffId`
**Response** (200 OK):
```json
{
  "message": "Staff deleted successfully"
}
```

### 6. POST /api/admin/staff/{staffId}/reset-password
**Description**: Reset staff password
**Auth**: OwnerOnly
**URL Parameter**: `staffId`
**Request Body**:
```json
{
  "newPassword": "ResetPass@456"
}
```
**Response** (200 OK):
```json
{
  "message": "Password reset successfully / تم إعادة تعيين كلمة المرور بنجاح"
}
```

---

## ⚙️ Tenant Settings Endpoints (5)

### 1. GET /api/settings
**Description**: Get tenant settings
**Auth**: OwnerOnly
**Response** (200 OK):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "gymName": "Iron Zone Gym",
  "gymNameAr": "صالة حديد زون",
  "gymCode": "GYM-TEST-01",
  "city": "Cairo",
  "address": "123 Fitness Street",
  "phone": "+201000000000",
  "email": "info@ironzone.test",
  "logoUrl": null,
  "currency": "EGP",
  "timeZone": "Africa/Cairo",
  "maxMembers": 1000,
  "isActive": true,
  "invitationQuotas": {
    "monthlyUnlimited": 2,
    "sessionPack": 1,
    "timeLimited": 0,
    "ptCredits": 0,
    "family": 3
  },
  "updatedAtUtc": "2026-05-03T12:00:00Z"
}
```

### 2. PUT /api/settings
**Description**: Update tenant settings
**Auth**: OwnerOnly
**Request Body**:
```json
{
  "gymName": "Iron Zone Gym Elite",
  "gymNameAr": "صالة حديد زون النخبة",
  "city": "Cairo",
  "address": "456 Premium Street",
  "phone": "+201111111111",
  "email": "elite@ironzone.test"
}
```
**Response** (200 OK):
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "gymName": "Iron Zone Gym Elite",
  "gymNameAr": "صالة حديد زون النخبة",
  "gymCode": "GYM-TEST-01",
  "city": "Cairo",
  "address": "456 Premium Street",
  "phone": "+201111111111",
  "email": "elite@ironzone.test",
  "logoUrl": null,
  "currency": "EGP",
  "timeZone": "Africa/Cairo",
  "maxMembers": 1000,
  "isActive": true,
  "updatedAtUtc": "2026-05-03T15:45:00Z"
}
```

### 3. GET /api/settings/gym-code
**Description**: Get gym code (for member login/QR scanning)
**Auth**: AnyStaff
**Response** (200 OK):
```json
{
  "gymCode": "GYM-TEST-01"
}
```

### 4. GET /api/settings/qr-poster
**Description**: Get QR poster URL (for check-in)
**Auth**: AnyStaff
**Response** (200 OK):
```json
{
  "qrPosterUrl": "https://localhost:5001/uploads/qr-posters/GYM-TEST-01.png"
}
```

### 5. PUT /api/settings/invitation-quotas
**Description**: Update invitation quotas per plan type
**Auth**: OwnerOnly
**Request Body**:
```json
{
  "monthlyUnlimited": 3,
  "sessionPack": 2,
  "timeLimited": 1,
  "ptCredits": 0,
  "family": 5
}
```
**Response** (200 OK):
```json
{
  "monthlyUnlimited": 3,
  "sessionPack": 2,
  "timeLimited": 1,
  "ptCredits": 0,
  "family": 5,
  "updatedAtUtc": "2026-05-03T16:00:00Z"
}
```

---

## ❌ Common Error Responses

### 400 Bad Request (Validation Failed)
```json
{
  "errors": {
    "price": ["Price must be greater than 0"],
    "name": ["Name is required"],
    "sessionCount": ["Session count must be 10, 20, or 50"]
  }
}
```

### 401 Unauthorized (Missing Token)
```json
{
  "error": "Unauthorized / غير مصرح"
}
```

### 403 Forbidden (Insufficient Role)
```json
{
  "error": "Forbidden - Insufficient permissions / ممنوع - صلاحيات غير كافية"
}
```

### 404 Not Found
```json
{
  "error": "Resource not found / المورد غير موجود"
}
```

### 409 Conflict
```json
{
  "error": "Cannot perform action / لا يمكن تنفيذ الإجراء",
  "message": "Detailed reason"
}
```

### 500 Internal Server Error
```json
{
  "error": "An unexpected error occurred / حدث خطأ غير متوقع",
  "message": "Error details (development only)"
}
```

---

## 🧪 Test Scenarios

### Scenario 1: Manager Cannot Create Plan (RBAC)
1. **Login as Manager**
   ```
   POST /api/auth/login
   manager@gymflow.test / Test@1234
   ```
2. **Try to create plan**
   ```
   POST /api/membership-plans
   [Request xxxx xxxx xxxx xxxx]
   ```
3. **Expected**: 403 Forbidden ✅

### Scenario 2: Continuous Membership Renewal
1. **Get current membership**
   ```
   GET /api/memberships/{memberId}/current
   ```
   Note: `endDate` is 2026-06-02
2. **Renew membership**
   ```
   POST /api/memberships/{memberId}/renew
   [Plan data]
   ```
3. **Verify new membership**
   - `startDate` = previous `endDate` (2026-06-02)
   - No gap between memberships ✅

### Scenario 3: One Active Membership per Member
1. **Assign membership to member** → Status: Active
2. **Try to assign another membership** 
   ```
   POST /api/memberships/{memberId}/assign
   [Different plan]
   ```
3. **Expected**: 409 Conflict with message ✅

### Scenario 4: Cannot Delete Plan with Active Members
1. **Get plan with active members**
   ```
   GET /api/membership-plans/{planId}
   activeMemberships > 0
   ```
2. **Try to delete plan**
   ```
   DELETE /api/membership-plans/{planId}
   ```
3. **Expected**: 409 Conflict ✅

---

## 📦 Postman Environment Variables

Create a Postman Environment named `GymFlowPro-Local` with:

```
BASE_URL:       https://localhost:5001
TOKEN:          [Leave empty - set after login]
OWNER_EMAIL:    owner@gymflow.test
OWNER_PASSWORD: Test@1234
MANAGER_EMAIL:  manager@gymflow.test
MANAGER_PASS:   Test@1234
TRAINER_EMAIL:  trainer@gymflow.test
TRAINER_PASS:   Test@1234
```

### Set Token After Login
In Postman, click login request → Tests tab → Add:
```javascript
var jsonData = pm.response.json();
pm.environment.set("TOKEN", jsonData.token);
```

Then use `{{TOKEN}}` in Authorization header for all protected requests.

---

## 🔄 Request/Response Cycle Example

### 1. Login
```http
POST https://localhost:5001/api/auth/login
Content-Type: application/json

{
  "email": "owner@gymflow.test",
  "password": "YOUR_PASSWORD"
}
```

**Response**:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "user": {"id": "...", "email": "owner@gymflow.test"}
}
```

### 2. Create Membership Plan
```http
POST https://localhost:5001/api/membership-plans
Authorization: Bearer YOUR_ACCESS_TOKEN
Content-Type: application/json

{
  "name": "Summer Blast",
  "nameAr": "انفجار الصيف",
  "planType": "monthly_unlimited",
  "price": 550,
  "durationDays": 30
}
```

**Response** (201):
```json
{
  "id": "770e8400-e29b-41d4-a716-446655440004",
  "name": "Summer Blast",
  "planType": "monthly_unlimited",
  "price": 550,
  "createdAtUtc": "2026-05-03T16:30:00Z"
}
```

### 3. Assign Membership
```http
POST https://localhost:5001/api/memberships/990e8400-e29b-41d4-a716-446655440006/assign
Authorization: Bearer [Token]
Content-Type: application/json

{
  "planId": "770e8400-e29b-41d4-a716-446655440004",
  "paymentMethod": "cash"
}
```

**Response** (201):
```json
{
  "id": "880e8400-e29b-41d4-a716-446655440013",
  "planName": "Summer Blast",
  "status": "active",
  "startDate": "2026-05-03",
  "endDate": "2026-06-02",
  "paymentMethod": "cash",
  "paymentDate": "2026-05-03T16:31:00Z"
}
```

---

## ✅ Verification Checklist

- [ ] Can login with Owner credentials
- [ ] Can login with Manager credentials
- [ ] Can login with Trainer credentials
- [ ] Can list membership plans (AnyStaff)
- [ ] Can get plan details (AnyStaff)
- [ ] Can create plan (OwnerOnly) → 201
- [ ] Cannot create plan as Manager → 403
- [ ] Can update plan (OwnerOnly)
- [ ] Can delete empty plan (OwnerOnly)
- [ ] Cannot delete plan with active members → 409
- [ ] Can assign membership to member
- [ ] Cannot assign second active membership → 409
- [ ] Can renew membership with continuous timeline
- [ ] Can list staff (OwnerOnly)
- [ ] Can create staff (OwnerOnly)
- [ ] Can update staff (OwnerOnly)
- [ ] Can reset staff password (OwnerOnly)
- [ ] Can get tenant settings (OwnerOnly)
- [ ] Can update tenant settings (OwnerOnly)
- [ ] Can update invitation quotas (OwnerOnly)
- [ ] All bilingual messages present (EN + AR)

---

**Ready for testing!** 🚀

Import these requests into Postman and start testing.
