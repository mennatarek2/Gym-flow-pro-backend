# GymFlowPro API — Frontend & Flutter Developer Documentation

> ⚠️ **STATUS (REM-F11):** This document describes a Flutter member app that is **SPECIFICATION ONLY — no Flutter/Dart project exists in this repository**. The *backend endpoints* described here are implemented and verified; the mobile client is not. Treat all Flutter UI/integration guidance as design material, not existing code. See `PROJECT-UNDERSTANDING.html` / `REMEDIATION-REPORT.md`.

> **Base URL:** `https://your-domain.com` (or `http://localhost:5000` for local dev)  
> **API Prefix:** `/api`  
> **Auth:** JWT Bearer token in `Authorization` header  
> **Content-Type:** `application/json`  
> **Swagger UI:** Available at root URL in development  

---

## Table of Contents

1. [Authentication Overview](#authentication-overview)
2. [Authorization Policies (Roles)](#authorization-policies-roles)
3. [Common Error Responses](#common-error-responses)
4. [API Endpoints by Domain](#api-endpoints-by-domain)
   - [Auth](#1-auth-authcontroller)
   - [Members](#2-members-memberscontroller)
   - [Memberships](#3-memberships-membershipscontroller)
   - [Membership Plans](#4-membership-plans-membershipplanscontroller)
   - [Attendance / Check-in](#5-attendance--check-in-attendancecontroller)
   - [Admin / Staff Management](#6-admin--staff-management-admincontroller)
   - [Tenant Settings](#7-tenant-settings-tenantsettingscontroller)
   - [Notifications](#8-notifications-notificationscontroller)
   - [Invitations](#9-invitations-invitationcontroller)
   - [Analytics](#10-analytics-analyticscontroller)
   - [Reports](#11-reports-reportscontroller)
   - [Payments (Webhooks)](#12-payments-webhooks-paymentscontroller)
   - [Health](#13-health-healthcontroller)
5. [DTO Reference (Request & Response Bodies)](#dto-reference-request--response-bodies)
6. [Enums](#enums)
7. [Real-time (SignalR)](#real-time-signalr)
8. [Rate Limiting](#rate-limiting)
9. [Flutter Integration Tips](#flutter-integration-tips)

---

## Authentication Overview

The API uses **JWT Bearer YOUR_ACCESS_TOKEN There are two auth flows:

### Staff Login (Email + Password)
1. Call `POST /api/auth/login` → receive `accessToken` + `refreshToken`
2. Include `Authorization: Bearer YOUR_ACCESS_TOKEN` on all subsequent requests
3. When token expires (15 min), call `POST /api/auth/refresh` with the refresh token

### Member Login (Phone OTP)
1. Call `POST /api/auth/member-otp` → OTP sent to phone (valid 5 min)
2. Call `POST /api/auth/member-verify` → receive `accessToken` + `refreshToken`
3. Same token usage as staff

### Token Details
| Property | Value |
|---|---|
| Access Token Lifetime | 15 minutes |
| Refresh Token Lifetime | 30 days |
| Token Type | JWT (HS256) |
| Header | `Authorization: Bearer {token}` |
| Expired Token Header | Response includes `Token-Expired: true` header |

### JWT Claims
| Claim | Description |
|---|---|
| `sub` | User ID (Guid) |
| `email` | User email |
| `role` | `Owner`, `Manager`, `Trainer`, or `Member` |
| `tenant_id` | Tenant (gym) ID |
| `member_id` | Member ID (only for Member role) |
| `gym_code` | Gym code string |

---

## Authorization Policies (Roles)

| Policy | Allowed Roles | Description |
|---|---|---|
| `OwnerOnly` | Owner | Gym owner — highest privilege |
| `ManagerOrAbove` | Owner, Manager | Management operations |
| `AnyStaff` | Owner, Manager, Trainer | All staff can access |
| `AuthenticatedMember` | Member | Member-only endpoints |
| `AnyAuthenticated` | Any logged-in user | Any authenticated user |

---

## Common Error Responses

All errors follow this pattern:

```json
{
  "error": "Human-readable error message",
  "message": "Optional additional detail (sometimes bilingual EN/AR)"
}
```

### Standard ErrorResponse DTO
```json
{
  "message": "Error description",
  "details": "Optional stack trace or detail",
  "statusCode": 400
}
```

### HTTP Status Codes Used
| Code | Meaning |
|---|---|
| 200 | Success |
| 201 | Created successfully |
| 400 | Bad Request — validation error |
| 401 | Unauthorized — missing/invalid token |
| 403 | Forbidden — insufficient role |
| 404 | Not Found |
| 409 | Conflict — e.g., active membership already exists |
| 429 | Too Many Requests — rate limited |
| 500 | Internal Server Error |

---

## API Endpoints by Domain

---

### 1. Auth (`AuthController`)

**Base Route:** `/api/auth`  
**Auth Required:** No (all endpoints are `[AllowAnonymous]`)

#### 1.1 Staff Login
```
POST /api/auth/login
```

Authenticates a staff user with email + password.

**Request Body:** [`LoginRequest`](#loginrequest)
```json
{
  "email": "manager@gym.com",
  "password": "YOUR_PASSWORD",
  "gymCode": "GYM-CAIRO-01"
}
```

**Response 200:** [`LoginResponse`](#loginresponse)
```json
{
  "accessToken": "eyJhbGciOi...",
  "refreshToken": "dGhpcyBpcyBh...",
  "expiresAtUtc": "2026-05-11T00:00:00Z",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "manager@gym.com",
    "fullName": "Ahmed Manager",
    "role": "Manager",
    "tenantId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
    "gymCode": "GYM-CAIRO-01"
  }
}
```

**Error Responses:**
- `400` — Invalid request body
- `401` — Invalid credentials or wrong gym code

---

#### 1.2 Refresh Token
```
POST /api/auth/refresh
```

Exchanges a valid refresh token for a new access/refresh token pair. Old refresh token is revoked (sliding rotation).

**Request Body:** [`RefreshTokenRequest`](#refreshtokenrequest)
```json
{
  "refreshToken": "dGhpcyBpcyBh..."
}
```

**Response 200:** [`LoginResponse`](#loginresponse) (same structure as login)

**Error Responses:**
- `400` — Invalid request body
- `401` — Invalid or expired refresh token

---

#### 1.3 Send Member OTP
```
POST /api/auth/member-otp
```

Sends a 6-digit OTP to the member's phone number. OTP is valid for 5 minutes.

**Request Body:** [`MemberOtpRequest`](#memberotprequest)
```json
{
  "phoneNumber": "+201234567890",
  "gymCode": "GYM-CAIRO-01"
}
```

**Response 200:**
```json
{
  "message": "OTP sent successfully / تم إرسال رمز التحقق بنجاح"
}
```

**Error Responses:**
- `400` — Phone number not found or invalid gym code

---

#### 1.4 Verify Member OTP
```
POST /api/auth/member-verify
```

Verifies the OTP and issues a member JWT. Auto-provisions an Identity user if one doesn't exist.

**Request Body:** [`MemberOtpVerifyRequest`](#memberotpverifyrequest)
```json
{
  "phoneNumber": "+201234567890",
  "gymCode": "GYM-CAIRO-01",
  "otp": "123456"
}
```

**Response 200:** [`LoginResponse`](#loginresponse) (same structure as login, with `role: "Member"`)

**Error Responses:**
- `400` — Invalid request body
- `401` — Invalid or expired OTP

---

### 2. Members (`MembersController`)

**Base Route:** `/api/members`  
**Auth Required:** `[Authorize]` (all endpoints)

#### 2.1 List/Search Members
```
GET /api/members?search=ahmed&status=active&page=1&pageSize=20
```

**Policy:** `ManagerOrAbove`

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `search` | string? | null | Search by name, phone, or member number |
| `status` | string? | null | Filter by status: `active`, `expired`, `frozen`, `cancelled` |
| `page` | int | 1 | Page number |
| `pageSize` | int | 20 | Items per page |

**Response 200:** `List<`[`MemberListItemDto`](#memberlistitemdto)`>`
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "memberNumber": "MEM-001",
    "fullName": "Ahmed Ali",
    "fullNameAr": "أحمد علي",
    "phone": "+201234567890",
    "isActive": true,
    "activePlan": "Monthly Unlimited",
    "activePlanAr": "شهري غير محدود",
    "expiryDate": "2026-06-01",
    "membershipStatus": "active"
  }
]
```

---

#### 2.2 Get Member Details
```
GET /api/members/{id}
```

**Policy:** `AnyStaff`

Returns full member details with current membership and recent attendance (last 5).

**Response 200:** [`MemberDetailDto`](#memberdetaildto)
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "memberNumber": "MEM-001",
  "fullName": "Ahmed Ali",
  "fullNameAr": "أحمد علي",
  "phone": "+201234567890",
  "email": "ahmed@email.com",
  "dateOfBirth": "1995-03-15",
  "profilePhotoUrl": "/uploads/photos/ahmed.jpg",
  "notes": null,
  "isActive": true,
  "invitationQuotaRemaining": 3,
  "createdAtUtc": "2026-01-15T10:30:00Z",
  "currentMembership": {
    "id": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
    "planName": "Monthly Unlimited",
    "planNameAr": "شهري غير محدود",
    "planType": "monthly_unlimited",
    "status": "active",
    "startDate": "2026-05-01",
    "endDate": "2026-06-01",
    "sessionsRemaining": null,
    "frozenFromDate": null,
    "frozenUntilDate": null,
    "amountPaid": 500.00,
    "paymentMethod": "cash"
  },
  "recentAttendance": [
    {
      "id": "5fa85f64-5717-4562-b3fc-2c963f66afa8",
      "checkInAtUtc": "2026-05-10T08:30:00Z",
      "checkOutAtUtc": null,
      "entryMethod": "qr"
    }
  ]
}
```

**Error:** `404` — Member not found

---

#### 2.3 Create Member
```
POST /api/members
```

**Policy:** `ManagerOrAbove`

Creates a new member with auto-generated `MemberNumber`.

**Request Body:** [`CreateMemberRequest`](#creatememberrequest)
```json
{
  "fullName": "Ahmed Ali",
  "fullNameAr": "أحمد علي",
  "phone": "+201234567890",
  "dateOfBirth": "1995-03-15",
  "nationalId": "29901011234567",
  "emergencyContact": "+201098765432",
  "email": "ahmed@email.com",
  "notes": "Referred by manager"
}
```

**Response 201:** [`MemberDetailDto`](#memberdetaildto) (same as GET member details)

**Error:** `400` — Validation error (e.g., duplicate phone)

---

#### 2.4 Update Member
```
PUT /api/members/{id}
```

**Policy:** `ManagerOrAbove`

Partial update — only provided fields are updated.

**Request Body:** [`UpdateMemberRequest`](#updatememberrequest)
```json
{
  "fullName": "Ahmed Ali Updated",
  "phone": "+201234567891",
  "notes": "Updated notes"
}
```

**Response 200:** [`MemberDetailDto`](#memberdetaildto)

**Errors:** `400` — Validation error | `404` — Member not found

---

#### 2.5 Deactivate Member (Soft Delete)
```
DELETE /api/members/{id}
```

**Policy:** `OwnerOnly`

Sets `IsActive = false`. Irreversible without direct DB access.

**Response 200:**
```json
{
  "message": "Member deactivated successfully / تم إلغاء تفعيل العضو بنجاح"
}
```

**Error:** `404` — Member not found

---

#### 2.6 Get Member Attendance History
```
GET /api/members/{id}/attendance?page=1&pageSize=20
```

**Policy:** `AnyStaff`

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `page` | int | 1 | Page number |
| `pageSize` | int | 20 | Items per page |

**Response 200:** Paginated list of attendance records

**Error:** `404` — Member not found

---

#### 2.7 Get Member's Current Membership
```
GET /api/members/{id}/membership
```

**Policy:** `AnyStaff`

**Response 200:** [`MembershipSummaryDto`](#membershipsummarydto) (nested in MemberDetailDto)

**Error:** `404` — No membership found

---

#### 2.8 Freeze Membership
```
POST /api/members/{id}/freeze
```

**Policy:** `ManagerOrAbove`

Freezes a member's active membership. End date is extended by the freeze duration.

**Request Body:** [`FreezeMembershipRequest`](#freezemembershiprequest)
```json
{
  "frozenUntil": "2026-05-20T00:00:00",
  "reason": "Travel / سفر"
}
```

**Response 200:**
```json
{
  "message": "Membership frozen successfully / تم تجميد الاشتراك بنجاح"
}
```

**Error:** `400` — No active membership or already frozen

---

#### 2.9 Unfreeze Membership
```
POST /api/members/{id}/unfreeze
```

**Policy:** `ManagerOrAbove`

Unfreezes a member's frozen membership back to active.

**Response 200:**
```json
{
  "message": "Membership unfrozen successfully / تم فك تجميد الاشتراك بنجاح"
}
```

**Error:** `400` — Membership is not frozen

---

### 3. Memberships (`MembershipsController`)

**Base Route:** `/api/memberships`  
**Auth Required:** `[Authorize]` (all endpoints)

#### 3.1 Get Current Membership for Member
```
GET /api/memberships/{memberId}/current
```

**Policy:** `AnyStaff`

Returns the current active membership. If no active membership, returns the last expired one.

**Response 200:** [`MembershipDto`](#membershipdto)
```json
{
  "id": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
  "planName": "Monthly Unlimited",
  "planNameAr": "شهري غير محدود",
  "planType": "monthly_unlimited",
  "startDate": "2026-05-01",
  "endDate": "2026-06-01",
  "status": "active",
  "sessionsRemaining": null,
  "amountPaid": 500.00,
  "paymentMethod": "cash",
  "paymentDate": "2026-05-01T10:00:00Z",
  "autoRenew": false,
  "frozenFromDate": null,
  "frozenUntilDate": null,
  "daysRemaining": 22
}
```

**Error:** `404` — No membership found

---

#### 3.2 Get Membership History
```
GET /api/memberships/{memberId}/history?page=1&pageSize=20
```

**Policy:** `AnyStaff`

Returns paginated membership history, newest first.

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `page` | int | 1 | Page number |
| `pageSize` | int | 20 | Items per page |

**Response 200:** `List<`[`MembershipHistoryItemDto`](#membershiphistoryitemdto)`>`
```json
[
  {
    "id": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
    "planName": "Monthly Unlimited",
    "planNameAr": "شهري غير محدود",
    "planType": "monthly_unlimited",
    "startDate": "2026-05-01",
    "endDate": "2026-06-01",
    "status": "active",
    "amountPaid": 500.00,
    "paymentMethod": "cash",
    "paymentDate": "2026-05-01T10:00:00Z",
    "createdAtUtc": "2026-05-01T10:00:00Z"
  }
]
```

**Error:** `404` — Member not found

---

#### 3.3 Assign Membership to Member
```
POST /api/memberships/{memberId}/assign
```

**Policy:** `ManagerOrAbove`

Assigns a new membership. Fails if member already has an active membership.

**Payment Flow:**
- `cash` → Membership created with `status = "active"` immediately
- `paymob` / `fawry` → Membership created with `status = "pending"`, activated via webhook on successful payment

**Request Body:** [`AssignMembershipRequest`](#assignmembershiprequest)
```json
{
  "planId": "7fa85f64-5717-4562-b3fc-2c963f66afa0",
  "paymentMethod": "cash"
}
```

**Response 201:** [`MembershipDto`](#membershipdto)

**Errors:**
- `400` — Validation error (invalid plan, etc.)
- `409` — Member already has an active membership

---

#### 3.4 Renew Membership
```
POST /api/memberships/{memberId}/renew
```

**Policy:** `ManagerOrAbove`

Renews a current/expired membership. `StartDate` = `EndDate` of previous membership (continuous). If `PlanId` is null, renews with the same plan.

**Request Body:** [`RenewMembershipRequest`](#renewmembershiprequest)
```json
{
  "planId": null,
  "paymentMethod": "cash",
  "amountPaid": 500.00
}
```

**Response 200:** [`MembershipDto`](#membershipdto)

**Errors:**
- `400` — Validation error
- `404` — No previous membership to renew

---

### 4. Membership Plans (`MembershipPlansController`)

**Base Route:** `/api/membership-plans`  
**Auth Required:** `[Authorize]` (all endpoints)

**Supported Plan Types:**
| Type | Description | Special Fields |
|---|---|---|
| `monthly_unlimited` | Unlimited access for duration | — |
| `session_pack` | Fixed number of sessions | `SessionCount` (10, 20, or 50) |
| `time_limited` | Access restricted to time window | `TimeRestrictionStart`, `TimeRestrictionEnd` |
| `pt_credits` | Personal training credits | — |
| `family` | Family plan with guest invitations | `InvitationQuota` |

#### 4.1 List All Active Plans
```
GET /api/membership-plans
```

**Policy:** `AnyStaff`

**Response 200:** `List<`[`PlanListItemDto`](#planlistitemdto)`>`
```json
[
  {
    "id": "7fa85f64-5717-4562-b3fc-2c963f66afa0",
    "name": "Monthly Unlimited",
    "nameAr": "شهري غير محدود",
    "planType": "monthly_unlimited",
    "price": 500.00,
    "currency": "EGP",
    "durationDays": 30,
    "isActive": true,
    "createdAtUtc": "2026-01-01T00:00:00Z"
  }
]
```

---

#### 4.2 Get Plan Details
```
GET /api/membership-plans/{id}
```

**Policy:** `AnyStaff`

**Response 200:** [`PlanDetailDto`](#plandetaildto)
```json
{
  "id": "7fa85f64-5717-4562-b3fc-2c963f66afa0",
  "name": "Monthly Unlimited",
  "nameAr": "شهري غير محدود",
  "description": "Full gym access for 30 days",
  "descriptionAr": "وصول كامل للصالة لمدة 30 يوم",
  "planType": "monthly_unlimited",
  "price": 500.00,
  "currency": "EGP",
  "durationDays": 30,
  "sessionCount": null,
  "timeRestrictionStart": null,
  "timeRestrictionEnd": null,
  "invitationQuota": 0,
  "isActive": true,
  "activeMemberships": 45,
  "totalMemberships": 120,
  "createdAtUtc": "2026-01-01T00:00:00Z",
  "updatedAtUtc": null
}
```

**Error:** `404` — Plan not found

---

#### 4.3 Create Plan
```
POST /api/membership-plans
```

**Policy:** `OwnerOnly`

**Request Body:** [`CreatePlanRequest`](#createplanrequest)
```json
{
  "name": "20-Session Pack",
  "nameAr": "باقة 20 جلسة",
  "description": "20 sessions to use within 90 days",
  "descriptionAr": "20 جلسة للاستخدام خلال 90 يوم",
  "planType": "session_pack",
  "price": 800.00,
  "durationDays": 90,
  "sessionCount": 20,
  "timeRestrictionStart": null,
  "timeRestrictionEnd": null,
  "invitationQuota": 0
}
```

**Response 201:** [`PlanDetailDto`](#plandetaildto)

**Error:** `400` — Validation error (e.g., `session_pack` requires `SessionCount` of 10, 20, or 50)

---

#### 4.4 Update Plan
```
PUT /api/membership-plans/{id}
```

**Policy:** `OwnerOnly`

**Request Body:** [`UpdatePlanRequest`](#updateplanrequest) (same structure as CreatePlanRequest)

**Response 200:** [`PlanDetailDto`](#plandetaildto)

**Errors:** `400` — Validation error | `404` — Plan not found

---

#### 4.5 Delete Plan (Soft Delete)
```
DELETE /api/membership-plans/{id}
```

**Policy:** `OwnerOnly`

Soft deletes a plan. Returns `409` if there are active memberships on this plan.

**Response 200:**
```json
{
  "message": "Plan deleted successfully / تم حذف الخطة بنجاح"
}
```

**Errors:**
- `404` — Plan not found
- `409` — Plan has active memberships, cannot delete

---

### 5. Attendance / Check-in (`AttendanceController`)

**Base Route:** `/api/attendance`  
**Auth Required:** Varies per endpoint

#### 5.1 QR Code Check-in
```
POST /api/attendance/qr-checkin
```

**Policy:** `AuthenticatedMember`  
**Rate Limit:** 30 requests/minute per IP

Member scans the gym's static QR code. Full validation gauntlet runs (membership, freeze, time, sessions).

**Request Body:** [`QrCheckinRequest`](#qrcheckinrequest)
```json
{
  "gymCode": "GYM-CAIRO-01"
}
```

**Response 200:** [`QrCheckinResponse`](#qrcheckinresponse)
```json
{
  "attendanceId": "5fa85f64-5717-4562-b3fc-2c963f66afa8",
  "memberName": "Ahmed Ali",
  "memberNameAr": "أحمد علي",
  "checkInAtUtc": "2026-05-10T08:30:00Z",
  "planName": "Monthly Unlimited",
  "planNameAr": "شهري غير محدود",
  "sessionsRemaining": null,
  "message": "Check-in successful! / تم تسجيل الدخول بنجاح!",
  "messageAr": "تم تسجيل الدخول بنجاح!"
}
```

**Errors:**
- `400` — No active membership, frozen, time-restricted, no sessions remaining
- `401` — Not authenticated
- `429` — Rate limited

---

#### 5.2 Manual Check-in (by Staff)
```
POST /api/attendance/manual-checkin
```

**Policy:** `ManagerOrAbove`

Staff manually checks in a member. Creates attendance with `entry_method = "manual"`.

**Request Body:** [`ManualCheckinRequest`](#manualcheckinrequest)
```json
{
  "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "reason": 1,
  "notes": "Phone battery died"
}
```

**`reason` values:** See [ManualCheckinReason enum](#manualcheckinreason)

**Response 200:** [`ManualCheckinResponse`](#manualcheckinresponse)
```json
{
  "attendanceId": "5fa85f64-5717-4562-b3fc-2c963f66afa8",
  "memberName": "Ahmed Ali",
  "memberNameAr": "أحمد علي",
  "checkInAtUtc": "2026-05-10T08:30:00Z",
  "planName": "Monthly Unlimited",
  "planNameAr": "شهري غير محدود",
  "sessionsRemaining": null,
  "staffName": "Sara Trainer",
  "message": "Manual check-in successful / تم تسجيل الدخول يدوياً بنجاح",
  "messageAr": "تم تسجيل الدخول يدوياً بنجاح"
}
```

**Errors:**
- `400` — Member not eligible for check-in
- `401` — Not authenticated
- `403` — Insufficient role

---

#### 5.3 Search Members (for Manual Check-in UI)
```
GET /api/attendance/search-members?query=ahmed&includeInactive=false
```

**Policy:** `AnyStaff`

Returns members with a `isSelectable` flag. Expired/frozen members are shown but not selectable.

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `query` | string | (required) | Matches name, phone, or member number |
| `includeInactive` | bool | false | Include inactive members |

**Response 200:** `List<`[`MemberSearchResult`](#membersearchresult)`>`
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "memberNumber": "MEM-001",
    "fullName": "Ahmed Ali",
    "fullNameAr": "أحمد علي",
    "phoneNumber": "+201234567890",
    "profilePhotoUrl": "/uploads/photos/ahmed.jpg",
    "membershipStatus": "active",
    "planName": "Monthly Unlimited",
    "planNameAr": "شهري غير محدود",
    "isSelectable": true,
    "unselectableReason": null,
    "unselectableReasonAr": null
  }
]
```

---

#### 5.4 Get Today's Attendance
```
GET /api/attendance/today?filter=all
```

**Policy:** `AnyStaff`

Returns today's attendance records for the live dashboard.

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `filter` | string | "all" | Filter by entry method: `qr`, `manual`, or `all` |

**Response 200:** `List<`[`TodayAttendanceDto`](#todayattendancedto)`>`
```json
[
  {
    "id": "5fa85f64-5717-4562-b3fc-2c963f66afa8",
    "memberId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "memberNumber": "MEM-001",
    "memberName": "Ahmed Ali",
    "memberNameAr": "أحمد علي",
    "checkInAtUtc": "2026-05-10T08:30:00Z",
    "checkOutAtUtc": null,
    "entryMethod": "qr",
    "planName": "Monthly Unlimited"
  }
]
```

---

### 6. Admin / Staff Management (`AdminController`)

**Base Route:** `/api/admin`  
**Auth Required:** `OwnerOnly` (all endpoints)

#### 6.1 List Staff Users
```
GET /api/admin/staff
```

Returns all staff users for current tenant (excludes owner).

**Response 200:** `List<`[`StaffListItemDto`](#stafflistitemdto)`>`
```json
[
  {
    "id": "8fa85f64-5717-4562-b3fc-2c963f66afa9",
    "fullName": "Sara Trainer",
    "email": "sara@gym.com",
    "role": "Trainer",
    "isActive": true,
    "lastLoginAt": "2026-05-09T14:00:00Z",
    "createdAtUtc": "2026-01-01T00:00:00Z"
  }
]
```

---

#### 6.2 Get Staff User Details
```
GET /api/admin/staff/{id}
```

**Response 200:** [`StaffDetailDto`](#staffdetaildto)
```json
{
  "id": "8fa85f64-5717-4562-b3fc-2c963f66afa9",
  "fullName": "Sara Trainer",
  "email": "sara@gym.com",
  "role": "Trainer",
  "isActive": true,
  "lastLoginAt": "2026-05-09T14:00:00Z",
  "createdAtUtc": "2026-01-01T00:00:00Z",
  "updatedAtUtc": null
}
```

**Error:** `404` — Staff user not found

---

#### 6.3 Create Staff User
```
POST /api/admin/staff
```

Creates a new staff user. Role must be `manager` or `trainer` (NOT `owner`).

**Request Body:** [`CreateStaffRequest`](#createstaffrequest)
```json
{
  "fullName": "Sara Trainer",
  "email": "sara@gym.com",
  "password": "YOUR_PASSWORD",
  "role": "trainer"
}
```

**Response 201:** [`StaffDetailDto`](#staffdetaildto)

**Error:** `400` — Validation error (duplicate email, invalid role)

---

#### 6.4 Update Staff User
```
PUT /api/admin/staff/{id}
```

**Request Body:** [`UpdateStaffRequest`](#updatestaffrequest)
```json
{
  "fullName": "Sara Senior Trainer",
  "role": "manager",
  "isActive": true
}
```

**Response 200:** [`StaffDetailDto`](#staffdetaildto)

**Errors:** `400` — Validation error | `404` — Not found

---

#### 6.5 Delete Staff User (Soft Delete)
```
DELETE /api/admin/staff/{id}
```

Marks staff user as inactive.

**Response 200:**
```json
{
  "message": "Staff user deleted successfully / تم حذف المستخدم بنجاح"
}
```

**Error:** `404` — Not found

---

#### 6.6 Reset Staff Password
```
POST /api/admin/staff/{id}/reset-password
```

**Request Body:** [`ResetPasswordRequest`](#resetpasswordrequest)
```json
{
  "newPassword": "NewP@ssw0rd!"
}
```

**Response 200:**
```json
{
  "message": "Password reset successfully / تم إعادة تعيين كلمة المرور بنجاح"
}
```

**Errors:** `400` — Empty password | `404` — Not found

---

### 7. Tenant Settings (`TenantSettingsController`)

**Base Route:** `/api/settings`  
**Auth Required:** Varies per endpoint

#### 7.1 Get Tenant Settings
```
GET /api/settings
```

**Policy:** `OwnerOnly`

**Response 200:** [`TenantSettingsDto`](#tenantsettingsdto)
```json
{
  "tenantId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
  "gymName": "FitZone Cairo",
  "gymNameAr": "فيت زون القاهرة",
  "gymCode": "GYM-CAIRO-01",
  "logoUrl": "/uploads/logos/fitzone.png",
  "phoneNumber": "+20212345678",
  "address": "15 Tahrir St, Cairo",
  "isActive": true,
  "createdAtUtc": "2025-12-01T00:00:00Z",
  "updatedAtUtc": "2026-03-15T12:00:00Z"
}
```

**Error:** `404` — Settings not found

---

#### 7.2 Update Tenant Settings
```
PUT /api/settings
```

**Policy:** `OwnerOnly`

`GymName` and `GymNameAr` are required.

**Request Body:** [`UpdateTenantSettingsRequest`](#updatetenantsettingsrequest)
```json
{
  "gymName": "FitZone Cairo Updated",
  "gymNameAr": "فيت زون القاهرة - محدث",
  "logoUrl": "/uploads/logos/fitzone-new.png",
  "phoneNumber": "+20212345679",
  "address": "20 Tahrir St, Cairo"
}
```

**Response 200:** [`TenantSettingsDto`](#tenantsettingsdto)

**Errors:** `400` — Missing required fields | `404` — Not found

---

#### 7.3 Get Gym Code
```
GET /api/settings/gym-code
```

**Policy:** `AnyAuthenticated` (any logged-in user)

**Response 200:**
```json
{
  "gymCode": "GYM-CAIRO-01"
}
```

**Error:** `404` — Not found

---

#### 7.4 Get QR Poster URL
```
GET /api/settings/qr-poster
```

**Policy:** `AnyAuthenticated` (any logged-in user)

Returns the URL for the gym's QR code poster (used for member check-in).

**Response 200:**
```json
{
  "qrPosterUrl": "/uploads/qr/GYM-CAIRO-01.png"
}
```

**Error:** `404` — Not found

---

### 8. Notifications (`NotificationsController`)

**Base Route:** `/api/notifications`  
**Auth Required:** `[Authorize]` (all endpoints)

#### 8.1 Get My Notifications
```
GET /api/notifications?page=1&pageSize=20
```

Returns the current member's notifications, paginated, newest first. Uses `member_id` claim from JWT.

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `page` | int | 1 | Page number |
| `pageSize` | int | 20 | Items per page |

**Response 200:** `List<`[`NotificationDto`](#notificationdto)`>`
```json
[
  {
    "id": "9fa85f64-5717-4562-b3fc-2c963f66afaa",
    "title": "Membership Expiring",
    "titleAr": "اشتراكك على وشك الانتهاء",
    "body": "Your membership expires in 3 days",
    "bodyAr": "اشتراكك ينتهي خلال 3 أيام",
    "channel": "push",
    "sentAt": "2026-05-09T10:00:00Z",
    "isRead": false
  }
]
```

---

#### 8.2 Mark Notification as Read
```
POST /api/notifications/{id}/read
```

Ownership enforced — member can only mark their own notifications.

**Response 200:**
```json
{
  "message": "Notification marked as read"
}
```

**Errors:**
- `403` — Not your notification
- `404` — Notification not found

---

#### 8.3 Send Bulk Notification
```
POST /api/notifications/send-bulk
```

**Policy:** `ManagerOrAbove`

Sends notification to specific members or all active members.

**Request Body:** [`SendBulkNotificationRequest`](#sendbulknotificationrequest)
```json
{
  "memberIds": null,
  "allMembers": true,
  "title": "Gym Closed Tomorrow",
  "titleAr": "الصالة مغلقة غداً",
  "body": "The gym will be closed for maintenance tomorrow",
  "bodyAr": "ستكون الصالة مغلقة للصيانة غداً",
  "channel": "push"
}
```

**Response 200:**
```json
{
  "message": "Notification sent to 150 members"
}
```

**Error:** `400` — Validation error

---

### 9. Invitations (`InvitationController`)

**Base Route:** `/api/invitation`  
**Auth Required:** Varies per endpoint

#### 9.1 Send Guest Invitation
```
POST /api/invitation/send
```

**Policy:** `AuthenticatedMember`

Member sends a guest invitation. Atomically checks and enforces monthly quota based on plan's `InvitationQuota`.

**Request Body:** [`SendInvitationRequest`](#sendinvitationrequest)
```json
{
  "guestName": "Mohamed Guest",
  "guestPhoneNumber": "+201111111111",
  "visitDate": "2026-05-15"
}
```

**Response 200:** [`SendInvitationResponse`](#sendinvitationresponse)
```json
{
  "invitationId": "afa85f64-5717-4562-b3fc-2c963f66afab",
  "guestName": "Mohamed Guest",
  "visitDate": "2026-05-15",
  "quotaUsed": 1,
  "quotaRemaining": 2,
  "message": "Invitation sent successfully / تم إرسال الدعوة بنجاح",
  "messageAr": "تم إرسال الدعوة بنجاح"
}
```

**Error:** `400` — Quota exceeded, invalid data

---

#### 9.2 Get Invitation History
```
GET /api/invitation/history
```

**Policy:** `AuthenticatedMember`

Returns the authenticated member's invitation history.

**Response 200:** `List<`[`InvitationHistoryResponse`](#invitationhistoryresponse)`>`
```json
[
  {
    "id": "afa85f64-5717-4562-b3fc-2c963f66afab",
    "guestName": "Mohamed Guest",
    "guestPhoneNumber": "+201111111111",
    "visitDate": "2026-05-15",
    "status": "sent",
    "sentAtUtc": "2026-05-10T09:00:00Z",
    "visitedAtUtc": null,
    "convertedAtUtc": null
  }
]
```

---

### 10. Analytics (`AnalyticsController`)

**Base Route:** `/api/analytics`  
**Auth Required:** `[Authorize]` (all endpoints)

All data from pre-computed snapshots for performance.

#### 10.1 Dashboard Overview
```
GET /api/analytics/overview
```

**Policy:** `ManagerOrAbove`

**Response 200:** [`DashboardOverviewDto`](#dashboardoverviewdto)
```json
{
  "activeMembers": 150,
  "expiredMembers": 30,
  "newMembersThisMonth": 12,
  "revenueThisMonth": 75000.00,
  "checkinsToday": 45,
  "checkinsThisWeek": 280,
  "snapshotTimeUtc": "2026-05-10T08:00:00Z"
}
```

---

#### 10.2 Revenue Chart
```
GET /api/analytics/revenue?months=6
```

**Policy:** `OwnerOnly`

**Query Parameters:**
| Param | Type | Default | Description |
|---|---|---|---|
| `months` | int | 6 | Number of months (1–36) |

**Response 200:** [`RevenueChartDto`](#revenuechartdto)
```json
{
  "labels": ["Dec", "Jan", "Feb", "Mar", "Apr", "May"],
  "values": [65000, 70000, 72000, 68000, 71000, 75000]
}
```

---

#### 10.3 Attendance Heatmap
```
GET /api/analytics/heatmap
```

**Policy:** `ManagerOrAbove`

Returns 7×24 matrix (7 days × 24 hours). `[0][0]` = Monday 00:00–01:00, `[6][23]` = Sunday 23:00–00:00.

**Response 200:** [`AttendanceHeatmapDto`](#attendanceheatmapdto)
```json
{
  "data": [
    [0, 0, 0, 0, 0, 0, 2, 8, 15, 20, 18, 12, 10, 8, 6, 5, 4, 3, 2, 1, 0, 0, 0, 0],
    [0, 0, 0, 0, 0, 0, 1, 6, 12, 18, 16, 10, 8, 7, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0],
    '...'
  ]
}
```

---

#### 10.4 Member Status Breakdown
```
GET /api/analytics/members-status
```

**Policy:** `ManagerOrAbove`

**Response 200:** [`MemberStatusPieDto`](#memberstatuspiedto)
```json
{
  "active": 150,
  "expired": 30,
  "frozen": 5,
  "cancelled": 2,
  "total": 187
}
```

---

#### 10.5 Invitation Funnel
```
GET /api/analytics/invitations
```

**Policy:** `ManagerOrAbove`

**Response 200:** [`InvitationFunnelDto`](#invitationfunneldto)
```json
{
  "sent": 200,
  "visited": 80,
  "converted": 25,
  "conversionRate": 12.5
}
```

---

### 11. Reports (`ReportsController`)

**Base Route:** `/api/reports`  
**Auth Required:** `[Authorize]` (all endpoints)

Real-time queries from source tables (not snapshots).

#### 11.1 Attendance Summary
```
GET /api/reports/attendance-summary?from=2026-05-01&to=2026-05-31
```

**Policy:** `ManagerOrAbove`

**Query Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `from` | DateOnly | Yes | Start date (YYYY-MM-DD) |
| `to` | DateOnly | Yes | End date (YYYY-MM-DD) |

**Response 200:** `List<`[`AttendanceSummaryItemDto`](#attendancesummaryitemdto)`>`
```json
[
  {
    "date": "2026-05-01",
    "checkinCount": 45,
    "uniqueMembers": 38
  }
]
```

**Error:** `400` — `from` must be before `to`

---

#### 11.2 Revenue Detail
```
GET /api/reports/revenue-detail?from=2026-05-01&to=2026-05-31&method=cash
```

**Policy:** `OwnerOnly`

**Query Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `from` | DateOnly | Yes | Start date |
| `to` | DateOnly | Yes | End date |
| `method` | string? | No | Filter by payment method: `cash`, `paymob`, `fawry` |

**Response 200:** `List<`[`RevenueDetailItemDto`](#revenuedetailitemdto)`>`
```json
[
  {
    "id": "bfa85f64-5717-4562-b3fc-2c963f66afac",
    "transactionDate": "2026-05-01T10:00:00Z",
    "memberName": "Ahmed Ali",
    "planName": "Monthly Unlimited",
    "amount": 500.00,
    "paymentMethod": "cash"
  }
]
```

---

#### 11.3 Peak Hours
```
GET /api/reports/peak-hours
```

**Policy:** `ManagerOrAbove`

Returns top 5 busiest time slots.

**Response 200:** `List<`[`PeakHourItemDto`](#peakhouritemdto)`>`
```json
[
  {
    "timeSlot": "10:00-11:00",
    "checkinCount": 120,
    "percentage": 18.5
  }
]
```

---

#### 11.4 Member Retention
```
GET /api/reports/member-retention
```

**Policy:** `OwnerOnly`

**Response 200:** [`MemberRetentionDto`](#memberretentiondto)
```json
{
  "totalExpiredMemberships": 200,
  "renewedMemberships": 85,
  "retentionRate": 42.5
}
```

---

### 12. Payments — Webhooks (`PaymentsController`)

**Base Route:** `/api/payments`  
**Auth Required:** `[AllowAnonymous]` — Payment gateways don't send JWT tokens. Security is enforced via HMAC signature verification.

> ⚠️ **Frontend/Flutter developers do NOT call these endpoints.** They are called by Paymob/Fawry servers after payment completion. Included here for completeness.

#### 12.1 Paymob Webhook
```
POST /api/payments/paymob-webhook
```

Called by Paymob after payment. Verifies HMAC-SHA512 signature from `X-Hmac` header.

**Response 200:**
```json
{ "status": "processed" }
```

#### 12.2 Fawry Webhook
```
POST /api/payments/fawry-webhook
```

Called by Fawry after payment. Verifies SHA-256 signature from `X-Fawry-Signature` header.

**Response 200:**
```json
{ "status": "processed" }
```

---

### 13. Health (`HealthController`)

**Base Route:** `/api/health`  
**Auth Required:** None

#### 13.1 Health Check
```
GET /api/health
```

**Response 200:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-05-10T20:00:00Z"
}
```

---

## DTO Reference (Request & Response Bodies)

### Auth DTOs

#### LoginRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `email` | string | ✅ | User's email address |
| `password` | string | ✅ | User's password |
| `gymCode` | string | ✅ | Gym code to identify tenant |

#### LoginResponse
| Field | Type | Description |
|---|---|---|
| `accessToken` | string | JWT access token (15 min lifetime) |
| `refreshToken` | string | Refresh token (30 days lifetime) |
| `expiresAtUtc` | DateTime | UTC expiration of access token |
| `user` | [UserInfo](#userinfo) | User profile info |

#### UserInfo
| Field | Type | Description |
|---|---|---|
| `id` | Guid | User ID |
| `email` | string | User email |
| `fullName` | string | Full name |
| `role` | string | Role: `Owner`, `Manager`, `Trainer`, `Member` |
| `tenantId` | Guid | Tenant (gym) ID |
| `gymCode` | string | Gym code |

#### RefreshTokenRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `refreshToken` | string | ✅ | Refresh token from login/previous refresh |

#### MemberOtpRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `phoneNumber` | string | ✅ | Phone in international format (e.g., `+201234567890`) |
| `gymCode` | string | ✅ | Gym code to identify tenant |

#### MemberOtpVerifyRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `phoneNumber` | string | ✅ | Must match the number OTP was sent to |
| `gymCode` | string | ✅ | Gym code identifying the tenant |
| `otp` | string | ✅ | 6-digit OTP code |

---

### Member DTOs

#### CreateMemberRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `fullName` | string | ✅ | Full name (English) |
| `fullNameAr` | string | ✅ | Full name (Arabic) |
| `phone` | string | ✅ | Phone number |
| `dateOfBirth` | DateOnly | ✅ | Date of birth (`YYYY-MM-DD`) |
| `nationalId` | string? | ❌ | National ID number |
| `emergencyContact` | string? | ❌ | Emergency contact phone |
| `email` | string? | ❌ | Email address |
| `notes` | string? | ❌ | Additional notes |

#### UpdateMemberRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `fullName` | string? | ❌ | Full name (English) |
| `fullNameAr` | string? | ❌ | Full name (Arabic) |
| `phone` | string? | ❌ | Phone number |
| `dateOfBirth` | DateOnly? | ❌ | Date of birth |
| `nationalId` | string? | ❌ | National ID |
| `emergencyContact` | string? | ❌ | Emergency contact |
| `email` | string? | ❌ | Email |
| `notes` | string? | ❌ | Notes |

> All fields optional — only provided fields are updated.

#### MemberDetailDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Member ID |
| `memberNumber` | string | Auto-generated member number (e.g., `MEM-001`) |
| `fullName` | string | Full name (English) |
| `fullNameAr` | string | Full name (Arabic) |
| `phone` | string | Phone number |
| `email` | string | Email address |
| `dateOfBirth` | DateOnly | Date of birth |
| `profilePhotoUrl` | string? | Profile photo URL |
| `notes` | string? | Additional notes |
| `isActive` | bool | Whether member is active |
| `invitationQuotaRemaining` | int | Remaining guest invitations |
| `createdAtUtc` | DateTime | Creation timestamp |
| `currentMembership` | [MembershipSummaryDto](#membershipsummarydto)? | Current active membership |
| `recentAttendance` | List<[AttendanceSummaryDto](#attendancesummarydto)> | Last 5 attendance records |

#### MemberListItemDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Member ID |
| `memberNumber` | string | Member number |
| `fullName` | string | Full name (EN) |
| `fullNameAr` | string | Full name (AR) |
| `phone` | string | Phone |
| `isActive` | bool | Active status |
| `activePlan` | string? | Current plan name (EN) |
| `activePlanAr` | string? | Current plan name (AR) |
| `expiryDate` | DateOnly? | Membership expiry date |
| `membershipStatus` | string? | Status: `active`, `expired`, `frozen`, `cancelled` |

#### MembershipSummaryDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Membership ID |
| `planName` | string | Plan name (EN) |
| `planNameAr` | string | Plan name (AR) |
| `planType` | string | Plan type |
| `status` | string | Membership status |
| `startDate` | DateOnly | Start date |
| `endDate` | DateOnly | End date |
| `sessionsRemaining` | int? | Sessions left (null for unlimited) |
| `frozenFromDate` | DateOnly? | Freeze start date |
| `frozenUntilDate` | DateOnly? | Freeze end date |
| `amountPaid` | decimal | Amount paid |
| `paymentMethod` | string | Payment method |

#### AttendanceSummaryDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Attendance record ID |
| `checkInAtUtc` | DateTime | Check-in time |
| `checkOutAtUtc` | DateTime? | Check-out time |
| `entryMethod` | string | `qr` or `manual` |

#### FreezeMembershipRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `frozenUntil` | DateTime | ✅ | Date to unfreeze (end date extended by freeze duration) |
| `reason` | string? | ❌ | Reason for freezing |

---

### Membership DTOs

#### AssignMembershipRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `planId` | Guid | ✅ | Membership plan to assign |
| `paymentMethod` | string | ✅ | `cash`, `paymob`, or `fawry` |

> `memberId` is in the URL path, not the body. `startDate` is auto-calculated.

#### RenewMembershipRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `planId` | Guid? | ❌ | New plan ID (null = renew same plan) |
| `paymentMethod` | string | ✅ | `cash`, `paymob`, `fawry`, or `vodafone_cash` |
| `amountPaid` | decimal | ✅ | Amount paid for renewal |

#### MembershipDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Membership ID |
| `planName` | string | Plan name (EN) |
| `planNameAr` | string | Plan name (AR) |
| `planType` | string | Plan type |
| `startDate` | DateOnly | Start date |
| `endDate` | DateOnly | End date |
| `status` | string | Status: `active`, `pending`, `expired`, `frozen`, `cancelled` |
| `sessionsRemaining` | int? | Sessions left (null for unlimited) |
| `amountPaid` | decimal | Amount paid |
| `paymentMethod` | string | Payment method |
| `paymentDate` | DateTime? | When payment was made |
| `autoRenew` | bool | Auto-renew flag |
| `frozenFromDate` | DateOnly? | Freeze start |
| `frozenUntilDate` | DateOnly? | Freeze end |
| `daysRemaining` | int | Computed: days until end date |

#### MembershipHistoryItemDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Membership ID |
| `planName` | string | Plan name (EN) |
| `planNameAr` | string | Plan name (AR) |
| `planType` | string | Plan type |
| `startDate` | DateOnly | Start date |
| `endDate` | DateOnly | End date |
| `status` | string | Status |
| `amountPaid` | decimal | Amount paid |
| `paymentMethod` | string | Payment method |
| `paymentDate` | DateTime? | Payment date |
| `createdAtUtc` | DateTime | Record creation time |

---

### Plan DTOs

#### CreatePlanRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | ✅ | Plan name (EN) |
| `nameAr` | string | ✅ | Plan name (AR) |
| `description` | string? | ❌ | Description (EN) |
| `descriptionAr` | string? | ❌ | Description (AR) |
| `planType` | string | ✅ | `monthly_unlimited`, `session_pack`, `time_limited`, `pt_credits`, `family` |
| `price` | decimal | ✅ | Price in EGP |
| `durationDays` | int | ✅ | Duration in days |
| `sessionCount` | int? | ❌ | Required for `session_pack` (10, 20, or 50) |
| `timeRestrictionStart` | TimeOnly? | ❌ | Required for `time_limited` (e.g., `"08:00:00"`) |
| `timeRestrictionEnd` | TimeOnly? | ❌ | Required for `time_limited` (e.g., `"17:00:00"`) |
| `invitationQuota` | int | ❌ | Guest invitations for `family` plans (default: 0) |

#### UpdatePlanRequest
Same structure as [CreatePlanRequest](#createplanrequest).

#### PlanDetailDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Plan ID |
| `name` | string | Plan name (EN) |
| `nameAr` | string | Plan name (AR) |
| `description` | string | Description (EN) |
| `descriptionAr` | string | Description (AR) |
| `planType` | string | Plan type |
| `price` | decimal | Price |
| `currency` | string | Currency (default: `EGP`) |
| `durationDays` | int | Duration in days |
| `sessionCount` | int? | Session count |
| `timeRestrictionStart` | TimeOnly? | Time restriction start |
| `timeRestrictionEnd` | TimeOnly? | Time restriction end |
| `invitationQuota` | int | Guest invitation quota |
| `isActive` | bool | Whether plan is active |
| `activeMemberships` | int | Current active memberships on this plan |
| `totalMemberships` | int | All-time memberships on this plan |
| `createdAtUtc` | DateTime | Creation time |
| `updatedAtUtc` | DateTime? | Last update time |

#### PlanListItemDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Plan ID |
| `name` | string | Plan name (EN) |
| `nameAr` | string | Plan name (AR) |
| `planType` | string | Plan type |
| `price` | decimal | Price |
| `currency` | string | Currency (`EGP`) |
| `durationDays` | int | Duration |
| `isActive` | bool | Active flag |
| `createdAtUtc` | DateTime | Creation time |

---

### Attendance DTOs

#### QrCheckinRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `gymCode` | string | ✅ | Gym code from static QR (e.g., `GYM-CAIRO-01`) |

#### QrCheckinResponse
| Field | Type | Description |
|---|---|---|
| `attendanceId` | Guid | Attendance record ID |
| `memberName` | string | Member name (EN) |
| `memberNameAr` | string | Member name (AR) |
| `checkInAtUtc` | DateTime | Check-in time |
| `planName` | string | Plan name (EN) |
| `planNameAr` | string | Plan name (AR) |
| `sessionsRemaining` | int? | Sessions left (null for unlimited) |
| `message` | string | Success message (EN) |
| `messageAr` | string | Success message (AR) |

#### ManualCheckinRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `memberId` | Guid | ✅ | Member to check in |
| `reason` | [ManualCheckinReason](#manualcheckinreason) | ✅ | Reason enum value (1–4) |
| `notes` | string? | ❌ | Notes for `Other` reason |

#### ManualCheckinResponse
| Field | Type | Description |
|---|---|---|
| `attendanceId` | Guid | Attendance record ID |
| `memberName` | string | Member name (EN) |
| `memberNameAr` | string | Member name (AR) |
| `checkInAtUtc` | DateTime | Check-in time |
| `planName` | string | Plan name (EN) |
| `planNameAr` | string | Plan name (AR) |
| `sessionsRemaining` | int? | Sessions left |
| `staffName` | string | Staff who performed check-in |
| `message` | string | Success message (EN) |
| `messageAr` | string | Success message (AR) |

#### MemberSearchRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `query` | string | ✅ | Search query (name, phone, member number) |
| `includeInactive` | bool | ❌ | Include inactive members (default: false) |

#### MemberSearchResult
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Member ID |
| `memberNumber` | string | Member number |
| `fullName` | string | Full name (EN) |
| `fullNameAr` | string | Full name (AR) |
| `phoneNumber` | string | Phone number |
| `profilePhotoUrl` | string | Photo URL |
| `membershipStatus` | string | `active`, `expired`, `frozen`, `cancelled`, `none` |
| `planName` | string | Current plan name (EN) |
| `planNameAr` | string | Current plan name (AR) |
| `isSelectable` | bool | Can be selected for manual check-in |
| `unselectableReason` | string? | Why not selectable (EN) |
| `unselectableReasonAr` | string? | Why not selectable (AR) |

#### TodayAttendanceDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Attendance record ID |
| `memberId` | Guid | Member ID |
| `memberNumber` | string | Member number |
| `memberName` | string | Member name (EN) |
| `memberNameAr` | string | Member name (AR) |
| `checkInAtUtc` | DateTime | Check-in time |
| `checkOutAtUtc` | DateTime? | Check-out time |
| `entryMethod` | string | `qr` or `manual` |
| `planName` | string? | Plan name |

---

### Admin DTOs

#### CreateStaffRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `fullName` | string | ✅ | Full name |
| `email` | string | ✅ | Email address |
| `password` | string | ✅ | Password |
| `role` | string | ✅ | `manager` or `trainer` (NOT `owner`) |

#### UpdateStaffRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `fullName` | string | ✅ | Full name |
| `role` | string | ✅ | `manager` or `trainer` |
| `isActive` | bool | ✅ | Active status |

#### StaffDetailDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Staff user ID |
| `fullName` | string | Full name |
| `email` | string | Email |
| `role` | string | `owner`, `manager`, or `trainer` |
| `isActive` | bool | Active status |
| `lastLoginAt` | DateTime? | Last login time |
| `createdAtUtc` | DateTime | Creation time |
| `updatedAtUtc` | DateTime? | Last update time |

#### StaffListItemDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Staff user ID |
| `fullName` | string | Full name |
| `email` | string | Email |
| `role` | string | Role |
| `isActive` | bool | Active status |
| `lastLoginAt` | DateTime? | Last login |
| `createdAtUtc` | DateTime | Creation time |

#### ResetPasswordRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `newPassword` | string | ✅ | New password (cannot be empty) |

#### TenantSettingsDto
| Field | Type | Description |
|---|---|---|
| `tenantId` | Guid | Tenant ID |
| `gymName` | string | Gym name (EN) |
| `gymNameAr` | string | Gym name (AR) |
| `gymCode` | string | Unique gym code for QR/registration |
| `logoUrl` | string? | Logo URL |
| `phoneNumber` | string? | Contact phone |
| `address` | string? | Address |
| `isActive` | bool | Active status |
| `createdAtUtc` | DateTime | Creation time |
| `updatedAtUtc` | DateTime? | Last update time |

#### UpdateTenantSettingsRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `gymName` | string | ✅ | Gym name (EN) — required |
| `gymNameAr` | string | ✅ | Gym name (AR) — required |
| `logoUrl` | string? | ❌ | Logo URL |
| `phoneNumber` | string? | ❌ | Contact phone |
| `address` | string? | ❌ | Address |

#### InvitationQuotasDto
| Field | Type | Description |
|---|---|---|
| `quotasByPlanType` | Dictionary<string, int> | Quota per plan type (e.g., `{"monthly_unlimited": 3, "session_pack": 2, "family": 5}`) |

---

### Analytics DTOs

#### DashboardOverviewDto
| Field | Type | Description |
|---|---|---|
| `activeMembers` | int | Active member count |
| `expiredMembers` | int | Expired member count |
| `newMembersThisMonth` | int | New members this month |
| `revenueThisMonth` | decimal | Revenue this month (EGP) |
| `checkinsToday` | int | Check-ins today |
| `checkinsThisWeek` | int | Check-ins this week |
| `snapshotTimeUtc` | DateTime | When snapshot was taken |

#### RevenueChartDto
| Field | Type | Description |
|---|---|---|
| `labels` | List<string> | Month labels (e.g., `["Jan", "Feb"]`) |
| `values` | List<decimal> | Revenue values per month |

#### AttendanceHeatmapDto
| Field | Type | Description |
|---|---|---|
| `data` | int[][] | 7×24 matrix. `[day][hour]` = check-in count. Day 0=Monday, 6=Sunday |

#### MemberStatusPieDto
| Field | Type | Description |
|---|---|---|
| `active` | int | Active count |
| `expired` | int | Expired count |
| `frozen` | int | Frozen count |
| `cancelled` | int | Cancelled count |
| `total` | int | Computed total |

#### InvitationFunnelDto
| Field | Type | Description |
|---|---|---|
| `sent` | int | Invitations sent |
| `visited` | int | Guests who visited |
| `converted` | int | Guests who became members |
| `conversionRate` | decimal | Conversion percentage (0–100) |

#### AttendanceSummaryItemDto
| Field | Type | Description |
|---|---|---|
| `date` | DateOnly | Date |
| `checkinCount` | int | Total check-ins |
| `uniqueMembers` | int | Unique members who checked in |

#### RevenueDetailItemDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Transaction ID |
| `transactionDate` | DateTime | Transaction date |
| `memberName` | string | Member name |
| `planName` | string | Plan name |
| `amount` | decimal | Amount (EGP) |
| `paymentMethod` | string | `cash`, `paymob`, `fawry` |

#### PeakHourItemDto
| Field | Type | Description |
|---|---|---|
| `timeSlot` | string | Time range (e.g., `"10:00-11:00"`) |
| `checkinCount` | int | Total check-ins in this slot |
| `percentage` | decimal | Percentage of total (0–100) |

#### MemberRetentionDto
| Field | Type | Description |
|---|---|---|
| `totalExpiredMemberships` | int | Total expired memberships |
| `renewedMemberships` | int | Renewed memberships |
| `retentionRate` | decimal | Retention percentage (0–100) |

---

### Invitation DTOs

#### SendInvitationRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `guestName` | string | ✅ | Guest's full name |
| `guestPhoneNumber` | string | ✅ | Guest's phone (international format) |
| `visitDate` | DateOnly | ✅ | Planned visit date |

#### SendInvitationResponse
| Field | Type | Description |
|---|---|---|
| `invitationId` | Guid | Created invitation ID |
| `guestName` | string | Guest name |
| `visitDate` | DateOnly | Visit date |
| `quotaUsed` | int | Invitations used this month |
| `quotaRemaining` | int | Remaining invitations |
| `message` | string | Success message (EN) |
| `messageAr` | string | Success message (AR) |

#### InvitationHistoryResponse
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Invitation ID |
| `guestName` | string | Guest name |
| `guestPhoneNumber` | string | Guest phone |
| `visitDate` | DateOnly | Visit date |
| `status` | string | `sent`, `visited`, `converted`, `expired` |
| `sentAtUtc` | DateTime | When invitation was sent |
| `visitedAtUtc` | DateTime? | When guest visited |
| `convertedAtUtc` | DateTime? | When guest became member |

---

### Notification DTOs

#### NotificationDto
| Field | Type | Description |
|---|---|---|
| `id` | Guid | Notification ID |
| `title` | string | Title (EN) |
| `titleAr` | string | Title (AR) |
| `body` | string | Body text (EN) |
| `bodyAr` | string | Body text (AR) |
| `channel` | string | `push` or `whatsapp` |
| `sentAt` | DateTime? | When sent |
| `isRead` | bool | Whether read |

#### SendBulkNotificationRequest
| Field | Type | Required | Description |
|---|---|---|---|
| `memberIds` | List<Guid>? | ❌ | Specific member IDs. Null = use `allMembers` |
| `allMembers` | bool | ❌ | If true, notify all active members (default: false) |
| `title` | string | ✅ | Title (EN) |
| `titleAr` | string | ✅ | Title (AR) |
| `body` | string | ✅ | Body text (EN) |
| `bodyAr` | string | ✅ | Body text (AR) |
| `channel` | string | ✅ | `push` or `whatsapp` (default: `push`) |

---

### Error DTO

#### ErrorResponse
| Field | Type | Description |
|---|---|---|
| `message` | string | Error description |
| `details` | string? | Additional details |
| `statusCode` | int | HTTP status code |

---

## Enums

### ManualCheckinReason

| Value | Name | Description |
|---|---|---|
| 1 | `DeadPhone` | Member's phone is dead/out of battery |
| 2 | `NoAppYet` | Member hasn't installed the app yet |
| 3 | `AppIssue` | App is experiencing technical issues |
| 4 | `Other` | Other reason (notes should be provided) |

### Membership Status Values
| Value | Description |
|---|---|
| `active` | Currently active membership |
| `pending` | Awaiting payment confirmation (gateway) |
| `expired` | Membership has expired |
| `frozen` | Membership is frozen (paused) |
| `cancelled` | Membership was cancelled |

### Plan Type Values
| Value | Description |
|---|---|
| `monthly_unlimited` | Unlimited access for duration |
| `session_pack` | Fixed number of sessions (10, 20, or 50) |
| `time_limited` | Access restricted to time window |
| `pt_credits` | Personal training credits |
| `family` | Family plan with guest invitations |

### Payment Method Values
| Value | Description |
|---|---|
| `cash` | Cash payment (immediate activation) |
| `paymob` | Paymob gateway (pending → webhook activation) |
| `fawry` | Fawry gateway (pending → webhook activation) |
| `vodafone_cash` | Vodafone Cash (renewal only) |

### Entry Method Values
| Value | Description |
|---|---|
| `qr` | QR code self-check-in |
| `manual` | Staff manual check-in |

### Role Values
| Value | Description |
|---|---|
| `Owner` | Gym owner — full access |
| `Manager` | Manager — most operations |
| `Trainer` | Trainer — read-only + manual check-in |
| `Member` | Gym member — limited access |

### Invitation Status Values
| Value | Description |
|---|---|
| `sent` | Invitation sent |
| `visited` | Guest visited the gym |
| `converted` | Guest became a member |
| `expired` | Invitation expired |

### Notification Channel Values
| Value | Description |
|---|---|
| `push` | Push notification |
| `whatsapp` | WhatsApp message |

---

## Real-time (SignalR)

The API exposes a SignalR hub for real-time attendance updates.

**Hub Endpoint:** `/hubs/attendance`

**Connection:** Pass JWT token via `access_token` query parameter:
```
wss://your-domain.com/hubs/attendance?access_token={jwt}
```

This is configured in `Program.cs` — the `OnMessageReceived` event extracts the token from the query string for WebSocket connections.

---

## Rate Limiting

| Endpoint | Policy | Limit |
|---|---|---|
| `POST /api/attendance/qr-checkin` | `checkin-policy` | 30 requests/minute per IP |

When rate-limited, the API returns:
- **Status:** `429`
- **Body:** `{"error":"لقد تجاوزت الحد المسموح به. حاول بعد دقيقة / Too many requests. Try again in a minute."}`

---

## Flutter Integration Tips

### 1. HTTP Client Setup
```dart
import 'package:dio/dio.dart';

final dio = Dio(BaseOptions(
  baseUrl: 'https://your-domain.com/api',
  headers: {'Content-Type': 'application/json'},
));

// Add auth interceptor
dio.interceptors.add(InterceptorsWrapper(
  onRequest: (options, handler) {
    final token = await secureStorage.read(key: 'access_token');
    if (token != null) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  },
  onError: (error, handler) async {
    if (error.response?.statusCode == 401) {
      // Token expired — try refresh
      final refreshed = await _refreshToken();
      if (refreshed) {
        return dio.request(error.requestOptions.path,
            options: Options(method: error.requestOptions.method));
      }
    }
    handler.next(error);
  },
));
```

### 2. Token Storage
Use `flutter_secure_storage` for storing tokens:
```dart
await secureStorage.write(key: 'access_token', value: loginResponse.accessToken);
await secureStorage.write(key: 'refresh_token', value: loginResponse.refreshToken);
await secureStorage.write(key: 'expires_at', value: loginResponse.expiresAtUtc.toIso8601String());
```

### 3. Auto Token Refresh
Check `Token-Expired` response header or track expiry time:
```dart
if (response.headers.value('token-expired') == 'true') {
  await _refreshToken();
  // Retry the request
}
```

### 4. DateOnly Serialization
C# `DateOnly` serializes as `"YYYY-MM-DD"` string. In Dart:
```dart
// Sending
'dateOfBirth': dateOfBirth.toIso8601String().split('T')[0], // "1995-03-15"

// Receiving
final dob = DateOnly.parse(json['dateOfBirth']); // Use a DateOnly class or DateTime.parse()
```

### 5. TimeOnly Serialization
C# `TimeOnly` serializes as `"HH:mm:ss"` string:
```dart
'timeRestrictionStart': '08:00:00',
'timeRestrictionEnd': '17:00:00',
```

### 6. Enum Values
Send enum values as **integers** (for `ManualCheckinReason`) or as **strings** (for `planType`, `paymentMethod`, `role`, etc.):
```dart
// ManualCheckinReason — send as int
'reason': 1, // DeadPhone

// Plan type — send as string
'planType': 'session_pack',
```

### 7. Bilingual Fields
Many responses include bilingual fields (`name`/`nameAr`, `message`/`messageAr`). Use locale to pick the right one:
```dart
String getLocalizedName(Map<String, dynamic> json, String locale) {
  return locale == 'ar' ? (json['nameAr'] ?? json['name']) : (json['name'] ?? json['nameAr']);
}
```

### 8. QR Check-in Flow (Flutter)
```dart
// 1. Scan QR code → extract gymCode
// 2. Call POST /api/attendance/qr-checkin
final response = await dio.post('/attendance/qr-checkin', data: {
  'gymCode': scannedGymCode,
});
// 3. Show success/error to user
```

### 9. Member OTP Flow (Flutter)
```dart
// Step 1: Request OTP
await dio.post('/auth/member-otp', data: {
  'phoneNumber': '+201234567890',
  'gymCode': 'GYM-CAIRO-01',
});

// Step 2: User enters OTP from SMS
final response = await dio.post('/auth/member-verify', data: {
  'phoneNumber': '+201234567890',
  'gymCode': 'GYM-CAIRO-01',
  'otp': '123456',
});

// Step 3: Store tokens
await secureStorage.write(key: 'access_token', value: response.data['accessToken']);
```

### 10. SignalR for Real-time Attendance
Use `signalr_netcore_hub` or `signalr_client` package:
```dart
final hubConnection = HubConnectionBuilder()
    .withUrl('https://your-domain.com/hubs/attendance', options: HttpConnectionOptions(
      accessTokenFactory: () async => await secureStorage.read(key: 'access_token'),
    ))
    .build();

hubConnection.on('AttendanceUpdate', (args) {
  // Handle real-time attendance event
});
await hubConnection.start();
```

---

## Quick Reference: All Endpoints

| # | Method | Endpoint | Policy | Request Body | Response |
|---|---|---|---|---|---|
| 1 | POST | `/api/auth/login` | Anonymous | `LoginRequest` | `LoginResponse` |
| 2 | POST | `/api/auth/refresh` | Anonymous | `RefreshTokenRequest` | `LoginResponse` |
| 3 | POST | `/api/auth/member-otp` | Anonymous | `MemberOtpRequest` | `{ message }` |
| 4 | POST | `/api/auth/member-verify` | Anonymous | `MemberOtpVerifyRequest` | `LoginResponse` |
| 5 | GET | `/api/members` | ManagerOrAbove | — | `List<MemberListItemDto>` |
| 6 | GET | `/api/members/{id}` | AnyStaff | — | `MemberDetailDto` |
| 7 | POST | `/api/members` | ManagerOrAbove | `CreateMemberRequest` | `MemberDetailDto` |
| 8 | PUT | `/api/members/{id}` | ManagerOrAbove | `UpdateMemberRequest` | `MemberDetailDto` |
| 9 | DELETE | `/api/members/{id}` | OwnerOnly | — | `{ message }` |
| 10 | GET | `/api/members/{id}/attendance` | AnyStaff | — | Paginated attendance |
| 11 | GET | `/api/members/{id}/membership` | AnyStaff | — | `MembershipSummaryDto` |
| 12 | POST | `/api/members/{id}/freeze` | ManagerOrAbove | `FreezeMembershipRequest` | `{ message }` |
| 13 | POST | `/api/members/{id}/unfreeze` | ManagerOrAbove | — | `{ message }` |
| 14 | GET | `/api/memberships/{memberId}/current` | AnyStaff | — | `MembershipDto` |
| 15 | GET | `/api/memberships/{memberId}/history` | AnyStaff | — | `List<MembershipHistoryItemDto>` |
| 16 | POST | `/api/memberships/{memberId}/assign` | ManagerOrAbove | `AssignMembershipRequest` | `MembershipDto` |
| 17 | POST | `/api/memberships/{memberId}/renew` | ManagerOrAbove | `RenewMembershipRequest` | `MembershipDto` |
| 18 | GET | `/api/membership-plans` | AnyStaff | — | `List<PlanListItemDto>` |
| 19 | GET | `/api/membership-plans/{id}` | AnyStaff | — | `PlanDetailDto` |
| 20 | POST | `/api/membership-plans` | OwnerOnly | `CreatePlanRequest` | `PlanDetailDto` |
| 21 | PUT | `/api/membership-plans/{id}` | OwnerOnly | `UpdatePlanRequest` | `PlanDetailDto` |
| 22 | DELETE | `/api/membership-plans/{id}` | OwnerOnly | — | `{ message }` |
| 23 | POST | `/api/attendance/qr-checkin` | Member | `QrCheckinRequest` | `QrCheckinResponse` |
| 24 | POST | `/api/attendance/manual-checkin` | ManagerOrAbove | `ManualCheckinRequest` | `ManualCheckinResponse` |
| 25 | GET | `/api/attendance/search-members` | AnyStaff | Query params | `List<MemberSearchResult>` |
| 26 | GET | `/api/attendance/today` | AnyStaff | Query params | `List<TodayAttendanceDto>` |
| 27 | GET | `/api/admin/staff` | OwnerOnly | — | `List<StaffListItemDto>` |
| 28 | GET | `/api/admin/staff/{id}` | OwnerOnly | — | `StaffDetailDto` |
| 29 | POST | `/api/admin/staff` | OwnerOnly | `CreateStaffRequest` | `StaffDetailDto` |
| 30 | PUT | `/api/admin/staff/{id}` | OwnerOnly | `UpdateStaffRequest` | `StaffDetailDto` |
| 31 | DELETE | `/api/admin/staff/{id}` | OwnerOnly | — | `{ message }` |
| 32 | POST | `/api/admin/staff/{id}/reset-password` | OwnerOnly | `ResetPasswordRequest` | `{ message }` |
| 33 | GET | `/api/settings` | OwnerOnly | — | `TenantSettingsDto` |
| 34 | PUT | `/api/settings` | OwnerOnly | `UpdateTenantSettingsRequest` | `TenantSettingsDto` |
| 35 | GET | `/api/settings/gym-code` | AnyAuthenticated | — | `{ gymCode }` |
| 36 | GET | `/api/settings/qr-poster` | AnyAuthenticated | — | `{ qrPosterUrl }` |
| 37 | GET | `/api/notifications` | AnyAuthenticated | Query params | `List<NotificationDto>` |
| 38 | POST | `/api/notifications/{id}/read` | AnyAuthenticated | — | `{ message }` |
| 39 | POST | `/api/notifications/send-bulk` | ManagerOrAbove | `SendBulkNotificationRequest` | `{ message }` |
| 40 | POST | `/api/invitation/send` | Member | `SendInvitationRequest` | `SendInvitationResponse` |
| 41 | GET | `/api/invitation/history` | Member | — | `List<InvitationHistoryResponse>` |
| 42 | GET | `/api/analytics/overview` | ManagerOrAbove | — | `DashboardOverviewDto` |
| 43 | GET | `/api/analytics/revenue` | OwnerOnly | Query: `months` | `RevenueChartDto` |
| 44 | GET | `/api/analytics/heatmap` | ManagerOrAbove | — | `AttendanceHeatmapDto` |
| 45 | GET | `/api/analytics/members-status` | ManagerOrAbove | — | `MemberStatusPieDto` |
| 46 | GET | `/api/analytics/invitations` | ManagerOrAbove | — | `InvitationFunnelDto` |
| 47 | GET | `/api/reports/attendance-summary` | ManagerOrAbove | Query: `from`, `to` | `List<AttendanceSummaryItemDto>` |
| 48 | GET | `/api/reports/revenue-detail` | OwnerOnly | Query: `from`, `to`, `method` | `List<RevenueDetailItemDto>` |
| 49 | GET | `/api/reports/peak-hours` | ManagerOrAbove | — | `List<PeakHourItemDto>` |
| 50 | GET | `/api/reports/member-retention` | OwnerOnly | — | `MemberRetentionDto` |
| 51 | POST | `/api/payments/paymob-webhook` | Anonymous | Raw JSON | `{ status }` |
| 52 | POST | `/api/payments/fawry-webhook` | Anonymous | Raw JSON | `{ status }` |
| 53 | GET | `/api/health` | Anonymous | — | `{ status, timestamp }` |
