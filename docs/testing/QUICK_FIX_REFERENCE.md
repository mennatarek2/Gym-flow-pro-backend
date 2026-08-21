# ⚡ QUICK FIX REFERENCE

## Your Error
```
400 Bad Request
"Unauthorized: Staff user not found or does not belong to this tenant"
```

## Root Cause
Token had old user ID from previous database state

## Solution
✅ **Fixed in**: `GMS.Infrastructure/Persistence/DataSeeder.cs`
- Changed from random to fixed user IDs
- ✅ Build: Successful

---

## 3-Step Fix

### Step 1: Reset Database
```powershell
# Package Manager Console:
Update-Database -Context GymFlowProDbContext -Force
```

### Step 2: Get Fresh Token
```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"owner@gymflow.test","password": "YOUR_PASSWORD"}'

# Copy the "accessToken" value
```

### Step 3: Test Manual Check-in
```bash
curl -X POST http://localhost:5000/api/attendance/manual-checkin \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "memberId": "8e4e7838-3715-48d2-842f-ee2df786669f",
    "reason": 1,
    "notes": "Forgot QR code"
  }'
```

**Expected**: 200 OK ✅

---

## Fixed User IDs

```
Owner:   owner@gymflow.test / Test@1234
         ID: f8677f57-8b74-46a1-9698-08deadd7eaba

Manager: manager@gymflow.test / Test@1234
         ID: 12345678-1234-1234-1234-123456789012

Trainer: trainer@gymflow.test / Test@1234
         ID: 87654321-4321-4321-4321-210987654321
```

These IDs are now **permanent** across all database resets.

---

## Verify It Works

After step 3 above, you should see:

```json
{
  "success": true,
  "attendanceId": "att-xxxxx",
  "memberName": "Ahmed Hassan",
  "checkInAtUtc": "2025-05-30T...",
  "message": "Manual check-in successful"
}
```

---

## If Still Getting Error

1. ❌ Are you using old token from before reset?
   → Get fresh token with login (Step 2 above)

2. ❌ Did you copy the full token?
   → Make sure no extra spaces or characters

3. ❌ Is member ID valid?
   → Verify member exists in database

4. ❌ Is token expired?
   → Tokens last 15 minutes - get fresh one

---

## Files Changed
- `GMS.Infrastructure/Persistence/DataSeeder.cs` ← Only file modified

## Build Status
✅ **Successful** - Ready to test

## Documentation
See `COMPLETE_FIX_SUMMARY.md` for full details

---

**That's it!** 🚀 You're ready to go.

