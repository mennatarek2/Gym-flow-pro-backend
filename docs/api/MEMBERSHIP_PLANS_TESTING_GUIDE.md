# 🧪 MembershipPlans API Testing Guide

## Quick Test Commands

### **1. Get All Plans** (Staff Access)
```bash
# Using curl with Manager JWT
curl -X GET "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure

# Expected: 200 OK
# Response: [{ id, name, nameAr, planType, price, ... }]
```

---

### **2. Get Plan Details** (Staff Access)
```bash
# Replace {plan_id} with actual plan GUID
curl -X GET "https://localhost:5001/api/membership-plans/550e8400-e29b-41d4-a716-446655440000" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure

# Expected: 200 OK
# Response: Full plan details with membership counts
```

---

### **3. Create Plan - Monthly Unlimited** (Owner Only)
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Monthly Unlimited",
    "nameAr": "غير محدود شهري",
    "description": "Unlimited gym access for 30 days",
    "descriptionAr": "وصول غير محدود للصالة لمدة 30 يوم",
    "planType": "monthly_unlimited",
    "price": 299.00,
    "durationDays": 30,
    "sessionCount": null,
    "timeRestrictionStart": null,
    "timeRestrictionEnd": null,
    "invitationQuota": 0
  }' \
  --insecure

# Expected: 201 Created
# Response: Full plan details with ID
```

---

### **4. Create Plan - Session Pack (10 Sessions)** (Owner Only)
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Session Pack 10",
    "nameAr": "باقة 10 جلسات",
    "description": "10 fitness sessions valid for 60 days",
    "descriptionAr": "10 جلسات تدريب صحيحة لمدة 60 يوم",
    "planType": "session_pack",
    "price": 450.00,
    "durationDays": 60,
    "sessionCount": 10,
    "timeRestrictionStart": null,
    "timeRestrictionEnd": null,
    "invitationQuota": 0
  }' \
  --insecure

# Expected: 201 Created
# Validation: SessionCount must be 10, 20, or 50
```

---

### **5. Create Plan - Time Limited (Morning Pass)** (Owner Only)
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Morning Pass",
    "nameAr": "تذكرة الصباح",
    "description": "Access 6AM-12PM daily",
    "descriptionAr": "وصول من 6 صباحاً إلى 12 ظهراً يومياً",
    "planType": "time_limited",
    "price": 199.00,
    "durationDays": 30,
    "sessionCount": null,
    "timeRestrictionStart": "06:00",
    "timeRestrictionEnd": "12:00",
    "invitationQuota": 0
  }' \
  --insecure

# Expected: 201 Created
# Validation: Both timeRestrictionStart and timeRestrictionEnd required
# Validation: End time must be after start time
```

---

### **6. Create Plan - Personal Training Credits** (Owner Only)
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "PT Credits 10",
    "nameAr": "10 نقاط تدريب شخصي",
    "description": "10 personal training credits",
    "descriptionAr": "10 نقاط تدريب شخصي",
    "planType": "pt_credits",
    "price": 1200.00,
    "durationDays": 90,
    "sessionCount": 10,
    "timeRestrictionStart": null,
    "timeRestrictionEnd": null,
    "invitationQuota": 0
  }' \
  --insecure

# Expected: 201 Created
```

---

### **7. Create Plan - Family Plan** (Owner Only)
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Family Plan",
    "nameAr": "خطة العائلة",
    "description": "Family membership for up to 4 people",
    "descriptionAr": "عضوية عائلية لحد 4 أشخاص",
    "planType": "family",
    "price": 899.00,
    "durationDays": 30,
    "sessionCount": null,
    "timeRestrictionStart": null,
    "timeRestrictionEnd": null,
    "invitationQuota": 3
  }' \
  --insecure

# Expected: 201 Created
# Note: InvitationQuota specifies number of family members
```

---

### **8. Update Plan** (Owner Only)
```bash
curl -X PUT "https://localhost:5001/api/membership-plans/550e8400-e29b-41d4-a716-446655440000" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Monthly Unlimited - Updated",
    "nameAr": "غير محدود شهري - محدث",
    "description": "Updated description",
    "descriptionAr": "وصف محدث",
    "planType": "monthly_unlimited",
    "price": 349.00,
    "durationDays": 30,
    "sessionCount": null,
    "timeRestrictionStart": null,
    "timeRestrictionEnd": null,
    "invitationQuota": 0
  }' \
  --insecure

