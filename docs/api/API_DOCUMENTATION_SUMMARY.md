# Complete API Documentation Summary

## 📋 Overview

This package contains comprehensive API documentation and integration guides for the **GymFlowPro** system for frontend and Flutter developers.

### Documents Included:

1. **API_DOCUMENTATION_FRONTEND.md** - Complete REST API reference
2. **FLUTTER_INTEGRATION_GUIDE.md** - Flutter app integration examples
3. **FIX_MANUAL_CHECKIN_FK_ERROR.md** - Critical bug fix documentation

---

## 🚀 Quick Start

### For Web Frontend Developers

1. **Start here**: `API_DOCUMENTATION_FRONTEND.md`
   - Base URL: `http://localhost:5000`
   - Authentication: JWT Bearer tokens
   - Main endpoints: Members, Attendance, Plans, Memberships, Analytics

2. **Key endpoints**:
   - `POST /api/auth/login` - Staff authentication
   - `POST /api/auth/member-otp` / `member-verify` - Member authentication
   - `POST /api/attendance/qr-checkin` - Member check-in
   - `POST /api/attendance/manual-checkin` - Staff check-in
   - `GET /api/members` - List members
   - `GET /api/analytics/*` - Dashboard data

### For Flutter Developers

1. **Start here**: `FLUTTER_INTEGRATION_GUIDE.md`
2. **Key implementations**:
   - Auth service with OTP flow
   - QR scanner integration
   - Member profile screen
   - Attendance history
   - Error handling

### Critical Bug Fix

**If you encounter**: `Foreign Key constraint error on manual-checkin`
- **Read**: `FIX_MANUAL_CHECKIN_FK_ERROR.md`
- **Problem**: StaffUserId validation missing
- **Solution**: Add staff user validation before creating attendance record

---

## 📊 API Architecture

### Authentication Types

| Type | Use Case | Flow |
|------|----------|------|
| Staff Login | Managers, Admins | Email + Password → JWT |
| Member OTP | App users | Phone → OTP → JWT |
| Token Refresh | Token expiration | Refresh token → New JWT pair |

### Authorization Policies

```
OwnerOnly (Owner)
├─ ManagerOrAbove (Manager, Admin, Owner)
│  ├─ AnyStaff (Receptionist, Manager, Admin, Owner)
│  └─ AuthenticatedMember (Members via app)
```

### Core Resources

| Resource | Endpoints | Operations |
|----------|-----------|-----------|
| **Members** | `/api/members` | GET, POST, PUT, DELETE |
| **Attendance** | `/api/attendance` | QR check-in, Manual check-in, Today's log |
| **Plans** | `/api/membership-plans` | CRUD |
| **Memberships** | `/api/memberships` | Assign, Renew, History |
| **Invitations** | `/api/invitations` | Send, History |
| **Analytics** | `/api/analytics` | Dashboard, Charts, Heatmaps |
| **Admin** | `/api/admin` | Settings, Staff, Quotas |

---

## 🔐 Security Considerations

### Token Management

```javascript
// Access Token: 15 minutes validity
"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

// Refresh Token: 30 days validity
// Store securely on client (encrypted storage for mobile)
```

### Multi-Tenancy

All queries auto-filtered by `TenantId` via:
- Global EF Core filter in DbContext
- JWT `tenant_id` claim verification
- Database constraints

### Rate Limiting

- **Check-in**: 5 requests / 5 minutes per user
- **Other endpoints**: 100 requests / 1 minute

---

## 📱 Member App Flow

```
1. Launch App
   ↓
2. Authentication
   ├─ No token → OTP login screen
   └─ Valid token → Home screen
   ↓
3. Home Screen
   ├─ Show QR Scanner button
   ├─ Show Member Profile
   └─ Show Attendance History
   ↓
4. QR Check-in
   ├─ Scan gym's static QR
   ├─ Validate membership
   └─ Show success/error
   ↓
5. Profile
   ├─ Current membership details
   ├─ Days remaining
   └─ Attendance stats
```

---

## 🎯 Admin Dashboard Flow

```
1. Login (Email + Password)
   ↓
2. Dashboard
   ├─ Active members count
   ├─ Revenue this month
   ├─ Today's check-ins
   └─ Member status breakdown
   ↓
3. Members Management
   ├─ List/Search
   ├─ Create new
   ├─ Edit profile
   └─ Manage memberships
   ↓
4. Check-in Management
   ├─ Manual check-in
   ├─ Today's attendance log
   └─ Search members
   ↓
5. Plans & Memberships
   ├─ Manage plans
   ├─ Assign memberships
   ├─ Renew memberships
   └─ View history
   ↓
6. Analytics
   ├─ Revenue charts
   ├─ Attendance heatmap
   ├─ Member retention
   └─ Invitation funnel
```

---

## 📊 Data Models

### Member
```json
{
  "id": "guid",
  "memberNumber": "MEM-00001",
  "firstName": "Ahmed",
  "lastName": "Hassan",
  "phoneNumber": "+20123456789",
  "email": "admin@gymflow.local",
  "status": "active|expired|frozen",
  "joinDate": "2024-01-15",
  "currentMembership": { ... }
}
```

### Membership
```json
{
  "id": "guid",
  "planId": "guid",
  "planName": "Monthly Unlimited",
  "status": "active|expired|frozen",
  "startDate": "2025-05-30",
  "expiryDate": "2025-06-30",
  "price": 500.00
}
```

### Plan
```json
{
  "id": "guid",
  "name": "Monthly Unlimited",
  "type": "monthly_unlimited|session_pack|family|pt_credits",
  "price": 500.00,
  "durationDays": 30
}
```

