# Employee App Phase 1 — API Contract

> Implemented contract (not a proposal). Auth: Gym Code + HR activation code → JWT role `Employee`.

## Architecture

| Concept | Field / mechanism |
|---------|-------------------|
| Staff desk link | `Employee.AppUserId` → `AppUser` (Manager/Trainer/Receptionist…) |
| Employee App identity | `Employee.EmployeeAppUserId` → `AppUser` with `Role=Employee` |
| Unlink staff | Clears `AppUserId` only — **does not** clear `EmployeeAppUserId` |
| `/me` resolve | JWT `sub` → `AppUser` → Employee where `EmployeeAppUserId` **or** `AppUserId` match, and `Status == Active` |

Employee App users do **not** receive Staff permissions (`perm` claims empty for role Employee).

---

## 1. Generate activation code

```
POST /api/hr/employees/{employeeId}/app-activation-code
Authorization: Bearer <staff JWT>
Permission: hr.manage
Feature: hr
```

**Rules:** Employee must exist in tenant, not deleted, `Status == Active`. Prior unused codes for that employee are revoked. Code stored as SHA-256 hash only.

**200 response:**

```json
{
  "employeeId": "…",
  "employeeNumber": "EMP-0001",
  "activationCode": "ABCD-EFGH",
  "expiresAtUtc": "2026-08-28T00:00:00Z",
  "expiresInMinutes": 1440
}
```

Plaintext is returned **once**. Default expiry: 24h (`EmployeeAppActivation:ExpirationHours`).

**Errors:** 404 not found · 400 unable to activate (non-Active) · 401/403 auth

Audit: `employee.app_activation_code.generated` (no plaintext).

---

## 2. Employee activate

```
POST /api/auth/employee-activate
AllowAnonymous
Rate limit: employee-activate-policy (10 / minute / IP)
```

**Body:**

```json
{
  "gymCode": "GYM-TEST-01",
  "activationCode": "ABCD-EFGH"
}
```

(Hyphens/spaces ignored; case-insensitive.)

**200:** same `LoginResponse` shape as staff/member activate:

```json
{
  "accessToken": "…",
  "refreshToken": "…",
  "expiresAtUtc": "…",
  "user": {
    "id": "<Identity user Guid>",
    "email": "emp-0001@employee.gymflowpro.local",
    "fullName": "…",
    "role": "Employee",
    "tenantId": "…",
    "gymCode": "…"
  }
}
```

**On success:** code consumed · Identity + AppUser (Role=Employee) created/reused · `Employee.EmployeeAppUserId` set.

**401** generic failures (do not leak):

- Invalid / expired / revoked / wrong gym
- Already used → `"This activation code has already been used."`
- Non-Active employee → `"Unable to activate this account."`

Client must **not** send EmployeeId.

Audit: `employee.app_activation.completed` (no plaintext).

Refresh: `POST /api/auth/refresh` with `{ "refreshToken" }` (unchanged).

---

## 3. Employee role

- Seeded Identity role: `Employee` (with Owner/Manager/Trainer/Receptionist/Member).
- Policy: `AuthenticatedEmployee` = RequireRole(`Employee`).
- `RolePermissionResolver` grants **no** Staff permissions for Employee (same as Member).

---

## 4. GET /api/hr/employees/me

```
GET /api/hr/employees/me
Authorization: Bearer <Employee JWT>
Feature: hr
```

Resolves Employee from JWT. Never accepts EmployeeId from client.

**200** `EmployeeMeDto`:

```json
{
  "id": "…",
  "employeeNumber": "EMP-0001",
  "firstName": "Ahmed",
  "lastName": "Mohamed",
  "fullName": "Ahmed Mohamed",
  "phone": "…",
  "email": "…",
  "photoUrl": null,
  "status": "Active",
  "departmentId": null,
  "departmentName": null,
  "positionId": null,
  "positionName": null,
  "hireDate": "2024-01-01",
  "dateOfBirth": null,
  "createdAtUtc": "…"
}
```

**403** if not linked or Employee not Active.

---

## 5. Existing /me endpoints (Flutter can consume)

All require Bearer + `hr` feature; resolve via same identity chain; Active required:

| Method | Path |
|--------|------|
| GET | `/api/hr/employee-attendance/me?from=&to=` |
| POST | `/api/hr/employee-attendance/me/check-in` |
| POST | `/api/hr/employee-attendance/me/check-out` |
| GET | `/api/hr/employee-schedules/me?from=&to=` |
| GET | `/api/hr/leave-requests/me` |
| POST | `/api/hr/leave-requests/me` |
| POST | `/api/hr/leave-requests/me/{id}/cancel` |
| GET | `/api/hr/leave-balances/me` |
| GET | `/api/hr/payroll-periods/me` |
| GET | `/api/hr/employees/me/documents` |
| GET | `/api/hr/employee-documents/me/{id}/file` |

Suspended/Terminated: `/me` resolve returns null → **403** (JWT alone is not enough).

---

## 6. JWT expectations

Claims (same TokenService): `sub`, `email`, `jti`, `tenant_id`, `gym_code`, `first_name`, `last_name`, `role` (= Employee), no Staff `perm` grants.

`user.id` / `sub` = Identity id — **not** Employee.Id.

---

## 7. Activation code expiry

- Config section `EmployeeAppActivation`: `ExpirationHours` (default 24), `CodePepper` (optional; falls back to JWT secret).
- Regenerate revokes prior unused codes for that employee.
- Concurrent consume: `RowVersion` → already-used error.

---

## 8. Staff coexistence

| Case | Behavior |
|------|----------|
| Employee only | Activate → Employee App works; no Staff desk |
| Employee + Staff linked | Both identities; unlink Staff keeps Employee App |
| Staff not linked | Staff login does not grant Employee App unless `AppUserId` or `EmployeeAppUserId` set |

`POST /api/hr/employees/{id}/link-staff` / `unlink-staff` unchanged (unlink does not touch Employee App identity). Linking rejects AppUsers with Role `Employee` or `Member`.

---

## 9. HR Web

Employee drawer: **Generate Employee App Code** (Active + `hr.manage`) — shows code, expiry, copy.