# Expected: 200 OK
```

---

### **9. Delete Plan (Success)** (Owner Only)
```bash
# Only works if plan has NO active memberships
curl -X DELETE "https://localhost:5001/api/membership-plans/550e8400-e29b-41d4-a716-446655440000" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure

# Expected: 200 OK
# Response: { "message": "Plan deleted successfully / تم حذف الخطة بنجاح" }
```

---

### **10. Delete Plan (Conflict - Has Active Members)** (Owner Only)
```bash
# Fails if plan has active memberships
curl -X DELETE "https://localhost:5001/api/membership-plans/550e8400-e29b-41d4-a716-446655440000" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure

# Expected: 409 Conflict
# Response: {
#   "error": "Cannot delete plan with 5 active memberships / لا يمكن حذف خطة بها أعضاء نشطين",
#   "message": "This plan has 5 active members"
# }
```

---

## ❌ Authorization Tests

### **Test 1: Manager Tries to Create (Should Fail)**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Plan",
    "nameAr": "خطة اختبار",
    "planType": "monthly_unlimited",
    "price": 299,
    "durationDays": 30
  }' \
  --insecure

# Expected: 403 Forbidden
# Reason: OwnerOnly policy requires Owner role
```

### **Test 2: Trainer Tries to Delete (Should Fail)**
```bash
curl -X DELETE "https://localhost:5001/api/membership-plans/550e8400-e29b-41d4-a716-446655440000" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure

# Expected: 403 Forbidden
# Reason: Only Owner can delete
```

---

## ❌ Validation Tests

### **Test 1: Invalid Session Count**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Invalid Pack",
    "nameAr": "باقة غير صحيحة",
    "planType": "session_pack",
    "price": 450,
    "durationDays": 60,
    "sessionCount": 15
  }' \
  --insecure

# Expected: 400 Bad Request
# Error: "Session count must be 10, 20, or 50 / عدد الجلسات يجب أن يكون 10 أو 20 أو 50"
```

### **Test 2: Missing Time Restrictions for Time-Limited Plan**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Morning Pass",
    "nameAr": "تذكرة الصباح",
    "planType": "time_limited",
    "price": 199,
    "durationDays": 30
  }' \
  --insecure

# Expected: 400 Bad Request
# Error: "Time restriction start is required for time-limited plans"
```

### **Test 3: Invalid Time Range (End Before Start)**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Invalid Time",
    "nameAr": "وقت غير صحيح",
    "planType": "time_limited",
    "price": 199,
    "durationDays": 30,
    "timeRestrictionStart": "12:00",
    "timeRestrictionEnd": "06:00"
  }' \
  --insecure

# Expected: 400 Bad Request
# Error: "End time must be after start time / وقت النهاية يجب أن يكون بعد وقت البداية"
```

### **Test 4: Negative Price**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Invalid Price",
    "nameAr": "سعر غير صحيح",
    "planType": "monthly_unlimited",
    "price": -100,
    "durationDays": 30
  }' \
  --insecure

# Expected: 400 Bad Request
# Error: "Price must be greater than 0 / السعر يجب أن يكون أكبر من 0"
```

### **Test 5: Empty Name**
```bash
curl -X POST "https://localhost:5001/api/membership-plans" \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "",
    "nameAr": "خطة اختبار",
    "planType": "monthly_unlimited",
    "price": 299,
    "durationDays": 30
  }' \
  --insecure

# Expected: 400 Bad Request
# Error: "Plan name is required / اسم الخطة مطلوب"
```

---

## 📊 Testing Checklist

### ✅ Basic CRUD Operations
- [ ] ✓ Get all plans (200 OK)
- [ ] ✓ Get plan by ID (200 OK)
- [ ] ✓ Create plan (201 Created)
- [ ] ✓ Update plan (200 OK)
- [ ] ✓ Delete empty plan (200 OK)
- [ ] ✓ Delete plan with members (409 Conflict)

### ✅ Plan Type Validation
- [ ] ✓ Monthly Unlimited (valid)
- [ ] ✓ Session Pack with valid count (10/20/50) (201 Created)
- [ ] ✓ Session Pack with invalid count (400 Bad Request)
- [ ] ✓ Time Limited with restrictions (201 Created)
- [ ] ✓ Time Limited without restrictions (400 Bad Request)
- [ ] ✓ Time Limited with invalid time range (400 Bad Request)
- [ ] ✓ PT Credits (201 Created)
- [ ] ✓ Family Plan (201 Created)