### Attendance
```json
{
  "id": "guid",
  "memberId": "guid",
  "checkInTime": "2025-05-30T06:30:00Z",
  "checkOutTime": "2025-05-30T08:00:00Z",
  "entryMethod": "qr|manual",
  "staffUserId": "guid" // for manual entries
}
```

---

## 🐛 Common Issues & Solutions

### Issue 1: "Invalid gym QR code"
**Cause**: QR token not matching gym code  
**Fix**: Verify `qrToken` parameter in QR code matches backend

### Issue 2: "Membership expired"
**Cause**: Member's membership end date has passed  
**Fix**: Renew membership via `/api/memberships/{id}/renew`

### Issue 3: "Foreign key constraint FK_gym_attendance_app_users_StaffUserId"
**Cause**: StaffUserId doesn't exist in app_users table  
**Fix**: See `FIX_MANUAL_CHECKIN_FK_ERROR.md`

### Issue 4: "Too many requests"
**Cause**: Rate limit exceeded  
**Fix**: Wait 5 minutes before retrying check-in

### Issue 5: "Unauthorized: Invalid token"
**Cause**: Token expired or invalid  
**Fix**: Call `/api/auth/refresh` with refresh token

---

## 📖 API Response Format

### Success Response (200/201)
```json
{
  "id": "123456",
  "name": "Ahmed Hassan",
  "email": "admin@gymflow.local"
}
```

### Paginated Response
```json
{
  "data": [ ... ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 150
}
```

### Error Response
```json
{
  "error": "Field validation failed",
  "message": "Email is required",
  "details": {
    "email": ["Email is required"]
  }
}
```

---

## 🔄 State Management (Flutter)

### Using Provider

```dart
// Define providers
final authServiceProvider = Provider((ref) => AuthService());
final memberServiceProvider = Provider((ref) => MemberService());

// Consume in widgets
final currentMember = ref.watch(memberProfileProvider);
```

### Using GetX

```dart
class AuthController extends GetxController {
  final isLoggedIn = false.obs;
  final user = Rx<MemberProfile?>(null);
  
  void login(String phone, String otp) {
    // Implementation
  }
}
```

---

## 🧪 Testing Checklist

### Authentication
- [ ] Member OTP login works
- [ ] Staff login works
- [ ] Token refresh works
- [ ] Logout clears tokens
- [ ] Invalid credentials return 401

### Attendance
- [ ] QR check-in succeeds with valid membership
- [ ] QR check-in fails with expired membership
- [ ] Manual check-in requires ManagerOrAbove
- [ ] Check-in rate limit (5/5 min) enforced
- [ ] Search members returns correct results

### Members
- [ ] Create member succeeds
- [ ] Update member succeeds
- [ ] Delete member soft-deletes (IsActive=false)
- [ ] List members paginated
- [ ] Search members by name/number

### Plans
- [ ] Create plan with type validation
- [ ] Update plan price
- [ ] Cannot delete plan with active memberships
- [ ] Session pack limits (10, 20, 50)

### Memberships
- [ ] Assign membership to member
- [ ] Renew membership extends end date
- [ ] History shows all past memberships
- [ ] Freeze membership works

---

## 📞 Support & Contact

### For API Issues
- Check `API_DOCUMENTATION_FRONTEND.md` for endpoint details
- Review error response for specific error message
- Check `FIX_MANUAL_CHECKIN_FK_ERROR.md` if FK error occurs

### For Flutter Integration
- See `FLUTTER_INTEGRATION_GUIDE.md` for code examples
- Check sample implementations for state management
- Use provided error handler for consistent UX

### For Database/Backend Issues
- Enable debug logging in appsettings.Development.json
- Check SQL query in logged exception
- Verify tenant filtering is applied

---

## 📚 File Structure

```
/
├─ API_DOCUMENTATION_FRONTEND.md      ← Main API reference
├─ FLUTTER_INTEGRATION_GUIDE.md        ← Mobile app guide
├─ FIX_MANUAL_CHECKIN_FK_ERROR.md      ← Bug fix guide
└─ API_DOCUMENTATION_SUMMARY.md        ← This file
```

---

## 🎓 Key Concepts

### Tenant Isolation
- All data filtered by `TenantId`
- Gym code identifies tenant
- JWT includes tenant_id claim
- Staff can only access their gym's data

### Multi-Entry Methods
- **QR Check-in**: Member scans, system validates
- **Manual Check-in**: Staff enters, reasons tracked
- **Guest Check-in**: Staff can add guests

### Membership Lifecycle
1. **Active**: Valid, member can check in
2. **Frozen**: Valid but not checkable, extended end date
3. **Expired**: Cannot check in, needs renewal
4. **Cancelled**: Soft-deleted

### Session Management
- **Monthly**: Unlimited sessions
- **Session Pack**: Counted sessions (10/20/50)
- **PT Credits**: Personal training credits
- **Time Limited**: Restricted hours
- **Family**: Shared across members

---

## 🔄 Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-05-30 | Initial API documentation release |

---

## 📝 Notes for Developers

1. **Always include tenant_id in JWT claims** - Required for multi-tenancy
2. **Use ISO 8601 for dates** - `2025-05-30T10:00:00Z`
3. **Handle rate limits gracefully** - Show user-friendly message
4. **Validate membership before check-in** - Don't trust client state
5. **Cache access tokens securely** - Use platform-specific secure storage
6. **Log all API errors** - Help with debugging user issues
7. **Implement token refresh silently** - Don't require re-login
8. **Test cross-tenant scenarios** - Ensure isolation works

---

**Last Updated**: 2025-05-30  
**Status**: Production Ready  
**Maintainer**: Development Team

