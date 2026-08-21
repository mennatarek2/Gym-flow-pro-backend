# 🏋️ GymFlowPro API Documentation

**Version:** v1  
**API Base URL:** `https://localhost:5001/api`  
**.NET Version:** .NET 8  
**Last Updated:** 2026

---

## 📑 Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Authentication](#authentication)
4. [API Endpoints](#api-endpoints)
   - [Auth](#auth-endpoints)
   - [Attendance](#attendance-endpoints)
   - [Invitations](#invitation-endpoints)
   - [Payments](#payment-endpoints)
   - [Health Check](#health-check)
5. [Error Handling](#error-handling)
6. [Rate Limiting](#rate-limiting)
7. [DTOs Reference](#dtos-reference)

---

## Overview

**GymFlowPro** is a comprehensive gym management system API built with **ASP.NET Core 8**. It provides:

- ✅ **Authentication** - Staff JWT & Member OTP flows
- ✅ **Attendance Tracking** - QR check-in & manual check-in with validation
- ✅ **Member Management** - Membership lifecycle & quotas
- ✅ **Invitations** - Guest invitations with monthly quotas
- ✅ **Payment Integration** - Paymob & Fawry webhooks
- ✅ **Multi-tenancy** - Isolated tenant data
- ✅ **Real-time Notifications** - SignalR for check-in notifications

---

## Getting Started

### Prerequisites

- **.NET 8 SDK** installed
- **SQL Server** (LocalDB or full instance)
- **Visual Studio 2026** (optional, but recommended)

### Installation

1. **Clone/Open the project:**
   ```powershell
   cd D:\GMS\GMS
   ```

2. **Restore NuGet packages:**
   ```powershell
   dotnet restore
   ```

3. **Update database:**
   ```powershell
   dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
   ```

4. **Run the API:**
   ```powershell
   dotnet run --project GMS.Api
   ```

5. **Access Swagger UI:**
   ```
   https://localhost:5001/swagger/ui
   ```

---

## Authentication

### Two Authentication Flows

#### **1. Staff Authentication (JWT)**

**Flow:** Email → Password → Access Token (15 min) + Refresh Token (30 days)

```
POST /api/auth/login
Authorization: None (public endpoint)
Body: { "email": "staff@gym.com", "password": "YOUR_PASSWORD" }
Response: { "accessToken": "eyJh...", "refreshToken": "abc123...", "expiresIn": 900 }
```

**Subsequent requests:**
```
GET /api/attendance/search-members
Authorization: Bearer YOUR_ACCESS_TOKEN
```

#### **2. Member Authentication (OTP)**

**Step 1 - Request OTP:**
```
POST /api/auth/member-otp
Body: { "phoneNumber": "+201234567890" }
Response: { "message": "OTP sent" }
```

**Step 2 - Verify OTP:**
```
POST /api/auth/member-verify
Body: { "phoneNumber": "+201234567890", "otp": "123456" }
Response: { "accessToken": "eyJh...", "refreshToken": "abc123...", "expiresIn": 3600 }
```

### Authorization Policies

| Policy | Required Role | Endpoints |
|--------|---|---|
| `AuthenticatedMember` | Member | QR check-in, View history, Send invitations |
| `AnyStaff` | Staff/Manager/Admin | Member search, Reports |
| `ManagerOrAbove` | Manager/Admin | Manual check-in, Approve refunds |
| `AdminOnly` | Admin | System configuration, Staff management |

---

## API Endpoints

### 🔐 Auth Endpoints

#### `POST /api/auth/login`
**Staff Login**

```http
POST /api/auth/login HTTP/1.1
Authorization: None
Content-Type: application/json

{
  "email": "manager@gym.com",
  "password": "YOUR_PASSWORD"
}
```

**Response 200 OK:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "e7d3f8a2-9c41-4f2b-8e15-3a5c6d7e8f9a",
  "expiresIn": 900,
  "tokenType": "Bearer"
}
```

**Response 401 Unauthorized:**
```json
{
  "error": "Invalid email or password"
}
```

---

#### `POST /api/auth/refresh`
**Refresh Access Token**

```http
POST /api/auth/refresh HTTP/1.1
Authorization: None
Content-Type: application/json

{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "e7d3f8a2-9c41-4f2b-8e15-3a5c6d7e8f9a"
}
```

**Response 200 OK:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "f8e4g9b3-0d52-5g3c-9f26-4b6d7e8f9g0b",
  "expiresIn": 900
}
```

---

#### `POST /api/auth/member-otp`
**Send OTP to Member**

```http
POST /api/auth/member-otp HTTP/1.1
Authorization: None
Content-Type: application/json

{
  "phoneNumber": "+201234567890"
}
```

**Response 200 OK:**
```json
{
  "message": "OTP sent successfully. Valid for 5 minutes.",
  "maskedPhone": "+2012345****"
}
```

**Response 400 Bad Request:**
```json
{
  "error": "Phone number not found in system"
}
```

---

#### `POST /api/auth/member-verify`
**Verify OTP & Get Member Token**

```http
POST /api/auth/member-verify HTTP/1.1
Authorization: None
Content-Type: application/json

{
  "phoneNumber": "+201234567890",
  "otp": "123456"
}
```

**Response 200 OK:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "e7d3f8a2-9c41-4f2b-8e15-3a5c6d7e8f9a",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

---

### 📍 Attendance Endpoints

#### `POST /api/attendance/qr-checkin`
**QR Code Check-in (Member)**

```http
POST /api/attendance/qr-checkin HTTP/1.1
Authorization: Bearer YOUR_ACCESS_TOKEN
Content-Type: application/json

{
  "qrCodeData": "gym-2024-001"
}
```

**Response 200 OK:**
```json
{
  "success": true,
  "checkInTime": "2026-05-02T14:30:00Z",
  "memberName": "Ahmed Mohamed",
  "membershipType": "Premium",
  "membershipExpiryDate": "2026-06-02",
  "message": "Welcome! Check-in successful."
}
```

**Response 400 Bad Request (Examples):**
```json
{
  "error": "Membership expired"
}
```

```json
{
  "error": "Membership is frozen until 2026-05-10"
}
```

```json
{
  "error": "Session limit reached for today"
}
```

**Rate Limit:** 10 check-ins per minute per member

---

#### `POST /api/attendance/manual-checkin`
**Manual Check-in by Staff**

```http
POST /api/attendance/manual-checkin HTTP/1.1
Authorization: Bearer YOUR_ACCESS_TOKEN
Content-Type: application/json

{
  "memberPhoneNumber": "+201234567890",
  "notes": "Guest check-in"
}
```

**Response 200 OK:**
```json
{
  "attendanceId": "550e8400-e29b-41d4-a716-446655440000",
  "memberId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "memberName": "Ahmed Mohamed",
  "checkInTime": "2026-05-02T14:35:00Z",
  "entryMethod": "manual",
  "staffName": "Manager Ali"
}
```

**Required Policy:** `ManagerOrAbove`

---

#### `GET /api/attendance/search-members`
**Search Members for Manual Check-in**

```http
GET /api/attendance/search-members?searchTerm=ahmed&limit=10 HTTP/1.1
Authorization: Bearer YOUR_ACCESS_TOKEN
```

**Response 200 OK:**
```json
{
  "results": [
    {
      "memberId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
      "name": "Ahmed Mohamed",
      "phone": "+201234567890",
      "membershipStatus": "Active",
      "expiryDate": "2026-06-02",
      "isSelectable": true,
      "reason": null
    },
    {
      "memberId": "a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6",
      "name": "Ahmed Hassan",
      "phone": "+201987654321",
      "membershipStatus": "Expired",
      "expiryDate": "2026-04-01",
      "isSelectable": false,
      "reason": "Membership expired"
    }
  ],
  "total": 2
}
```

---

### 🎟️ Invitation Endpoints

#### `POST /api/invitation/send`
**Send Guest Invitation (Monthly Quota: 3)**

```http
POST /api/invitation/send HTTP/1.1
Authorization: Bearer YOUR_ACCESS_TOKEN
Content-Type: application/json

{
  "guestName": "Fatima Ali",
  "guestPhone": "+201098765432"
}
```

**Response 200 OK:**
```json
{
  "invitationId": "550e8400-e29b-41d4-a716-446655440000",
  "guestName": "Fatima Ali",
  "guestPhone": "+201098765432",
  "sentAt": "2026-05-02T14:40:00Z",
  "expiresAt": "2026-05-09T14:40:00Z",
  "remainingQuota": 2
}
```

**Response 400 Bad Request:**
```json
{
  "error": "Monthly invitation quota exceeded (3/3)"
}
```

---

#### `GET /api/invitation/history`
**Get Member's Invitation History**

```http
GET /api/invitation/history HTTP/1.1
Authorization: Bearer YOUR_ACCESS_TOKEN
```

**Response 200 OK:**
```json
{
  "invitations": [
    {
      "invitationId": "550e8400-e29b-41d4-a716-446655440000",
      "guestName": "Fatima Ali",
      "guestPhone": "+201098765432",
      "sentAt": "2026-05-02T14:40:00Z",
      "expiresAt": "2026-05-09T14:40:00Z",
      "status": "Active"
    },
    {
      "invitationId": "660e8400-e29b-41d4-a716-446655440001",
      "guestName": "Sarah Mohamed",
      "guestPhone": "+201567890123",
      "sentAt": "2026-04-25T10:30:00Z",
      "expiresAt": "2026-05-02T10:30:00Z",
      "status": "Expired"
    }
  ],
  "totalCount": 2
}
```

---

### 💳 Payment Endpoints

#### `POST /api/payments/paymob-webhook`
**Paymob Payment Webhook**

```http
POST /api/payments/paymob-webhook HTTP/1.1
Content-Type: application/json
X-Hmac: sha512_hmac_signature_here

{
  "type": "transaction.approved",
  "obj": {
    "id": 1234567890,
    "success": true,
    "amount_cents": 50000,
    "order": {
      "merchant_order_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479|a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6"
    }
  }
}
```

**Response 200 OK:**
```json
{
  "status": "processed",
  "message": "Membership created successfully"
}
```

**Response 401 Unauthorized:**
```json
{
  "error": "Invalid HMAC signature"
}
```

---

#### `POST /api/payments/fawry-webhook`
**Fawry Payment Webhook**

```http
POST /api/payments/fawry-webhook HTTP/1.1
Content-Type: application/json
X-Fawry-Signature: sha256_signature_here

{
  "orderStatus": "PAID",
  "fawryRefNumber": "123456789",
  "paymentAmount": 500.00,
  "merchantRefNum": "f47ac10b-58cc-4372-a567-0e02b2c3d479|a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6"
}
```

**Response 200 OK:**
```json
{
  "status": "processed",
  "message": "Membership created successfully"
}
```

---

### 🏥 Health Check

#### `GET /api/health`
**API Health Status**

```http
GET /api/health HTTP/1.1
```

**Response 200 OK:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-05-02T14:45:00Z"
}
```

---

## Error Handling

### Standard Error Response

```json
{
  "error": "Error message",
  "statusCode": 400,
  "timestamp": "2026-05-02T14:45:00Z",
  "traceId": "0HN1GBDV5RSQR:00000001"
}
```

### Common HTTP Status Codes

| Code | Meaning | Example |
|------|---------|---------|
| `200 OK` | Success | Successful check-in |
| `400 Bad Request` | Invalid input | Missing required field |
| `401 Unauthorized` | Auth failed or missing | Expired token, invalid OTP |
| `403 Forbidden` | Insufficient permissions | Staff-only endpoint, wrong policy |
| `404 Not Found` | Resource not found | Member doesn't exist |
| `429 Too Many Requests` | Rate limit exceeded | Too many check-in attempts |
| `500 Server Error` | Internal server error | Database error, etc. |

---

## Rate Limiting

### Check-in Rate Limit
- **Limit:** 10 check-ins per minute per member
- **Headers:**
  ```
  X-RateLimit-Limit: 10
  X-RateLimit-Remaining: 8
  X-RateLimit-Reset: 1714746360
  ```
- **Response when exceeded (429):**
  ```json
  {
    "error": "Too many check-in attempts. Please try again in 30 seconds.",
    "retryAfter": 30
  }
  ```

---

## DTOs Reference

### Auth DTOs

#### `LoginRequest`
```csharp
{
  "email": "string (required)",
  "password": "YOUR_PASSWORD"
}
```

#### `LoginResponse`
```csharp
{
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": "int (seconds)",
  "tokenType": "Bearer"
}
```

#### `MemberOtpRequest`
```csharp
{
  "phoneNumber": "string (required, format: +20XXXXXXXXXXX)"
}
```

#### `MemberOtpVerifyRequest`
```csharp
{
  "phoneNumber": "string (required)",
  "otp": "string (required, length: 6)"
}
```

---

### Attendance DTOs

#### `QrCheckinRequest`
```csharp
{
  "qrCodeData": "string (required)"
}
```

#### `QrCheckinResponse`
```csharp
{
  "success": "bool",
  "checkInTime": "DateTime (ISO 8601)",
  "memberName": "string",
  "membershipType": "string",
  "membershipExpiryDate": "DateTime",
  "message": "string"
}
```

#### `ManualCheckinRequest`
```csharp
{
  "memberPhoneNumber": "string (required)",
  "notes": "string (optional)"
}
```

#### `MemberSearchRequest`
```csharp
{
  "searchTerm": "string (phone or name)",
  "limit": "int (default: 10)"
}
```

---

### Invitation DTOs

#### `SendInvitationRequest`
```csharp
{
  "guestName": "string (required)",
  "guestPhone": "string (required, format: +20XXXXXXXXXXX)"
}
```

#### `SendInvitationResponse`
```csharp
{
  "invitationId": "Guid",
  "guestName": "string",
  "guestPhone": "string",
  "sentAt": "DateTime",
  "expiresAt": "DateTime",
  "remainingQuota": "int"
}
```

#### `InvitationHistoryResponse`
```csharp
{
  "invitationId": "Guid",
  "guestName": "string",
  "guestPhone": "string",
  "sentAt": "DateTime",
  "expiresAt": "DateTime",
  "status": "Active|Expired|Used"
}
```

---

## Integration Examples

### cURL - Staff Login

```bash
curl -X POST "https://localhost:5001/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "manager@gym.com",
    "password": "YOUR_PASSWORD"
  }'
```

### cURL - QR Check-in

```bash
curl -X POST "https://localhost:5001/api/attendance/qr-checkin" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "qrCodeData": "gym-2024-001"
  }'
```

### JavaScript/Fetch - Member OTP

```javascript
const response = await fetch('https://localhost:5001/api/auth/member-otp', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    phoneNumber: '+201234567890'
  })
});

const data = await response.json();
console.log(data);
```

---

## SignalR Real-time Events

### Connection
```
Hub URL: wss://localhost:5001/hubs/attendance
```

### Events
- **`checkin.received`** - Real-time check-in notification
  ```json
  {
    "memberId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "memberName": "Ahmed Mohamed",
    "checkInTime": "2026-05-02T14:50:00Z",
    "membershipType": "Premium"
  }
  ```

---

## Support & Resources

- **API Documentation:** https://localhost:5001/swagger/ui
- **GitHub Repository:** [Your Repo URL]
- **Issues/Bug Reports:** [Your Issue Tracker]
- **Contact Support:** support@gymflowpro.com

---

**Last Updated:** May 2, 2026  
**API Version:** v1.0.0  
**Status:** ✅ Production Ready