### ✅ Authorization
- [ ] ✓ Owner can create/update/delete (Success)
- [ ] ✓ Manager cannot create/update/delete (403 Forbidden)
- [ ] ✓ Trainer cannot create/update/delete (403 Forbidden)
- [ ] ✓ Manager can read (200 OK)
- [ ] ✓ Trainer can read (200 OK)

### ✅ Field Validation
- [ ] ✓ Empty name (400 Bad Request)
- [ ] ✓ Negative price (400 Bad Request)
- [ ] ✓ Zero duration (400 Bad Request)
- [ ] ✓ Invalid plan type (400 Bad Request)
- [ ] ✓ Negative invitation quota (400 Bad Request)

### ✅ Multi-Tenancy
- [ ] ✓ Plans isolated by tenant
- [ ] ✓ Cross-tenant access prevented

---

## 🔧 Debugging Tips

### Enable Detailed Logging
```json
// In appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "GMS.Api.Controllers.MembershipPlansController": "Debug",
      "GMS.Application.Services.MembershipPlanService": "Debug"
    }
  }
}
```

### Check Request/Response in Browser DevTools
1. Open browser (F12)
2. Network tab
3. Make request to API
4. Click request → examine headers and body

### SQL Query Monitoring
```sql
-- Check plans created
SELECT Id, Name, NameAr, PlanType, Price, IsActive, CreatedAtUtc 
FROM MembershipPlans 
WHERE TenantId = '{your-tenant-id}'
ORDER BY CreatedAtUtc DESC;

-- Check soft-deleted plans
SELECT * FROM MembershipPlans WHERE IsActive = 0;

-- Check active memberships on a plan
SELECT COUNT(*) as ActiveMemberships 
FROM Memberships 
WHERE MembershipPlanId = '{plan-id}' 
AND (Status = 'active' OR Status = 'frozen');
```

---

## 📝 Example Postman Collection

```json
{
  "info": {
    "name": "MembershipPlans API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Get All Plans",
      "request": {
        "method": "GET",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer YOUR_ACCESS_TOKEN"
          }
        ],
        "url": {
          "raw": "{{base_url}}/api/membership-plans",
          "host": ["{{base_url}}"],
          "path": ["api", "membership-plans"]
        }
      }
    },
    {
      "name": "Create Monthly Plan",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer YOUR_ACCESS_TOKEN"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "body": {
          "mode": "raw",
          "raw": "{\n  \"name\": \"Monthly Unlimited\",\n  \"nameAr\": \"غير محدود شهري\",\n  \"description\": \"Unlimited gym access for 30 days\",\n  \"descriptionAr\": \"وصول غير محدود للصالة لمدة 30 يوم\",\n  \"planType\": \"monthly_unlimited\",\n  \"price\": 299.00,\n  \"durationDays\": 30,\n  \"sessionCount\": null,\n  \"timeRestrictionStart\": null,\n  \"timeRestrictionEnd\": null,\n  \"invitationQuota\": 0\n}"
        },
        "url": {
          "raw": "{{base_url}}/api/membership-plans",
          "host": ["{{base_url}}"],
          "path": ["api", "membership-plans"]
        }
      }
    }
  ],
  "variable": [
    {
      "key": "base_url",
      "value": "https://localhost:5001"
    }
  ]
}
```

---

## 📞 Troubleshooting

### "401 Unauthorized"
- **Cause**: Invalid or missing JWT token
- **Solution**: Verify token in Authorization header, ensure it's not expired

### "403 Forbidden"
- **Cause**: User role insufficient for operation
- **Solution**: Create/Delete needs Owner, Get needs AnyStaff

### "400 Bad Request - Validation Error"
- **Cause**: Invalid request data
- **Solution**: Check error message, validate plan type requirements

### "404 Not Found"
- **Cause**: Plan ID doesn't exist
- **Solution**: Verify plan ID exists in your tenant

### "409 Conflict"
- **Cause**: Trying to delete plan with active members
- **Solution**: Remove/expire active memberships first, or use different plan

---

**Last Updated**: May 3, 2026  
**Version**: 1.0.0
