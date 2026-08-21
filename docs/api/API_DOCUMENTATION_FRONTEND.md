# GymFlowPro API Documentation for Frontend & Flutter Developers

## Overview

**Base URL**: `http://localhost:5000` (Development)  
**API Version**: 1.0  
**Authentication**: JWT (Bearer Token)  
**Response Format**: JSON  
**Timezone**: UTC (all timestamps are in UTC)

---

## Table of Contents

1. [Authentication](#authentication)
2. [Authorization Policies](#authorization-policies)
3. [Error Handling](#error-handling)
4. [API Endpoints](#api-endpoints)
5. [Data Transfer Objects (DTOs)](#data-transfer-objects)
6. [Common Patterns](#common-patterns)

---

## Authentication

### 1. Staff Login

**Endpoint**: `POST /api/auth/login`

**Description**: Authenticate a staff user (Manager, Admin, or Owner) with email and password.

**Request Body**:
```json
{
  "email": "manager@gymflow.test",
  "password": "YOUR_PASSWORD"
}
```

**Response** (200 OK):
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 900,
  "user": {
    "id": "f8677f57-8b74-46a1-9698-08deadd7eaba",
    "email": "manager@gymflow.test",
    "firstName": "John",
    "lastName": "Manager",
    "role": "Manager",
    "gymCode": "GYM-TEST-01"
  }
}
```

**Status Codes**:
- `200`: Success
- `400`: Invalid request
- `401`: Invalid credentials

---

### 2. Refresh Token

**Endpoint**: `POST /api/auth/refresh`

**Description**: Refresh an expired access token using a valid refresh token. Implements sliding rotation.

**Request Body**:
```json
{
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response** (200 OK):
```json
{
  "accessToken": "new-access-token",
  "refreshToken": "new-refresh-token",
  "expiresIn": 900,
  "user": { ... }
}
```

**Status Codes**:
- `200`: Success
- `401`: Invalid or expired refresh token

---

### 3. Member OTP Request

**Endpoint**: `POST /api/auth/member-otp`

**Description**: Request a 6-digit OTP sent to member's phone number. Valid for 5 minutes.

**Request Body**:
```json
{
  "phoneNumber": "+20123456789"
}
```

**Response** (200 OK):
```json
{
  "message": "OTP sent successfully",
  "expiresIn": 300
}
```

---

### 4. Verify Member OTP

**Endpoint**: `POST /api/auth/member-verify`

**Description**: Verify OTP and receive JWT token. Auto-provisions Identity user if needed.

**Request Body**:
```json
{
  "phoneNumber": "+20123456789",
  "otp": "123456"
}
```

**Response** (200 OK):
```json
{
  "accessToken": "member-jwt-token",
  "refreshToken": "member-refresh-token",
  "expiresIn": 900,
  "user": {
    "id": "8e4e7838-3715-48d2-842f-ee2df786669f",
    "firstName": "Ahmed",
    "lastName": "Member",
    "phoneNumber": "+20123456789",
    "memberNumber": "MEM-00001",
    "role": "Member"
  }
}
```

---

## Authorization Policies

All API endpoints (except Auth) require authorization. Include JWT token in headers:

```
Authorization: Bearer <access_token>
```

### Available Policies

| Policy | Roles | Description |
|--------|-------|-------------|
| `OwnerOnly` | Owner | Full system access |
| `ManagerOrAbove` | Manager, Admin, Owner | Manage members, plans, invitations |
| `AnyStaff` | Receptionist, Manager, Admin, Owner | View data, check-in members |
| `AuthenticatedMember` | Member | Mobile app access, QR check-in |

---

## Error Handling

### Error Response Format

**Status Codes**:
- `400`: Bad Request (validation error, business rule violation)
- `401`: Unauthorized (missing/invalid token)
- `403`: Forbidden (insufficient permissions)
- `404`: Not Found
- `409`: Conflict (e.g., trying to delete plan with active memberships)
- `429`: Too Many Requests (rate limit exceeded)
- `500`: Internal Server Error

**Error Response Body**:
```json
{
  "error": "Field validation failed",
  "message": "Email is required",
  "details": {
    "email": ["Email is required", "Email must be valid"]
  }
}
```

---

## API Endpoints

### Members Management

#### Get All Members (Paginated)

**Endpoint**: `GET /api/members`

**Authorization**: ManagerOrAbove

**Query Parameters**:
- `search` (string): Search by name or member number
- `status` (string): Filter by `active`, `expired`, or `frozen`
- `page` (int): Page number (default: 1)
- `pageSize` (int): Items per page (default: 20)

**Request Example**:
```
GET /api/members?search=ahmed&status=active&page=1&pageSize=20
```

**Response** (200 OK):
```json
{
  "data": [
    {
      "id": "8e4e7838-3715-48d2-842f-ee2df786669f",
      "memberNumber": "MEM-00001",
      "firstName": "Ahmed",
      "lastName": "Hassan",
      "phoneNumber": "+20123456789",
      "email": "admin@gymflow.local",
      "status": "active",
      "membershipId": "12345678-1234-1234-1234-123456789012",
      "planName": "Monthly Unlimited",
      "expiryDate": "2025-06-30",
      "profileImageUrl": "https://..."
    }
  ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 150
}
```

---

#### Get Member Details

**Endpoint**: `GET /api/members/{id}`

**Authorization**: AnyStaff

**Response** (200 OK):
```json
{
  "id": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "memberNumber": "MEM-00001",
  "firstName": "Ahmed",
  "lastName": "Hassan",
  "phoneNumber": "+20123456789",
  "email": "admin@gymflow.local",
  "dateOfBirth": "1990-05-15",
  "gender": "Male",
  "joinDate": "2024-01-15",
  "status": "active",
  "isActive": true,
  "emergencyContact": "Fatima Hassan",
  "emergencyPhone": "+20198765432",
  "currentMembership": {
    "id": "12345678-1234-1234-1234-123456789012",
    "planName": "Monthly Unlimited",
    "status": "active",
    "startDate": "2025-05-30",
    "expiryDate": "2025-06-30",
    "price": 500.00
  },
  "recentAttendance": [
    {
      "date": "2025-05-29",
      "checkInTime": "06:30:00",
      "duration": "01:30:00",
      "entryMethod": "qr"
    }
  ]
}
```

---

#### Create Member

**Endpoint**: `POST /api/members`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "firstName": "Sara",
  "lastName": "Ahmed",
  "phoneNumber": "+20987654321",
  "email": "admin@gymflow.local",
  "dateOfBirth": "1995-03-20",
  "gender": "Female",
  "emergencyContact": "Hassan Ahmed",
  "emergencyPhone": "+20123456789"
}
```

**Response** (201 Created):
```json
{
  "id": "newly-created-id",
  "memberNumber": "MEM-00002",
  "firstName": "Sara",
  "lastName": "Ahmed",
  "phoneNumber": "+20987654321",
  "email": "admin@gymflow.local",
  "status": "active",
  "joinDate": "2025-05-30"
}
```

---

#### Update Member

**Endpoint**: `PUT /api/members/{id}`

**Authorization**: ManagerOrAbove

**Request Body** (partial update):
```json
{
  "phoneNumber": "+20987654321",
  "email": "admin@gymflow.local",
  "emergencyContact": "New Contact"
}
```

**Response** (200 OK): Updated member object

---

#### Deactivate Member

**Endpoint**: `DELETE /api/members/{id}`

**Authorization**: OwnerOnly

**Response** (200 OK):
```json
{
  "message": "Member deactivated successfully"
}
```

---

#### Get Member Attendance History

**Endpoint**: `GET /api/members/{id}/attendance`

**Authorization**: AnyStaff

**Query Parameters**:
- `page` (int): Page number (default: 1)
- `pageSize` (int): Items per page (default: 20)

**Response** (200 OK):
```json
{
  "data": [
    {
      "date": "2025-05-29",
      "checkInTime": "06:30:00",
      "checkOutTime": "08:00:00",
      "duration": "01:30:00",
      "entryMethod": "qr",
      "notes": null
    },
    {
      "date": "2025-05-27",
      "checkInTime": "17:00:00",
      "checkOutTime": "18:45:00",
      "duration": "01:45:00",
      "entryMethod": "manual",
      "notes": "Manual entry by manager"
    }
  ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 45
}
```

---

#### Get Current Membership

**Endpoint**: `GET /api/members/{id}/membership`

**Authorization**: AnyStaff

**Response** (200 OK):
```json
{
  "id": "12345678-1234-1234-1234-123456789012",
  "planName": "Monthly Unlimited",
  "status": "active",
  "startDate": "2025-05-30",
  "expiryDate": "2025-06-30",
  "price": 500.00,
  "sessionsRemaining": null,
  "isFrozen": false,
  "freezeStartDate": null,
  "freezeEndDate": null
}
```

---

### Attendance Management

#### QR Code Check-in

**Endpoint**: `POST /api/attendance/qr-checkin`

**Authorization**: AuthenticatedMember

**Rate Limit**: 5 check-ins per 5 minutes

**Request Body**:
```json
{
  "qrToken": "gym-static-qr-token-value"
}
```

**Response** (200 OK):
```json
{
  "success": true,
  "message": "Welcome Ahmed! Check-in successful.",
  "attendanceId": "att-123456",
  "checkInTime": "2025-05-30T06:30:00Z",
  "remainingTime": "24:30:00",
  "memberName": "Ahmed Hassan",
  "planType": "Monthly Unlimited"
}
```

**Error Cases** (400):
- **Membership Expired**: "Your membership expired on 2025-05-29"
- **Membership Frozen**: "Your membership is frozen until 2025-06-30"
- **Session Limit Reached**: "You have reached your session limit for this month"
- **Active Session**: "You already have an active session"
- **Check-in Too Soon**: "You can only check in every 2 minutes"

---

#### Manual Check-in (Staff)

**Endpoint**: `POST /api/attendance/manual-checkin`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "memberId": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "reason": 1,
  "notes": "Member forgot QR card"
}
```

**Reason Codes**:
- `0`: Guest Check-in
- `1`: Forgot QR Code
- `2`: System Error
- `3`: Special Authorization
- `4`: Other

**Response** (200 OK):
```json
{
  "success": true,
  "message": "Ahmed Hassan checked in successfully",
  "attendanceId": "att-123456",
  "checkInTime": "2025-05-30T06:30:00Z",
  "memberName": "Ahmed Hassan"
}
```

---

#### Search Members for Check-in

**Endpoint**: `GET /api/attendance/search-members`

**Authorization**: AnyStaff

**Query Parameters**:
- `search` (string): Name or member number (min 2 chars)

**Request Example**:
```
GET /api/attendance/search-members?search=ahmed
```

**Response** (200 OK):
```json
[
  {
    "id": "8e4e7838-3715-48d2-842f-ee2df786669f",
    "memberNumber": "MEM-00001",
    "firstName": "Ahmed",
    "lastName": "Hassan",
    "status": "active",
    "selectable": true,
    "reason": null,
    "profileImageUrl": "https://..."
  },
  {
    "id": "member-id-2",
    "memberNumber": "MEM-00003",
    "firstName": "Ahmed",
    "lastName": "Ali",
    "status": "expired",
    "selectable": false,
    "reason": "Membership expired on 2025-05-29"
  }
]
```

---

#### Get Today's Attendance

**Endpoint**: `GET /api/attendance/today`

**Authorization**: AnyStaff

**Query Parameters**:
- `filter` (string): `all`, `qr`, or `manual` (default: `all`)

**Response** (200 OK):
```json
[
  {
    "id": "att-001",
    "memberNumber": "MEM-00001",
    "memberName": "Ahmed Hassan",
    "checkInTime": "2025-05-30T06:30:00Z",
    "checkOutTime": null,
    "duration": null,
    "entryMethod": "qr"
  },
  {
    "id": "att-002",
    "memberNumber": "MEM-00002",
    "memberName": "Sara Ahmed",
    "checkInTime": "2025-05-30T07:15:00Z",
    "checkOutTime": "2025-05-30T08:45:00Z",
    "duration": "01:30:00",
    "entryMethod": "manual"
  }
]
```

---

### Membership Plans

#### Get All Plans

**Endpoint**: `GET /api/membership-plans`

**Authorization**: AnyStaff

**Response** (200 OK):
```json
[
  {
    "id": "plan-001",
    "name": "Monthly Unlimited",
    "type": "monthly_unlimited",
    "price": 500.00,
    "durationDays": 30,
    "description": "Unlimited gym access for 30 days",
    "isActive": true,
    "memberCount": 45
  },
  {
    "id": "plan-002",
    "name": "10 Session Pack",
    "type": "session_pack",
    "price": 450.00,
    "sessionCount": 10,
    "description": "10 sessions (valid for 60 days)",
    "isActive": true,
    "memberCount": 23
  },
  {
    "id": "plan-003",
    "name": "Family Plan",
    "type": "family",
    "price": 1500.00,
    "durationDays": 30,
    "maxMembers": 4,
    "description": "Unlimited access for up to 4 family members",
    "isActive": true,
    "memberCount": 12
  }
]
```

---

#### Get Plan Details

**Endpoint**: `GET /api/membership-plans/{id}`

**Authorization**: AnyStaff

**Response** (200 OK):
```json
{
  "id": "plan-001",
  "name": "Monthly Unlimited",
  "type": "monthly_unlimited",
  "price": 500.00,
  "durationDays": 30,
  "description": "Unlimited gym access for 30 days",
  "isActive": true,
  "memberCount": 45,
  "createdAtUtc": "2025-01-01T00:00:00Z",
  "updatedAtUtc": "2025-05-30T00:00:00Z"
}
```

---

#### Create Plan

**Endpoint**: `POST /api/membership-plans`

**Authorization**: OwnerOnly

**Request Body**:
```json
{
  "name": "Premium 3-Month",
  "type": "monthly_unlimited",
  "price": 1350.00,
  "durationDays": 90,
  "description": "3 months of unlimited gym access at discounted rate"
}
```

**Plan Types Supported**:
- `monthly_unlimited`: Fixed duration unlimited access
- `session_pack`: Limited sessions (10, 20, or 50 only)
- `time_limited`: Restricted hours (requires timeRestrictionStart & timeRestrictionEnd)
- `pt_credits`: Personal training credits
- `family`: Multi-member family plan

**Response** (201 Created): Created plan object

---

#### Update Plan

**Endpoint**: `PUT /api/membership-plans/{id}`

**Authorization**: OwnerOnly

**Request Body** (partial update):
```json
{
  "price": 550.00,
  "description": "Updated description"
}
```

**Response** (200 OK): Updated plan object

---

#### Delete Plan

**Endpoint**: `DELETE /api/membership-plans/{id}`

**Authorization**: OwnerOnly

**Response** (200 OK):
```json
{
  "message": "Membership plan deleted successfully"
}
```

**Conflict Response** (409):
```json
{
  "error": "Cannot delete plan with active memberships",
  "activeMembershipCount": 45
}
```

---

### Memberships

#### Assign Membership to Member

**Endpoint**: `POST /api/memberships/assign`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "memberId": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "planId": "plan-001",
  "startDate": "2025-05-30",
  "paymentMethod": "cash",
  "notes": "Full payment received"
}
```

**Payment Methods**: `cash`, `card`, `transfer`, `check`

**Response** (201 Created):
```json
{
  "id": "mem-123456",
  "memberId": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "planId": "plan-001",
  "planName": "Monthly Unlimited",
  "status": "active",
  "startDate": "2025-05-30",
  "expiryDate": "2025-06-30",
  "price": 500.00,
  "paymentMethod": "cash",
  "createdAtUtc": "2025-05-30T10:00:00Z"
}
```

---

#### Renew Membership

**Endpoint**: `POST /api/memberships/{id}/renew`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "planId": "plan-001",
  "paymentMethod": "card",
  "notes": "Renewal with card payment"
}
```

**Response** (200 OK): New membership object with extended dates

---

#### Get Membership History

**Endpoint**: `GET /api/memberships/{memberId}/history`

**Authorization**: AnyStaff

**Query Parameters**:
- `page` (int): Page number
- `pageSize` (int): Items per page

**Response** (200 OK):
```json
{
  "data": [
    {
      "id": "mem-123456",
      "planName": "Monthly Unlimited",
      "status": "expired",
      "startDate": "2025-04-30",
      "expiryDate": "2025-05-30",
      "price": 500.00,
      "sessionsUsed": null,
      "paymentMethod": "cash"
    }
  ],
  "totalCount": 3
}
```

---

### Member Invitations

#### Send Invitation

**Endpoint**: `POST /api/invitations/send`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "email": "admin@gymflow.local",
  "firstName": "Karim",
  "lastName": "Ibrahim",
  "message": "Join our modern gym facility!"
}
```

**Response** (201 Created):
```json
{
  "id": "inv-123456",
  "email": "admin@gymflow.local",
  "firstName": "Karim",
  "lastName": "Ibrahim",
  "status": "sent",
  "sentAtUtc": "2025-05-30T10:00:00Z",
  "expiresAtUtc": "2025-06-30T10:00:00Z"
}
```

---

#### Get Invitation History

**Endpoint**: `GET /api/invitations/history`

**Authorization**: ManagerOrAbove

**Query Parameters**:
- `page` (int)
- `pageSize` (int)
- `status` (string): `sent`, `visited`, `converted`

**Response** (200 OK):
```json
[
  {
    "id": "inv-123456",
    "email": "admin@gymflow.local",
    "firstName": "Karim",
    "status": "converted",
    "sentAtUtc": "2025-05-20T10:00:00Z",
    "visitedAtUtc": "2025-05-21T15:30:00Z",
    "convertedAtUtc": "2025-05-25T08:00:00Z"
  }
]
```

---

### Analytics & Reports

#### Get Dashboard Overview

**Endpoint**: `GET /api/analytics/dashboard-overview`

**Authorization**: ManagerOrAbove

**Response** (200 OK):
```json
{
  "activeMembers": 145,
  "expiredMembers": 32,
  "newMembersThisMonth": 8,
  "revenueThisMonth": 45000.00,
  "checkinsToday": 52,
  "checkinsThisWeek": 280,
  "snapshotTimeUtc": "2025-05-30T10:00:00Z"
}
```

---

#### Get Revenue Chart

**Endpoint**: `GET /api/analytics/revenue-chart`

**Authorization**: ManagerOrAbove

**Query Parameters**:
- `months` (int): Number of months to include (default: 6)

**Response** (200 OK):
```json
{
  "labels": ["Nov", "Dec", "Jan", "Feb", "Mar", "Apr"],
  "values": [45000, 52000, 48500, 51000, 55000, 58000]
}
```

---

#### Get Attendance Heatmap

**Endpoint**: `GET /api/analytics/attendance-heatmap`

**Authorization**: ManagerOrAbove

**Response** (200 OK):
```json
{
  "data": [
    [0, 0, 0, 0, 0, 2, 15, 45, 52, 38, 25, 18, 12, 5, 0],
    [0, 0, 0, 0, 0, 3, 18, 48, 55, 42, 28, 20, 14, 6, 0],
    ...
  ]
}
```

**Note**: 7 rows (Mon-Sun), 24 columns (hours 0-23)

---

#### Get Member Status Breakdown

**Endpoint**: `GET /api/analytics/member-status`

**Authorization**: ManagerOrAbove

**Response** (200 OK):
```json
{
  "active": 145,
  "expired": 32,
  "frozen": 8,
  "cancelled": 5
}
```

---

#### Get Invitation Funnel

**Endpoint**: `GET /api/analytics/invitation-funnel`

**Authorization**: ManagerOrAbove

**Response** (200 OK):
```json
{
  "sent": 250,
  "visited": 180,
  "converted": 45,
  "conversionRate": 18.0
}
```

---

### Admin Settings

#### Get Tenant Settings

**Endpoint**: `GET /api/admin/tenant-settings`

**Authorization**: OwnerOnly

**Response** (200 OK):
```json
{
  "gymName": "FitZone Gym",
  "gymCode": "GYM-TEST-01",
  "timezone": "Africa/Cairo",
  "currency": "EGP",
  "theme": "dark",
  "maxMembersAllowed": 500,
  "currentMemberCount": 177,
  "logo": "https://...",
  "primaryColor": "#FF6B35"
}
```

---

#### Update Tenant Settings

**Endpoint**: `PUT /api/admin/tenant-settings`

**Authorization**: OwnerOnly

**Request Body** (partial update):
```json
{
  "gymName": "FitZone Gym Pro",
  "timezone": "Africa/Cairo",
  "primaryColor": "#FF6B35"
}
```

**Response** (200 OK): Updated settings object

---

#### Get Staff List

**Endpoint**: `GET /api/admin/staff`

**Authorization**: ManagerOrAbove

**Query Parameters**:
- `page` (int)
- `pageSize` (int)
- `status` (string): `active`, `inactive`

**Response** (200 OK):
```json
[
  {
    "id": "staff-001",
    "firstName": "John",
    "lastName": "Manager",
    "email": "john@gymflow.test",
    "role": "Manager",
    "status": "active",
    "joinDate": "2024-01-15"
  }
]
```

---

#### Create Staff Member

**Endpoint**: `POST /api/admin/staff`

**Authorization**: ManagerOrAbove

**Request Body**:
```json
{
  "firstName": "Lisa",
  "lastName": "Receptionist",
  "email": "lisa@gymflow.test",
  "role": "Receptionist",
  "phoneNumber": "+20123456789"
}
```

**Valid Roles**: `Receptionist`, `Manager`, `Admin`, `Owner`

**Response** (201 Created):
```json
{
  "id": "staff-new-001",
  "firstName": "Lisa",
  "lastName": "Receptionist",
  "email": "lisa@gymflow.test",
  "role": "Receptionist",
  "status": "active",
  "joinDate": "2025-05-30"
}
```

---

## Data Transfer Objects

### Member DTOs

#### MemberListItemDto
```json
{
  "id": "8e4e7838-3715-48d2-842f-ee2df786669f",
  "memberNumber": "MEM-00001",
  "firstName": "Ahmed",
  "lastName": "Hassan",
  "phoneNumber": "+20123456789",
  "email": "admin@gymflow.local",
  "status": "active",
  "membershipId": "12345678-1234-1234-1234-123456789012",
  "planName": "Monthly Unlimited",
  "expiryDate": "2025-06-30",
  "profileImageUrl": "https://..."
}
```

#### MemberDetailDto
Extends MemberListItemDto with:
```json
{
  "dateOfBirth": "1990-05-15",
  "gender": "Male",
  "joinDate": "2024-01-15",
  "isActive": true,
  "emergencyContact": "Fatima Hassan",
  "emergencyPhone": "+20198765432",
  "currentMembership": { ... },
  "recentAttendance": [ ... ]
}
```

### Plan DTOs

#### PlanListItemDto
```json
{
  "id": "plan-001",
  "name": "Monthly Unlimited",
  "type": "monthly_unlimited",
  "price": 500.00,
  "durationDays": 30,
  "description": "Unlimited gym access for 30 days",
  "isActive": true,
  "memberCount": 45
}
```

#### PlanDetailDto
Extends PlanListItemDto with:
```json
{
  "createdAtUtc": "2025-01-01T00:00:00Z",
  "updatedAtUtc": "2025-05-30T00:00:00Z"
}
```

### Attendance DTOs

#### TodayAttendanceDto
```json
{
  "id": "att-001",
  "memberNumber": "MEM-00001",
  "memberName": "Ahmed Hassan",
  "checkInTime": "2025-05-30T06:30:00Z",
  "checkOutTime": null,
  "duration": null,
  "entryMethod": "qr"
}
```

#### QrCheckinResponse
```json
{
  "success": true,
  "message": "Welcome Ahmed! Check-in successful.",
  "attendanceId": "att-123456",
  "checkInTime": "2025-05-30T06:30:00Z",
  "remainingTime": "24:30:00",
  "memberName": "Ahmed Hassan",
  "planType": "Monthly Unlimited"
}
```

---

## Common Patterns

### Pagination

List endpoints return paginated responses:

```json
{
  "data": [ ... ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 150
}
```

### Date Formats

- **Date Only**: `YYYY-MM-DD` (e.g., `2025-05-30`)
- **DateTime**: ISO 8601 UTC (e.g., `2025-05-30T10:00:00Z`)
- **Time Only**: `HH:MM:SS` (e.g., `06:30:00`)
- **Duration**: `HH:MM:SS` (e.g., `01:30:00`)

### Rate Limiting

- **Check-in endpoint**: 5 requests per 5 minutes per user
- **Other endpoints**: 100 requests per minute

### Status Codes Summary

| Code | Meaning |
|------|---------|
| 200 | OK |
| 201 | Created |
| 400 | Bad Request |
| 401 | Unauthorized |
| 403 | Forbidden |
| 404 | Not Found |
| 409 | Conflict |
| 429 | Too Many Requests |
| 500 | Server Error |

---

## Flutter Integration Example

```dart
// Example Flutter code for login
Future<void> login(String email, String password) async {
  try {
    final response = await http.post(
      Uri.parse('http://localhost:5000/api/auth/login'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'email': email,
        'password': password,
      }),
    );

    if (response.statusCode == 200) {
      final data = jsonDecode(response.body);
      // Store tokens securely
      await storage.write(key: 'access_token', value: data['accessToken']);
      await storage.write(key: 'refresh_token', value: data['refreshToken']);
    } else {
      throw Exception('Login failed');
    }
  } catch (e) {
    print('Error: $e');
  }
}

// Example: QR Check-in
Future<void> qrCheckin(String qrToken) async {
  final token = await storage.read(key: 'access_token');
  
  final response = await http.post(
    Uri.parse('http://localhost:5000/api/attendance/qr-checkin'),
    headers: {
      'Content-Type': 'application/json',
      'Authorization': 'Bearer $token',
    },
    body: jsonEncode({'qrToken': qrToken}),
  );

  if (response.statusCode == 200) {
    final data = jsonDecode(response.body);
    print('Check-in successful: ${data['message']}');
  } else {
    final error = jsonDecode(response.body);
    print('Check-in failed: ${error['error']}');
  }
}
```

---

## Support

For issues or questions, contact the development team at: `dev@gymflowpro.test`

Last Updated: 2025-05-30

