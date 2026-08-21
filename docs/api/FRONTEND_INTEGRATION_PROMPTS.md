# GymFlowPro — Frontend Integration Documentation

Generated directly from the current backend implementation (`GMS.Api`, `GMS.Application`, `GMS.Core`,
`GMS.Infrastructure`). Every endpoint, request/response shape, status value, and permission listed
here exists in the codebase as of this writing — nothing is speculative. Where the backend does not
yet implement something (e.g. a UI convenience endpoint), it is called out explicitly as **Not
implemented** rather than assumed.

This document contains no visual/design guidance (colors, layout, spacing, components) — it is a
data and behavior contract only.

---

## 0. Cross-Cutting Concerns

Read this section first — every feature section below assumes it.

### 0.1 Base URL & Transport

- All REST endpoints are under `/api/...`. Swagger UI is served at the API root in Development
  (`app.UseSwaggerUI` with `RoutePrefix = string.Empty`).
- Real-time transport: SignalR hub at `/hubs/attendance` (WebSocket, falls back per SignalR client
  negotiation). See §5 (Attendance) for the one event it currently emits.
- A Hangfire dashboard exists at `/hangfire` (Basic-Auth-gated, `HangfireDashboardAuthFilter`) — this
  is an ops surface, not a frontend integration point.

### 0.2 Multi-Tenancy — how the backend resolves "which gym"

Every request except `/api/auth/*`, `/health`, `/swagger`, `/_framework` is resolved to a tenant by
`TenantMiddleware` using, in order:

1. JWT claim `gym_code` (present on every token issued by `/api/auth/login`, `/api/auth/member-verify`).
2. Fallback header `X-Gym-Code` (only needed for anonymous/pre-auth calls that still need tenant
   context — in practice this codebase's endpoints are all either `/api/auth/*` or already
   authenticated, so the header path is a rarely-exercised fallback, not the primary mechanism).

If an authenticated request has neither, the middleware returns **401** with
`{"error": "Tenant context required. Provide gym_code claim or X-Gym-Code header."}` before the
request reaches any controller. If the resolved `gym_code` doesn't match a tenant, or the tenant is
`IsActive = false`, the middleware also returns 401 with a specific message
(`"Invalid gym code."` / `"This gym is currently inactive."`).

**Frontend implication:** once a JWT is obtained from login, no separate tenant header is normally
required — the JWT alone carries tenant context for every subsequent call. Store `gym_code` at login
time only if you need it for display or for pre-auth flows (e.g. showing the gym name on a login
screen before authenticating), not because the API needs it as a header in the normal case.

### 0.3 Authentication

Two identity types share one JWT scheme (`JwtBearerDefaults`, HS256, `SecurityKey` from
`JwtSettings:SecretKey`), distinguished by ASP.NET Identity **role** claims:

- **Staff** (`Owner` / `Manager` / `Trainer` / `Receptionist`): `POST /api/auth/login` with
  email+password+gymCode.
- **Member** (`Member` role): two-step OTP flow — `POST /api/auth/member-otp` then
  `POST /api/auth/member-verify`.

Token claims relevant to frontend logic:
- `sub` (or `ClaimTypes.NameIdentifier`) — the ASP.NET Identity user id (staff) or the JWT subject
  used to resolve a `GymMember` (member flows use this same claim location).
- `tenant_id` — tenant GUID.
- `gym_code` — tenant's gym code (drives `TenantMiddleware`, see §0.2).
- `member_id` — present only on Member-role tokens; used by `NotificationsController` to scope a
  member's own notifications.
- `perm` — zero or more discrete permission-string claims (see §0.4); resolved once at login and
  baked into the token — **a permission or role change on the backend does not take effect until the
  user's next login/refresh**, since nothing is re-checked against the DB per request.

Access tokens expire in **15 minutes**; refresh tokens in **30 days**
(`POST /api/auth/refresh` with `{ refreshToken }`, returns a new token pair with sliding rotation —
the old refresh token is revoked when a new one is issued). On a 401 with response header
`Token-Expired: true`, the frontend should attempt a silent refresh before forcing re-login.

SignalR connections authenticate the same JWT via `?access_token=` query string (only accepted on
paths starting with `/hubs`) since browsers can't set an `Authorization` header on a WebSocket
handshake.

### 0.4 Permission Model

Two authorization layers coexist and either may gate any given endpoint — check both when wiring a
screen:

**A. Named role policies** (`[Authorize(Policy = "...")]`):
| Policy | Requirement |
|---|---|
| `OwnerOnly` | role = Owner |
| `ManagerOrAbove` | role = Owner or Manager |
| `AnyStaff` | role = Owner, Manager, or Trainer |
| `AuthenticatedMember` | role = Member |
| `AnyAuthenticated` | any authenticated user |

**B. Fine-grained permissions** (`[HasPermission(Permissions.X)]`): checks a single `perm` claim
string. The full permission universe (`GMS.Core.Constants.Permissions`):

`members.view`, `members.create`, `members.edit`, `checkin.manual`, `sales.sell`,
`sales.discount.apply`, `sales.discount.override`, `payments.cash.accept`,
`payments.refund.request`, `payments.refund.approve`, `shift.open`, `shift.close`,
`shift.reconcile.approve`, `memberships.freeze`, `plans.manage`, `reports.financial.view`,
`settings.manage`.

A failed check under either layer returns a bare **403** (default ASP.NET Core challenge — no JSON
body is guaranteed unless the specific endpoint's own logic produces one). Do not rely on parsing a
403 body for a reason code; instead, hide/disable actions client-side based on the permissions your
own login response's role/claims imply, and treat 403 purely as "this shouldn't have been reachable."

**Receptionist role note:** `UserRole` enum includes `Receptionist = 5`, intended for front-desk
intake/manual-checkin/cash-sale duties without plan/settings management — drive its capabilities from
the actual `perm` claims issued to it at login (which permissions a Receptionist receives is an
`AuthService`/`DataSeeder` concern, not fixed by the enum itself).

### 0.5 Error Envelope

Two different error shapes appear across the API — the frontend must handle both:

**A. `ProblemDetails` (RFC 7807)** — used by controllers that call `Problem(detail:, statusCode:,
title:)`. Shape:
```json
{ "type": "...", "title": "CODE_STRING", "status": 400, "detail": "Human-readable message", "traceId": "..." }
```
`title` is a **machine-readable code** (e.g. `FORBIDDEN_DISCOUNT_OVERRIDE`, `NO_OPEN_SHIFT`,
`FEATURE_DISABLED`) suitable for `switch`-style frontend handling; `detail` is a bilingual
English/Arabic message (`"message / رسالة"`) suitable for direct display. This is the dominant
pattern for Sales, Shifts, Refunds, Trials, Debtors, Call Sheet, Imports, Z-Reports, Invoices,
Promo Codes, Audit.

**B. Ad-hoc `{ error, message }` JSON** — used by older/simpler controllers via the base
`BadRequest(string)`/`NotFound(string)` helpers on `BaseApiController`, or explicit anonymous objects.
Shape varies slightly by controller but is consistently at least `{ "error": "..." }`, sometimes with
an additional `message` field. Used by Members, Memberships, Plans, Admin, TenantSettings,
Notifications, Invitation.

**Recommendation:** when parsing an error body, check for `title`/`detail` (ProblemDetails) first,
fall back to `error`/`message`. Do not assume one shape API-wide.

**Machine-readable code catalogs** (the `title`/leading-segment values you can safely switch on):

| Service | Codes |
|---|---|
| Sales | `STAFF_USER_NOT_FOUND`, `MEMBER_NOT_FOUND`, `MEMBER_CREATE_FAILED`, `PLAN_NOT_FOUND`, `FORBIDDEN_DISCOUNT_OVERRIDE` (→403), `OVERPAY`, `PAYMENT_INCOMPLETE`, `OPEN_SHIFT_REQUIRED` (→409), `PROMO_RACE_LOST`, `SALE_NOT_FOUND`, `PAYMENT_EXCEEDS_AMOUNT_DUE`, `INSUFFICIENT_CREDIT` |
| Shifts | `STAFF_USER_NOT_FOUND`, `SHIFT_ALREADY_OPEN` (→409), `NO_OPEN_SHIFT` (→409), `SHIFT_NOT_FOUND` (→404), `NOT_AWAITING_APPROVAL` (→409), `MANAGER_APPROVAL_REQUIRED` (→403), `INVALID_MOVEMENT_TYPE`, `SHIFT_NOT_OPEN` (→409) |
| Refunds | `STAFF_USER_NOT_FOUND`, `SALE_NOT_FOUND` (→404), `REFUND_NOT_FOUND` (→404), `REFUND_EXCEEDS_REMAINDER`, `SALE_FULLY_REFUNDED` (→409), `NOT_AWAITING_APPROVAL` (→409), `SELF_APPROVAL_FORBIDDEN` (→403), `OPEN_SHIFT_REQUIRED` (→409), `GATEWAY_REFUND_UNSUPPORTED` (→409), `INSUFFICIENT_CREDIT` |
| Trials | `PLAN_NOT_FOUND` (→404), `PLAN_NOT_TRIAL`, `TRIAL_ALREADY_USED` (→409), `OTP_INVALID`, `PENDING_TRIAL_NOT_FOUND` (→404), `STAFF_USER_NOT_FOUND` |
| Debtors | `MEMBER_NOT_FOUND` (→404), `NO_OUTSTANDING_BALANCE` (→404), `REMINDER_THROTTLE` (→429) |
| Call Sheet | `MEMBERSHIP_NOT_FOUND` (→404), `STAFF_USER_NOT_FOUND` (→404), `INVALID_OUTCOME` |
| Imports | `BATCH_NOT_FOUND` (→404), `INVALID_STATUS`, `FILE_TOO_LARGE`, `TOO_MANY_ROWS`, `UNSUPPORTED_FILE_TYPE`, `ROLLBACK_WINDOW_EXPIRED` |
| Import row errors (per-row, comma-joined in `ImportRow.ErrorCodes`) | `PHONE_INVALID`, `PHONE_DUP_FILE`, `PHONE_EXISTS`, `PLAN_UNMATCHED`, `DATE_RANGE_INVALID`, `RETAINED_HAS_ACTIVITY`, `ROLLED_BACK` |
| Z-Reports | `ZREPORT_NOT_FOUND` (→404) |
| Promo validation (`POST /api/sales/validate-promo` failure body's `FailureReason` field, not a ProblemDetails title) | `CODE_NOT_FOUND`, `CODE_INACTIVE`, `DATE_RANGE_INVALID`, `MAX_USES_REACHED`, `MEMBER_MAX_USES_REACHED`, `PLAN_NOT_IN_SCOPE`, `BELOW_MIN_PRICE`, `PLAN_NOT_FOUND` |
| Feature flags | `FEATURE_DISABLED` (→404, ProblemDetails) |

Where a status code isn't annotated above, it defaults to **400 Bad Request**.

### 0.6 Pagination Convention

List endpoints that page (Members, Shifts, Invoices, Promo Codes, Audit) return
`GMS.Application.Common.PagedResult<T>`:
```json
{
  "items": [ /* T[] */ ],
  "totalCount": 137,
  "page": 1,
  "pageSize": 20,
  "totalPages": 7,
  "hasNext": true,
  "hasPrevious": false
}
```
Request-side, pagination is always `?page=1&pageSize=20` query parameters (1-based page numbers).
Not every list endpoint pages — several (Debtors CSV export, Promo Codes' `AppliesTo` filtering, Call
Sheet, Analytics) return a flat array or a single object instead; check each feature section below.

### 0.7 Idempotency

`POST /api/sales` accepts an idempotency key via **either** the `X-Idempotency-Key` header or the
request body's `idempotencyKey` field (header wins if both are supplied). A repeated call with the
same key against the same tenant returns the original `SaleResponse` with `isReplay: true` and
creates no new rows — safe to retry blindly on network ambiguity (timeout, disconnect) without
double-charging a customer. No other endpoint in this API currently implements idempotency-key
support — do not assume it elsewhere.

### 0.8 Feature Flags

Six modules can be disabled per-tenant (`Tenant.Settings` JSON, nested `feature_flags` key — this is
a backend/ops-configured setting; **no endpoint currently exists to read or write it from the
frontend**, so do not build a settings-page toggle for this yet): `sales`, `shifts`, `trials`,
`refunds`, `debtors`, `imports`. All default to `true` (enabled) if unset. When disabled, every
endpoint under that module's controller returns:
```json
{ "type": "...", "title": "FEATURE_DISABLED", "status": 404, "detail": "..." }
```
**Frontend implication:** treat a `FEATURE_DISABLED` 404 exactly like "this module doesn't exist for
this tenant" — hide the corresponding nav/section rather than surfacing a generic error, and expect
this to be discoverable only by calling the endpoint (there is no separate "which modules are enabled
for me" endpoint to pre-check against).

### 0.9 Real-time (SignalR)

One hub, one event, currently:
- Hub: `/hubs/attendance`, `[Authorize]`, requires a valid JWT (via `?access_token=` on the
  WebSocket handshake).
- On connect, the client is auto-joined to group `tenant-{tenantId}` (read from the `tenant_id` JWT
  claim) — no client-side subscribe call needed beyond connecting.
- Event: **`MemberCheckedIn`** — pushed to the tenant's group whenever a check-in succeeds (QR or
  manual), payload:
  ```json
  { "memberId": "guid", "memberName": "string", "memberNumber": "string", "checkInTime": "ISO-8601", "entryMethod": "qr | manual" }
  ```
- The push is best-effort — if it fails, the check-in itself still succeeds (the failure is only
  logged server-side). Do not build any flow that depends on `MemberCheckedIn` for correctness (e.g.
  don't skip re-fetching attendance data on your own explicit actions); use it only to live-update a
  passive dashboard view.
- No other real-time events exist (no refund/shift/sale push notifications) — anything else needing
  "live" data must be polled.

---

## 1. Authentication & Session Management

### Purpose
Establish a staff or member identity and obtain the JWT used by every other endpoint.

### User Flow
- **Staff:** enter email + password + gym code → receive token pair → store both, use access token
  as `Authorization: Bearer` on all calls → on 401/`Token-Expired`, call refresh → on refresh failure,
  force re-login.
- **Member:** enter phone number + gym code → request OTP → enter 6-digit code → receive token pair
  (same refresh mechanics as staff).

### Backend APIs Used
| Method | Endpoint | Auth |
|---|---|---|
| POST | `/api/auth/login` | Anonymous |
| POST | `/api/auth/refresh` | Anonymous (refresh token itself is the credential) |
| POST | `/api/auth/member-otp` | Anonymous |
| POST | `/api/auth/member-verify` | Anonymous |

### Request/Response Contracts
- `LoginRequest`: `{ email, password, gymCode }` → `LoginResponse`:
  ```json
  { "accessToken": "...", "refreshToken": "...", "expiresAtUtc": "ISO-8601",
    "user": { "id": "guid", "email": "...", "fullName": "...", "role": "Owner|Manager|Trainer|Receptionist", "tenantId": "guid", "gymCode": "..." } }
  ```
- `RefreshTokenRequest`: `{ refreshToken }` → `LoginResponse` (new pair, old refresh token revoked).
- `MemberOtpRequest`: `{ phoneNumber, gymCode }` → `200 OK` with `{ message }` (not a typed DTO — plain
  anonymous object). OTP validity window is 5 minutes per `AuthController`'s doc comment.
- `MemberOtpVerifyRequest`: `{ phoneNumber, gymCode, otp }` → `LoginResponse` (same shape as staff
  login; auto-provisions an Identity user for the member on first verification if one doesn't exist).

### State Management
Persist `accessToken`, `refreshToken`, `expiresAtUtc`, and the `user` object from `LoginResponse`.
Treat `role` as authoritative for which UI sections to show, but always let the backend's 401/403 be
the final authority (client-side role checks are a UX convenience, not a security boundary).

### Validation Rules
No client-side validation rules are enforced by the backend beyond required non-empty fields on
these DTOs — password strength, phone format, etc. are not validated on these specific endpoints
(contrast with member creation, which does not validate phone format either per `CreateMemberRequest`
having no documented pattern constraint).

### Error Handling
- `401 Unauthorized` with `{ error }` on bad credentials/OTP/gym code.
- Header `Token-Expired: true` accompanies a 401 caused specifically by an expired JWT — distinguish
  this from a bad-credentials 401 to decide whether to silently refresh vs. force re-login.

### Permissions & Authorization
All four endpoints are `[AllowAnonymous]`.

### Real-time Behavior
None.

### Integration Notes
- `gymCode` must be supplied by the user (or pre-filled from a saved/last-used value) at login time —
  there is no "discover my gym" endpoint.
- Access tokens are short-lived (15 min) by design — plan for a refresh interceptor on every API
  client, not just error-path handling.

### Edge Cases
- Refresh token reuse after rotation: the old token is revoked on issuance of a new one — a client
  holding a stale cached refresh token after a successful refresh elsewhere will fail; always persist
  the newest pair immediately, and treat a refresh failure as "log out," not "retry."

### Acceptance Criteria
- A valid login/OTP-verify returns a usable bearer token that succeeds against a permissioned
  endpoint matching the returned `role`.
- An expired access token triggers a refresh-then-retry cycle transparently to the end user at least
  once per token lifetime.

---

## 2. Member Management

### Purpose
CRUD + search over gym members, plus membership-adjacent read actions (attendance history, current
membership, account-credit balance) surfaced from the member profile.

### User Flow
Staff searches/browses a paginated member list → opens a member profile → views current membership,
recent attendance, and credit balance → edits details, freezes/unfreezes membership, or deactivates
the member.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/members?search=&status=&page=&pageSize=` | `members.view` |
| GET | `/api/members/{id}` | `members.view` |
| POST | `/api/members` | `members.create` |
| PUT | `/api/members/{id}` | `members.edit` |
| DELETE | `/api/members/{id}` | `OwnerOnly` policy (not a permission) |
| GET | `/api/members/{id}/attendance?page=&pageSize=` | `members.view` |
| GET | `/api/members/{id}/membership` | `members.view` |
| POST | `/api/members/{id}/freeze` | `memberships.freeze` |
| POST | `/api/members/{id}/unfreeze` | `memberships.freeze` |
| GET | `/api/members/{id}/credits` | `members.view` |

### Request/Response Contracts
- `GET /api/members`: query `search` (name/phone/number, optional), `status` (optional, exact values
  not enforced server-side beyond whatever `IMemberService` matches against — treat as free-form
  unless you've confirmed the accepted set against `MemberService`), `page`, `pageSize` → paged list
  of `MemberListItemDto`:
  ```json
  { "id","memberNumber","fullName","fullNameAr","phone","isActive","activePlan","activePlanAr","expiryDate","membershipStatus" }
  ```
- `GET /api/members/{id}` → `MemberDetailDto`:
  ```json
  { "id","memberNumber","fullName","fullNameAr","phone","email","dateOfBirth","profilePhotoUrl","notes",
    "isActive","invitationQuotaRemaining","createdAtUtc",
    "currentMembership": { "id","planName","planNameAr","planType","status","startDate","endDate",
       "sessionsRemaining","frozenFromDate","frozenUntilDate","amountPaid","paymentMethod" } | null,
    "recentAttendance": [ { "id","checkInAtUtc","checkOutAtUtc","entryMethod" } ] }
  ```
  (`recentAttendance` is capped to the 5 most recent — confirmed by `MembersController`'s doc
  comment; do not build "load more" pagination on this embedded list, use `GET .../attendance`
  instead for a full paged history.)
- `CreateMemberRequest`: `{ fullName, fullNameAr, phone, dateOfBirth, nationalId?, emergencyContact?, email?, notes? }` → `201 Created` with `MemberDetailDto`, `Location` header pointing at `GET /api/members/{id}`.
- `UpdateMemberRequest`: same fields, all optional (partial update — only supplied fields change).
- `DELETE /api/members/{id}`: soft-deletes (`IsActive = false`) — **irreversible without direct DB
  access**, per the controller's own doc comment. Returns `{ message }`.
- `GET /api/members/{id}/attendance`: paged list of attendance (shape not separately named in the
  DTOs surveyed — returned via `IMemberService.GetMemberAttendanceAsync`; treat as an opaque paged
  result of attendance records matching `AttendanceSummaryDto`'s fields until/unless you inspect the
  service directly).
- `GET /api/members/{id}/membership` → `MembershipSummaryDto` (same shape as `MemberDetailDto`'s
  nested `currentMembership`).
- `FreezeMembershipRequest`: `{ frozenUntil: "ISO-8601 DateTime", reason? }` → `{ message }`.
  Unfreeze takes no body.
- `GET /api/members/{id}/credits` → `MemberCreditSummaryDto`:
  ```json
  { "balance": 150.00, "entries": [ { "id","amount","entryType": "refund|payment_use|adjustment","referenceId","reason","createdAtUtc" } ] }
  ```

### State Management
Member list should be re-fetched (not optimistically patched) after create/update/deactivate, since
`MembershipStatus`/`ActivePlan` are server-derived aggregates, not raw member fields.

### Validation Rules
No FluentValidation validators were found specifically for `CreateMemberRequest`/`UpdateMemberRequest`
in the areas surveyed — treat required-ness as: `fullName`, `fullNameAr`, `phone`, `dateOfBirth` are
the meaningfully required fields for creation (all others nullable in the DTO), but confirm against
`IMemberService.CreateMemberAsync`'s actual checks before hard-coding client-side required-field
enforcement beyond what's structurally non-nullable in the DTO.

### Error Handling
`400`/`404` via the ad-hoc `{ error, message }` shape (Members uses `BaseApiController.BadRequest`/
`NotFound` helpers, not `ProblemDetails`) — see §0.5.B.

### Permissions & Authorization
See table above. Note the asymmetry: `DELETE` requires the `OwnerOnly` **role policy**, not a
`members.*` **permission** — a Manager with full `members.edit`/`members.create` permissions still
cannot deactivate a member.

### Real-time Behavior
None directly; a member's check-in (elsewhere) will indirectly affect `RecentAttendance` on next
fetch of this profile, but this endpoint itself is not pushed to.

### Integration Notes
`GET /api/members/{id}/credits` is defined on `MembersController` but is implemented via
`IRefundService.GetMemberCreditSummaryAsync` — a cross-feature read; keep this in mind if you're
build a permissions matrix that assumes one controller = one service.

### Edge Cases
- A member with no membership ever assigned returns `currentMembership: null` — do not assume every
  member has at least an expired membership row.
- Freeze/unfreeze act on "a member's active membership" — if no active membership exists, expect a
  `400` (`IMemberService.FreezeMembershipAsync` presumably returns a failure `Result` in that case;
  the exact message isn't enumerated in a constants file the way Sales/Shifts/etc. are, so surface
  whatever `error`/`message` text comes back verbatim rather than trying to pattern-match it).

### Acceptance Criteria
- List, search, and pagination all reflect real backend state (no client-side filtering of an
  unpaged full list).
- Freeze/unfreeze and deactivate immediately reflect in a subsequent `GET /api/members/{id}` call.

---

## 3. Membership Plans

### Purpose
Owner/Manager-facing CRUD for the plans members can be sold (monthly, session-pack, time-limited,
pt-credits, family, trial, day-pass).

### User Flow
Manage plans in a settings-style list → create/edit a plan with type-specific fields → attempt delete
→ if plan has active memberships, delete is blocked with a specific conflict response.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/membership-plans` | `plans.manage` |
| GET | `/api/membership-plans/{id}` | `plans.manage` |
| POST | `/api/membership-plans` | `plans.manage` |
| PUT | `/api/membership-plans/{id}` | `plans.manage` |
| DELETE | `/api/membership-plans/{id}` | `plans.manage` |

Note: **reading** the plan list requires `plans.manage`, the same permission as writing — there is no
separate lower-privilege "view plans" permission on this controller (contrast with e.g. Members,
which separates `members.view` from `members.create`/`members.edit`). A role that needs to reference
plans (e.g. for a sales screen's plan picker) but shouldn't manage them will still need
`plans.manage` granted, or the plan list must be sourced from elsewhere for that role — there is no
alternate read-only plans endpoint in this API.

### Request/Response Contracts
- `GET /api/membership-plans` → `List<PlanListItemDto>` (not paged — a flat array):
  `{ id, name, nameAr, planType, price, currency, durationDays, isActive, createdAtUtc }`.
- `GET /api/membership-plans/{id}` → `PlanDetailDto` (adds `description`, `descriptionAr`,
  `sessionCount?`, `timeRestrictionStart?`, `timeRestrictionEnd?`, `invitationQuota`,
  `trialVisitLimit?`, `activeMemberships`, `totalMemberships`, `updatedAtUtc?`).
- `CreatePlanRequest`/`UpdatePlanRequest` (identical shape):
  ```json
  { "name","nameAr","description?","descriptionAr?",
    "planType": "monthly_unlimited|session_pack|time_limited|pt_credits|family|trial|day_pass",
    "price","durationDays","sessionCount?","timeRestrictionStart?","timeRestrictionEnd?",
    "invitationQuota": 0, "trialVisitLimit?" }
  ```
  Per the controller's doc comment: `session_pack` requires `sessionCount` to be **10, 20, or 50**;
  `time_limited` requires both `timeRestrictionStart` and `timeRestrictionEnd`. These constraints are
  enforced server-side (`400` on violation) — validate the same client-side before submit to avoid a
  round-trip, but the backend is the source of truth.
- `DELETE /api/membership-plans/{id}`: soft-delete. Returns **409 Conflict** (not 400) specifically
  when the plan has active memberships — check for this status code to show a distinct "can't delete,
  plan in use" message rather than a generic error.

### State Management
Plan list rarely changes mid-session — safe to cache per screen-load, but re-fetch after any
create/update/delete.

### Validation Rules
See plan-type-specific constraints above. `TimeOnly` fields serialize as `"HH:mm:ss"` strings.

### Error Handling
Ad-hoc `{ error, message }` shape; `409` specifically for the active-memberships delete conflict.

### Permissions & Authorization
`plans.manage` for all five operations — no split between read and write on this controller.

### Real-time Behavior
None.

### Integration Notes
`PlanListItemDto` is intentionally lighter than `PlanDetailDto` — don't fetch full detail for a
picker/dropdown UI; use the list endpoint.

### Edge Cases
- Deleting a plan currently referenced by active memberships is blocked (409) — the frontend cannot
  force it; the only backend-supported path is to wait until no active memberships reference it (or
  handle those memberships first).

### Acceptance Criteria
- Plan-type-specific fields are only submitted/required per the active `planType` selection.
- A 409 on delete is distinguished from a 400 in the UI.

---

## 4. Memberships (Assign / Renew)

### Purpose
Attach a membership plan purchase to a member outside the full POS sale flow (see §6 for the
POS-integrated purchase path via Sales). This is the direct membership-lifecycle API.

### User Flow
From a member profile: view current membership + history → assign a new membership (if none active)
or renew (if expired/active, same or different plan) → for cash, membership activates immediately;
for a gateway method, it's created `pending` until a webhook confirms payment (see §23).

### Backend APIs Used
| Method | Endpoint | Policy |
|---|---|---|
| GET | `/api/memberships/{memberId}/current` | `AnyStaff` |
| GET | `/api/memberships/{memberId}/history?page=&pageSize=` | `AnyStaff` |
| POST | `/api/memberships/{memberId}/assign` | `ManagerOrAbove` |
| POST | `/api/memberships/{memberId}/renew` | `ManagerOrAbove` |

Note this controller uses **role policies**, not `[HasPermission]` — a Trainer (in `AnyStaff` but not
`ManagerOrAbove`) can read but not assign/renew, regardless of any `members.*`/`plans.manage`
permission claims they hold.

### Request/Response Contracts
- `GET .../current` → `MembershipDto`:
  ```json
  { "id","planName","planNameAr","planType","startDate","endDate","status","sessionsRemaining",
    "amountPaid","paymentMethod","paymentDate","autoRenew","frozenFromDate","frozenUntilDate",
    "daysRemaining": <computed, EndDate - today, may be negative if expired> }
  ```
  Falls back to the **last expired membership** if none is currently active (per the controller's
  doc comment) — a non-null response here does not imply the member currently has access.
- `GET .../history` → paged `MembershipHistoryItemDto[]` (newest first): adds nothing structurally
  interesting beyond `MembershipDto` minus the live-only fields (`sessionsRemaining` retained,
  `daysRemaining` absent since these are historical rows).
- `AssignMembershipRequest`: `{ planId, paymentMethod: "cash|paymob|fawry" }` → `201 Created` +
  `MembershipDto`. **409 Conflict** if the member already has an active membership (check
  `result.Error` containing `"active membership"` — this is a substring-matched convention in the
  controller, not a formal code constant, so match loosely).
- `RenewMembershipRequest`: `{ planId?: null-means-same-plan, paymentMethod: "cash|paymob|fawry|vodafone_cash", amountPaid }` → `200 OK` + `MembershipDto`. `StartDate` is
  automatically the prior membership's `EndDate` (continuous membership) — the frontend does not
  supply a start date.

### State Management
After assign/renew, refresh both "current membership" and the member's top-level list-item fields
(`ActivePlan`, `ExpiryDate`) if displayed elsewhere on the same screen.

### Validation Rules
`paymentMethod` accepted values differ subtly between the two endpoints per their own doc comments
(`assign`: cash/paymob/fawry; `renew`: cash/paymob/fawry/vodafone_cash) — don't assume they're
identical; validate against each endpoint's own documented set.

### Error Handling
Ad-hoc `{ error, message }`; `409` specifically for "already has an active membership" on assign.

### Permissions & Authorization
Role-policy based (see table) — do not gate these actions behind `members.edit` or `plans.manage`
permission checks in the UI; use `ManagerOrAbove`-equivalent role logic instead.

### Real-time Behavior
None directly. If `paymentMethod` is a gateway, the eventual activation happens via an async webhook
(§23) with no push notification — the frontend must poll `GET .../current` (or re-fetch on user
action, e.g. pull-to-refresh) to observe the `pending → active` transition.

### Integration Notes
This is a separate purchase path from `POST /api/sales` (§6) — Sales creates its own Membership as a
side effect of a POS sale with line items, promo, and split payments; this controller is a simpler
direct assign/renew without those POS concepts (no promo code, no partial payment, no receipt). Don't
conflate the two when deciding which to call for a given screen.

### Edge Cases
- Assign fails with 409 if an active membership already exists — the caller must renew or otherwise
  resolve the existing membership first, there is no "force replace" option.
- A gateway-method assign/renew leaves the member in `pending` state indefinitely if the webhook
  never fires (e.g. customer abandons payment) — no automatic timeout/expiry is documented for this
  state within the surveyed code; build UI accordingly (e.g. an explicit "still waiting for payment"
  affordance) rather than assuming eventual consistency within a fixed window.

### Acceptance Criteria
- Assign is blocked (409, distinct message) when an active membership already exists.
- Renew correctly starts the new period at the prior membership's end date without the frontend
  computing or supplying that date itself.

---

## 5. Attendance & Check-In

### Purpose
Member self-service QR check-in (mobile) and staff-driven manual check-in (front desk), plus the live
attendance dashboard fed by SignalR.

### User Flow
- **QR (member app):** scan gym's static QR (encodes `gymCode`) → app POSTs the code with the
  member's own JWT → immediate accept/reject with a bilingual message.
- **Manual (front desk):** search for a member by name/phone/number → select from selectable results
  → choose a reason → check in.

### Backend APIs Used
| Method | Endpoint | Auth |
|---|---|---|
| POST | `/api/attendance/qr-checkin` | Policy `AuthenticatedMember`; rate-limited (`checkin-policy`: 30 req/min per IP, no queueing — 30 sec waiting or so is not how limiting is enforced client-side, expect immediate 429s past 30/min per IP, not delayed) |
| POST | `/api/attendance/manual-checkin` | (not fully detailed in this survey's controller excerpt beyond line 55 — confirm required policy/permission directly against `AttendanceController` before wiring; the doc comment states "requires checkin.manual permission") |

### Request/Response Contracts
- `QrCheckinRequest`: `{ gymCode }` (member identity comes from the JWT, not the body) → `200 OK`
  `QrCheckinResponse`:
  ```json
  { "attendanceId","memberName","memberNameAr","checkInAtUtc","planName","planNameAr",
    "sessionsRemaining?": <int, only meaningful for session_pack plans>,
    "message","messageAr" }
  ```
  or `400` `{ error }` if the "full validation gauntlet" (membership active/not frozen/time
  restriction/sessions remaining) fails, or `401` if unauthenticated, or `429` if rate-limited.
- `ManualCheckinRequest`: `{ memberId, reason: ManualCheckinReason(1=DeadPhone,2=NoAppYet,3=AppIssue,4=Other), notes? }` → `ManualCheckinResponse` (adds `staffName` to the QR response shape).
- Member search (for the manual check-in picker): `MemberSearchRequest` `{ query, includeInactive }`
  → `MemberSearchResult[]`:
  ```json
  { "id","memberNumber","fullName","fullNameAr","phoneNumber","profilePhotoUrl",
    "membershipStatus": "active|expired|frozen|cancelled|none",
    "planName","planNameAr", "isSelectable", "unselectableReason?", "unselectableReasonAr?" }
  ```
  **Only offer selection for `isSelectable: true` rows** — the backend precomputes this (expired,
  frozen, or inactive members are marked unselectable with a bilingual reason) rather than expecting
  the frontend to re-derive eligibility from status fields.
- `TodayAttendanceDto` (live dashboard list — exact GET route wasn't in the excerpt read for
  `AttendanceController`; the DTO exists and its fields are: `id, memberId, memberNumber,
  memberName, memberNameAr, checkInAtUtc, checkOutAtUtc?, entryMethod, planName?` — confirm the exact
  route directly if building this specific dashboard).

### State Management
Live dashboard should merge SignalR `MemberCheckedIn` pushes into local state optimistically, but
reconcile against a periodic re-fetch (the push doesn't guarantee delivery — see §0.9).

### Validation Rules
`ManualCheckinRequest.Notes` is described as relevant specifically "for 'Other' reason" — consider
requiring it client-side when `reason === Other`, though this isn't confirmed as server-enforced.

### Error Handling
QR check-in returns plain `{ error }` on business-rule failure (400) — no machine-readable code
constant exists for check-in failures the way Sales/Shifts do; display the message directly.

### Permissions & Authorization
- QR check-in: member's own JWT only (`AuthenticatedMember` policy) — a member can only check
  themselves in.
- Manual check-in: staff, gated by `checkin.manual` permission per its doc comment.

### Real-time Behavior
`MemberCheckedIn` SignalR event on both QR and manual success (see §0.9) — pushed to
`tenant-{tenantId}` group.

### Integration Notes
The QR rate limiter is IP-based, not per-member/per-device — in a gym with one shared front-desk
tablet or a NAT'd network, many members' independent check-ins share the same limiter bucket.

### Edge Cases
- Rate-limit rejection (`429`) returns the bilingual body configured globally in `Program.cs`
  (`"لقد تجاوزت الحد المسموح به... / Too many requests..."`) — this is a fixed, non-JSON-structured
  string body (raw `application/json` text written directly, not a typed object) — parse
  defensively.
- A member with a frozen or expired membership is rejected by QR check-in with a `400`, not silently
  allowed.

### Acceptance Criteria
- Manual check-in's member search never allows selecting a member the backend marked
  `isSelectable: false`.
- A successful check-in (either path) is reflected live on any open dashboard via SignalR without a
  manual refresh, within the best-effort push's normal latency.

---

## 6. Point of Sale (Sales)

### Purpose
The atomic, idempotent sale endpoint: sells a plan (existing or newly-created member), applies a
promo code and/or manual discount, accepts one or more split payments, and optionally leaves a
partial balance due.

### User Flow
Staff selects a plan → selects existing member or enters new-member details inline → optionally
applies a promo code (validate first) or a manual discount (permission-gated) → enters one or more
payment legs → submits → receives totals + (if any Amount was left unpaid) an implied "debt" state
the Debtors module (§13) will later surface.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| POST | `/api/sales/validate-promo` | `sales.sell` |
| POST | `/api/sales` | `sales.sell` |
| POST | `/api/sales/{id}/payments` | `sales.sell` |

All three are behind `[FeatureFlag("sales")]` — see §0.8.

### Request/Response Contracts
- `POST /api/sales/validate-promo` — `ValidatePromoRequest` `{ code, planId, memberId }` →
  `PromoValidationResult`:
  ```json
  { "isValid", "failureReason?": "<see §0.5 promo codes>",
    "promoCodeId?", "code?", "type?": "percent|fixed",
    "originalPrice?", "discountAmount?", "finalPrice?" }
  ```
  Call this **before** submitting the sale to preview pricing; it does not consume/reserve the promo
  (consumption happens atomically inside `POST /api/sales` itself, so a race between preview and
  submit is possible — see Edge Cases).
- `POST /api/sales` — `CreateSaleRequest`:
  ```json
  {
    "idempotencyKey?": "string, or use X-Idempotency-Key header instead (header wins)",
    "memberId?": "guid — exactly one of memberId/newMember must be set",
    "newMember?": { "fullName","fullNameAr?","phoneNumber","dateOfBirth?" },
    "planId": "guid",
    "promoCode?": "string",
    "manualDiscount?": { "amount", "reason" },
    "payments": [ { "method": "cash|card_paymob|fawry|vodafone|instapay|account_credit", "amount" } ],
    "partialPayment?": { "dueDate": "date-only" }
  }
  ```
  → `200 OK` `SaleResponse`:
  ```json
  { "saleId","membershipId?","isReplay",
    "invoiceStatus": "queued|skipped|not_applicable",
    "totals": { "subtotal","discount","tax","total","paid","amountDue" },
    "warnings": ["string", "..."],
    "receiptUrl?": null }
  ```
  `manualDiscount` requires the caller's JWT to carry `sales.discount.override` — otherwise a `403`
  `FORBIDDEN_DISCOUNT_OVERRIDE`. Presence of `partialPayment` signals intent to leave a balance due;
  omitting it while still underpaying is instead treated as `PAYMENT_INCOMPLETE` (400) — the frontend
  must explicitly opt into partial payment, it isn't inferred from an underpaid `payments` sum alone.
- `POST /api/sales/{id}/payments` — `RecordPaymentRequest` `{ method, amount }` (records a debt
  payment against an existing partially-paid sale) → `SaleResponse` (same shape; `receiptUrl` is
  populated here — see §10 — pointing at the original invoice's receipt view with a `?paymentId=`
  query param).

### State Management
`SaleResponse.invoiceStatus` is `"queued"` for a nonzero-total sale (invoice generation is
asynchronous/background) — do not assume an invoice/PDF exists immediately after the sale response
returns; poll or re-fetch via §10 if you need to display/print it right away, and expect a short
delay.

### Validation Rules
- Exactly one of `memberId` / `newMember` must be set (not enforced as a DTO-level attribute per the
  survey — expect a `400` if both/neither are given, verify exact wording against `ISaleService`
  before hard-coding a client message).
- `payments` must sum correctly relative to the plan's post-discount total: underpay without
  `partialPayment` → `PAYMENT_INCOMPLETE`; overpay → `OVERPAY`.

### Error Handling
See §0.5's Sales code table. Note `OPEN_SHIFT_REQUIRED` → **409** — a cash sale cannot be recorded
without an open shift for the acting staff member (see §8); surface this as "open a shift first,"
ideally by checking shift state proactively before showing the sale screen at all.

### Permissions & Authorization
`sales.sell` for all three endpoints; `sales.discount.override` additionally required to submit a
`manualDiscount`. (`sales.discount.apply` exists in the permission universe but is not referenced by
any endpoint surveyed in this controller — it may gate promo-code application at a service layer not
visible from the controller signature alone; do not assume it's unused without checking
`SaleService` directly if you need to gate promo-code UI specifically.)

### Real-time Behavior
None.

### Integration Notes
- Idempotency (§0.7) makes `POST /api/sales` safe to retry on ambiguous network failures — always
  generate and reuse the same key across retries of the *same* user-initiated submit action.
- `SaleResponse.warnings` is a free-form string array — surface these to staff as non-blocking
  notices (the sale still succeeded), not errors.

### Edge Cases
- **Promo race:** validating a promo code, then submitting the sale, is not atomic — a promo can be
  exhausted (`MAX_USES_REACHED`) between preview and submit under concurrent staff use. The sale
  endpoint itself returns `PROMO_RACE_LOST` in that case — handle it distinctly from a preview-time
  rejection (re-preview or clear the promo field, since the earlier `validate-promo` result is now
  stale).
- A day-pass or trial-type plan sale still goes through this same endpoint (see the Reconciliation
  test fixture's "Sale 5: day-pass" and the Trials feature's own two-step flow in §12 — trials are
  NOT submitted via `POST /api/sales` at all, they use `/api/trials/*` instead).

### Acceptance Criteria
- A resubmitted request with the same idempotency key never creates a second sale/invoice/membership.
- A sale attempted with no open shift is rejected with 409 before any partial state is created.

---

## 7. Promo Codes

### Purpose
Owner/Manager-managed discount codes (`percent` or `fixed`), scoped to specific plans or all plans,
with usage caps.

### User Flow
Manage a list of promo codes → create/edit with type, value, applicable plans, validity window, and
usage caps → deactivate (soft) when retired.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/promo-codes?activeOnly=&validToday=&page=&pageSize=` | `sales.sell` |
| GET | `/api/promo-codes/{id}` | `sales.sell` |
| POST | `/api/promo-codes` | `plans.manage` |
| PUT | `/api/promo-codes/{id}` | `plans.manage` |
| DELETE | `/api/promo-codes/{id}` | `plans.manage` |

Note the read/write permission split here mirrors Members (view vs. manage), unlike Plans (§3) which
uses one permission for everything.

### Request/Response Contracts
- `GET /api/promo-codes` → paged `PromoCodeDto[]`:
  ```json
  { "id","code","type": "percent|fixed","value","appliesTo?": ["planId", "..."] or null-for-all-plans,
    "validFrom","validTo","maxUses?","maxUsesPerMember?","usesCount","minPrice?","isActive",
    "createdAtUtc","updatedAtUtc?" }
  ```
- `CreatePromoCodeRequest`/`UpdatePromoCodeRequest` (same shape): `{ code, type, value, appliesTo?, validFrom, validTo, maxUses?, maxUsesPerMember?, minPrice? }`. `code` is stored **uppercased regardless of input case** — display it uppercased to match what's stored, and don't rely on case-sensitive uniqueness checks client-side.
- `DELETE` is a **soft deactivate** (`IsActive = false`), not a hard delete — reflect this in the UI
  label/confirmation copy ("deactivate" rather than "delete permanently").

### State Management
Re-fetch the list after create/update/deactivate; `usesCount` is server-computed and will drift from
any locally cached value as sales consume the code.

### Validation Rules
No FluentValidation validator was found in this survey for these two request DTOs specifically —
confirm `validFrom <= validTo` and `value` sign/range client-side defensively, but treat the backend
as authoritative.

### Error Handling
Ad-hoc `{ error, message }` / plain `Problem(detail:, statusCode: 400)` — no dedicated ProblemDetails
`title` code constants exist for CRUD-level failures on this controller (contrast with the actual
*validation-at-sale-time* codes in §0.5's promo table, which come from a different method,
`ValidateAndPriceAsync`, not this CRUD controller).

### Permissions & Authorization
See table — note `sales.sell` (not `sales.discount.apply`) gates the two GET endpoints.

### Real-time Behavior
None.

### Integration Notes
This controller's data is what `POST /api/sales/validate-promo` (§6) validates against — a promo
created/edited here takes effect immediately for the next sale attempt (no cache invalidation delay
documented).

### Edge Cases
`appliesTo: null` (or omitted) means the code applies to **all plans** — do not render this as "no
plans selected" in an editing UI; render it as an explicit "all plans" state distinct from an empty
selection.

### Acceptance Criteria
- A deactivated promo code immediately fails validation on the next `validate-promo`/sale attempt
  (`CODE_INACTIVE`).

---

## 8. Cash Drawer / Shifts

### Purpose
Blind-count cash-drawer session tracking: open with a float, accumulate cash movements (sales,
refunds, manual paid-in/paid-out, float adjustments), close with a physical count, and reconcile any
variance.

### User Flow
Staff opens a shift with an opening float at the start of a work session → the system silently
accrues cash movements as sales/refunds happen → at end of session, staff enters their physical cash
count (without seeing the expected total — "blind count") → the system reveals variance → if beyond
tolerance, a manager approval step follows (or a manager can force-close).

### Backend APIs Used
| Method | Endpoint | Permission/Policy |
|---|---|---|
| POST | `/api/shifts/open` | `shift.open` |
| GET | `/api/shifts/current` | `shift.open` |
| POST | `/api/shifts/current/close` | `shift.close` |
| POST | `/api/shifts/current/movements` | `shift.open` (base), `shift.reconcile.approve` needed only for a `paid_out` exceeding the tenant's approval threshold |
| POST | `/api/shifts/{id}/approve` | `shift.reconcile.approve` |
| POST | `/api/shifts/{id}/force-close` | `ManagerOrAbove` |
| GET | `/api/shifts?from=&to=&userId=&page=&pageSize=` | `ManagerOrAbove` |
| GET | `/api/shifts/open-summary` | `reports.financial.view` |

All behind `[FeatureFlag("shifts")]`.

### Request/Response Contracts
- `OpenShiftRequest`: `{ openingFloat }` → `ShiftDto`. **409** `SHIFT_ALREADY_OPEN` if the caller
  already has one open.
- `GET .../current` → `ShiftDto` **with `expectedCash` always null** (blind count — the API never
  reveals the expected total before close, by design; do not attempt to compute/display an expected
  total client-side from movements either, since that defeats the blind-count control).
  `ShiftDto`:
  ```json
  { "id","userId","userName?","openedAt","closedAt?","openingFloat","expectedCash?",
    "countedCash?","variance?","varianceNote?","approvedByUserId?",
    "status": "open|closed|approved",
    "movements": [ { "id","type": "sale|refund|paid_in|paid_out|float_adjust","amount","referenceId?","reason?","createdByUserId","createdAtUtc" } ] }
  ```
- `CloseShiftRequest`: `{ countedCash, varianceNote? }` → `ShiftDto`, now with `expectedCash`
  populated (`OpeningFloat + Σ movement amounts`) and `variance = countedCash - expectedCash`.
- `RecordMovementRequest`: `{ type: "paid_in"|"paid_out"|"float_adjust", amount, referenceId?, reason? }`.
  **Submit `amount` as a positive magnitude for `paid_in`/`paid_out`** — the backend normalizes the
  sign itself (`paid_in` → stored positive, `paid_out` → stored negative). For `float_adjust`, **the
  caller's sign is used as-is** — send a negative value to decrease the float, positive to increase
  it. (`sale`/`refund` movement types are recorded internally by other services, not via this
  endpoint — submitting them here would fail type validation.) Returns `CashMovementDto`.
  - `paid_out` beyond the tenant's configured approval threshold (`Tenant.Settings`'s
    `paid_out_approval_threshold_egp`, default unlimited/no threshold) requires the caller to also
    carry `shift.reconcile.approve` — otherwise `403` `MANAGER_APPROVAL_REQUIRED`. There's no
    separate "request approval" sub-flow here — the same-permission user must simply re-attempt with
    sufficient privilege, or a privileged user performs the paid_out themselves.
- `ApproveShiftRequest`: `{ note? }` → `ShiftDto` (moves `status` from `closed` → `approved`).
  **409** `NOT_AWAITING_APPROVAL` if the shift isn't in `closed` state.
- `ForceClose`: no body → `ShiftDto`. Manager-only escape hatch (e.g. a forgotten open shift from a
  no-show staff member).
- `GET /api/shifts`: paged `ShiftDto[]` filtered by optional `from`/`to`/`userId`.
- `GET /api/shifts/open-summary` → `ShiftOpenSummaryDto`: `{ openShifts: ShiftDto[], totalCashInDrawers }`.

### State Management
Poll or re-fetch `GET .../current` at the start of any screen that needs to know "is a shift open" —
there is no push notification for shift state changes.

### Validation Rules
`RecordMovementRequest.type` must be exactly `paid_in`, `paid_out`, or `float_adjust` — anything else
(including `sale`/`refund`) is rejected with `INVALID_MOVEMENT_TYPE`.

### Error Handling
See §0.5's Shifts code table — note three distinct 409s (`SHIFT_ALREADY_OPEN`, `NO_OPEN_SHIFT`,
`NOT_AWAITING_APPROVAL`, `SHIFT_NOT_OPEN`) that should each produce a distinct user-facing message
rather than a generic "conflict."

### Permissions & Authorization
Mixed permission/policy model — see table; `paid_out`'s threshold-based extra check is the only
*conditional* permission requirement in this entire API (every other endpoint's permission
requirement is static per-route).

### Real-time Behavior
None.

### Integration Notes
Sales (§6) and Refunds (§9) both write `sale`/`refund`-typed movements into the currently open shift
as a side effect — a shift's movement list will show entries this controller itself never directly
created.

### Edge Cases
- A cash sale or cash refund with **no open shift** fails at the Sales/Refunds layer with
  `OPEN_SHIFT_REQUIRED` (409) — not at this controller — reinforcing that shift state should be
  checked before allowing entry to the sale/refund screens at all, not only when those specific
  actions are attempted.
- `expectedCash` is genuinely `null` (not just hidden) until close — don't treat a null there as "not
  yet loaded," it's the intended blind-count value.

### Acceptance Criteria
- The physical count entry UI never has access to (or can derive) the expected total before
  submission.
- A `paid_out` above threshold is blocked for a non-approving user and a clear reason is shown, not a
  generic 403.

---

## 9. Refunds & Account Credit

### Purpose
Two-step refund workflow (request → approve, which executes immediately) against a completed sale,
with three refund methods: `cash` (drawer movement), `gateway` (Paymob/Fawry reversal — currently
unsupported, see below), `credit` (adds to the member's account-credit ledger instead of returning
money).

### User Flow
Staff selects a sale needing a refund → requests a refund with amount/method/reason → a
different staff member with approval permission approves (or rejects) → on approval, the refund
executes immediately (cash movement, credit ledger entry, or — for `gateway` — currently always
fails).

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| POST | `/api/refunds` | `payments.refund.request` |
| POST | `/api/refunds/{id}/approve` | `payments.refund.approve` |
| POST | `/api/refunds/{id}/reject` | `payments.refund.approve` |
| GET | `/api/refunds?saleId=&memberId=&status=` | `payments.refund.approve` |

Behind `[FeatureFlag("refunds")]`.

### Request/Response Contracts
- `RequestRefundRequest`: `{ saleId, amount, method: "cash"|"gateway"|"credit", reason }` →
  `RefundDto`:
  ```json
  { "id","saleId","paymentTransactionId?","amount","method","reason","requestedByUserId",
    "approvedByUserId?","status": "requested|approved|executed|rejected",
    "rejectionNote?","creditNoteInvoiceId?","executedAt?","createdAtUtc" }
  ```
  **409** `SALE_FULLY_REFUNDED` if the sale has no refundable remainder; `400`
  `REFUND_EXCEEDS_REMAINDER` if the requested amount exceeds what's left refundable.
- `POST .../approve`: no body → `RefundDto` with `status: "executed"`, `executedAt` set. **403**
  `SELF_APPROVAL_FORBIDDEN` if the approver is the same user who requested it (Owner role is exempt
  from this restriction — see cerebrum-documented backend behavior: Owner can self-approve). **409**
  `OPEN_SHIFT_REQUIRED` for a `cash` method refund with no open shift. **409**
  `GATEWAY_REFUND_UNSUPPORTED` — **`gateway`-method refunds are not currently executable at all**;
  do not offer `gateway` as a method in the UI unless/until this is implemented, or clearly mark it
  as "not yet supported" if you must show it for completeness.
  - `creditNoteInvoiceId` is populated for `cash`/`gateway` refunds (a legal credit-note document is
    issued) but **stays null for `credit`-method refunds** — a credit-method refund is a store-credit
    liability, not a revenue reversal, so it deliberately produces no credit-note document. Don't
    treat a null `creditNoteInvoiceId` as a bug for credit-method refunds.
- `RejectRefundRequest`: `{ note }` → `RefundDto` with `status: "rejected"`, `rejectionNote` set.
- `GET /api/refunds`: **flat array, not paged** — filterable by `saleId`/`memberId`/`status`, all
  optional.
- Member credit balance/ledger is read via `GET /api/members/{id}/credits` (§2), not this controller.

### State Management
After approval/rejection, re-fetch the sale (its `AmountDue`/status may have changed) and, for
`credit`-method approvals, the member's credit balance (§2).

### Validation Rules
`amount` must not exceed the sale's refundable remainder (`REFUND_EXCEEDS_REMAINDER`); the sale must
not already be fully refunded (`SALE_FULLY_REFUNDED`).

### Error Handling
See §0.5's Refunds code table.

### Permissions & Authorization
Request and approve/reject use **different** permissions (`payments.refund.request` vs.
`payments.refund.approve`) — enforcing separation of duties structurally; a single user with only
`request` cannot also approve their own refund even before the self-approval-forbidden business rule
kicks in (unless they also separately hold the approve permission, in which case the self-approval
rule is the actual guard, except for Owner).

### Real-time Behavior
None.

### Integration Notes
A `credit`-method refund increases the member's `member_credits` ledger balance
(`GET /api/members/{id}/credits`), which can later be spent via `SalePaymentRequest.method =
"account_credit"` on a future sale (§6) — make sure any "apply store credit" UI on the sale screen
reads this same balance endpoint.

### Edge Cases
- Two concurrent refund-approval attempts against the same refund: the second correctly fails with
  `NOT_AWAITING_APPROVAL` (409) once the first has executed — don't allow a UI to show "approve"
  as still actionable after a concurrent approval elsewhere without re-fetching first.
- `gateway` method is a real, listed option in `RequestRefundRequest`'s accepted values but is
  guaranteed to fail at approval time in the current backend — this is a genuine gap, not a
  documentation oversight; do not silently drop it from request-time UI without accounting for how
  gateway refunds are meant to actually happen operationally (currently: apparently not, via this
  API) until a backend change lands.

### Acceptance Criteria
- Self-approval is blocked for non-Owner roles with a distinct 403 message.
- A `credit`-method refund is reflected in the member's credit balance immediately after approval.

---

## 10. Invoices & Receipts

### Purpose
Read-only access to the legal invoice/credit-note trail generated (asynchronously) by Sales and
Refunds, plus void and resend-delivery actions, and an 80mm-thermal-printer-ready HTML receipt view.

### User Flow
Staff browses/filters invoices (by date range, member, status, type) → opens one to view/print/resend
→ Manager+ voids an erroneously-issued invoice with a reason.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/invoices?from=&to=&memberId=&status=&type=&page=&pageSize=` | `reports.financial.view` |
| GET | `/api/invoices/{id}` | `reports.financial.view` |
| POST | `/api/invoices/{id}/void` | `payments.refund.approve` |
| POST | `/api/invoices/{id}/resend` | `sales.sell` |
| GET | `/api/invoices/{id}/receipt-html?paymentId=` | `sales.sell` |

### Request/Response Contracts
- `GET /api/invoices` — `InvoiceQueryRequest` query params `{ from?, to?, memberId?, status?:
  "issued"|"voided", type?: "invoice"|"credit_note", page, pageSize }` → paged `InvoiceDto[]`.
- `InvoiceDto`:
  ```json
  { "id","type": "invoice|credit_note","invoiceNumber": "INV-YYYY-NNNNNN or CN-YYYY-NNNNNN",
    "saleId?","originalInvoiceId?","memberNameSnapshot","memberPhoneSnapshot",
    "lines": [ { "description","descriptionAr?","qty","unitPrice","lineTotal" } ],
    "subtotal","discountAmount","vatRate","vatAmount","total","currency": "EGP",
    "issuedAt","pdfUrl?","status": "issued|voided","voidReason?" }
  ```
  Member/line details are **snapshotted at issue time** — a later edit to the member's name/phone or
  the sale's lines will never retroactively change an already-issued invoice's displayed values.
- `VoidInvoiceRequest`: `{ reason }` → `{ message }`.
- `POST .../resend`: no body → `{ message }` — re-enqueues an async delivery job (WhatsApp/email,
  per whatever `IInvoiceService.ResendAsync` actually dispatches); does not return the invoice itself.
- `GET .../receipt-html?paymentId=`: returns **raw `text/html`** (not JSON) — a self-contained 80mm
  thermal-receipt document with inline print CSS, no external resources. Render this directly in a
  webview/iframe or send straight to a print dialog; don't attempt to parse it as structured data.
  Supplying `paymentId` (from a debt-payment's `SaleResponse.receiptUrl`, see §6) appends a "Payment
  Received" section for that specific payment **without generating a new invoice number** — this is
  the correct way to print a receipt for an individual debt payment against an already-invoiced sale.

### State Management
Invoice list should be re-fetched after any Sales/Refunds action that generates new invoices/credit
notes — there's an inherent async delay (§6) between the originating action and the invoice actually
existing.

### Validation Rules
None beyond the query filter shapes.

### Error Handling
Ad-hoc `Problem(detail:, statusCode: 400)` / `NotFound` — no dedicated code-constant table for this
controller specifically.

### Permissions & Authorization
Note void requires `payments.refund.approve` (not a dedicated invoice permission) and resend/receipt
require `sales.sell` — there's no standalone "invoices.manage"-style permission.

### Real-time Behavior
None — invoice creation is background/async with no push notification; the frontend must poll or
simply re-fetch after a reasonable delay following the originating sale/refund.

### Integration Notes
`invoiceNumber` format (`INV-YYYY-NNNNNN` / `CN-YYYY-NNNNNN`) is gap-free and sequential per
tenant/year/type — safe to display as the canonical legal reference; never construct or guess one
client-side.

### Edge Cases
- `pdfUrl` may be null even for an `issued` invoice if PDF rendering hasn't completed yet
  (asynchronous, same caveat as invoice creation itself) — don't treat a null `pdfUrl` as an error
  state, just as "not ready yet."
- A `credit_note`'s `total` is stored **positive** (a fixed bug from a prior hardening pass) — do not
  render it with a negative sign or subtract it manually; treat `type` as the signal for how it nets
  against invoices, not the sign of `total`.

### Acceptance Criteria
- The receipt-html view renders correctly as a standalone print target with no additional network
  fetches.
- Voiding an invoice is only offered to users holding `payments.refund.approve`.

---

## 11. Daily Z-Report

### Purpose
Immutable end-of-day closing snapshot per tenant: payment-method totals, revenue-by-line-type,
discounts, refunds, and per-shift rows for a given calendar date.

### User Flow
Manager+ opens the Z-Report for a given date (typically "yesterday" or "today so far") → views/prints
the PDF → if something changed after generation (rare), a Manager can force regeneration.

### Backend APIs Used
| Method | Endpoint | Permission/Policy |
|---|---|---|
| GET | `/api/reports/z/{date}` | `reports.financial.view` |
| GET | `/api/reports/z/{date}/pdf` | `reports.financial.view` |
| POST | `/api/reports/z/{date}/regenerate` | `ManagerOrAbove` |

`{date}` is a `DateOnly` route segment (`yyyy-MM-dd`).

### Request/Response Contracts
- `GET /api/reports/z/{date}` → `ZReportDto`:
  ```json
  { "id","tenantId","reportDate","pdfUrl?","generatedAt","generatedByUserId?",
    "methodTotals": [ { "method","count","total" } ],
    "lineTypeTotals": [ { "lineType": "membership|trial|day_pass|retail|fee","count","revenue" } ],
    "promoDiscountTotal","manualDiscountTotal","manualDiscountCount",
    "refundsTotal",
    "shifts": [ { "userId","userName","openedAt","closedAt?","openingFloat","expectedCash?","countedCash?","variance?","status" } ],
    "outstandingAddedToday","membershipRevenueToday" }
  ```
  **404** `ZREPORT_NOT_FOUND` if no report exists yet for that date (e.g. today's report, which
  generates via a scheduled job at 23:59 Cairo time — see `LAUNCH_CHECKLIST.md`'s job list — won't
  exist until then; don't assume every date is queryable on demand).
- `GET .../pdf` → binary `application/pdf` (`Content-Disposition`-style filename
  `z-report-{date}.pdf` via the `File()` result) — same 404 behavior.
- `POST .../regenerate` → `ZReportDto` (recomputed).

### State Management
Treat a fetched `ZReportDto` as an immutable snapshot for that date — don't assume it updates live;
only `regenerate` changes it.

### Validation Rules
None beyond the date format.

### Error Handling
`ZREPORT_NOT_FOUND` (404) is the only documented code for this controller.

### Permissions & Authorization
Regeneration is a distinctly higher bar (`ManagerOrAbove` role policy) than viewing
(`reports.financial.view` permission).

### Real-time Behavior
None — this is a generated-once-per-day artifact, not a live view.

### Integration Notes
The report is generated by a Hangfire recurring job at 23:59 Cairo time daily (see
`LAUNCH_CHECKLIST.md`) — a same-day "Z-report so far" view is **not** what this endpoint provides;
it's strictly the finalized end-of-day closing for a completed business day.

### Edge Cases
Requesting a future date, or a date before the tenant existed, both presumably 404 the same way as
"not generated yet" — there's no distinct "date out of range" vs. "not generated yet" code, treat
both as "not available."

### Acceptance Criteria
- A 404 for an ungenerated date shows a clear "not yet available" state rather than a generic error.

---

## 12. Free Trials

### Purpose
Staff-initiated, phone-OTP-verified two-step free-trial membership issuance (distinct from a paid
Sales-flow purchase).

### User Flow
Staff enters a prospective member's name/phone and selects a trial plan → system sends an OTP to the
phone → staff (or the prospect, depending on operational convention) enters the OTP → trial
membership is created and the (possibly new) member record confirmed.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| POST | `/api/trials/initiate` | `sales.sell` |
| POST | `/api/trials/confirm` | `sales.sell` |

Behind `[FeatureFlag("trials")]`.

### Request/Response Contracts
- `TrialInitiateRequest`: `{ fullName, fullNameAr?, phoneNumber, planId }` → `TrialInitiateResponse`:
  `{ otpSent: true, expiresInSeconds }`. **409** `TRIAL_ALREADY_USED` if this phone number has already
  had a trial (one trial per member, enforced by phone). **404** `PLAN_NOT_FOUND` if `planId` doesn't
  resolve to a `trial`-type plan (`PLAN_NOT_TRIAL` for a non-trial plan id).
- `TrialConfirmRequest`: `{ phoneNumber, otp }` → `TrialConfirmResponse`:
  ```json
  { "member": <MemberDetailDto, §2>, "membership": <MembershipSummaryDto, §2> }
  ```
  **404** `PENDING_TRIAL_NOT_FOUND` if no matching initiated-but-unconfirmed trial exists for that
  phone (e.g. OTP expired and was never re-initiated); `400` `OTP_INVALID` for a wrong/expired code.

### State Management
On successful confirm, the returned `member`/`membership` can seed a freshly-created member profile
view directly — no need to re-fetch `GET /api/members/{id}` immediately after.

### Validation Rules
Trial eligibility is keyed by phone number, not by an existing member id — a returning phone number
that already used a trial is rejected even if no member record superficially looks like a duplicate.

### Error Handling
See §0.5's Trials code table.

### Permissions & Authorization
Both steps require `sales.sell` — same permission as the general POS flow, not a distinct
trials-specific permission.

### Real-time Behavior
None.

### Integration Notes
This is entirely separate from `POST /api/sales` (§6) — a trial plan (`planType: "trial"`, `price:
0`) is issued exclusively through this two-endpoint flow, never through the general sale endpoint,
despite trial plans being visible/creatable through the regular Plans CRUD (§3).

### Edge Cases
A trial plan's `durationDays` and/or `trialVisitLimit` (§3) govern when it actually expires/exhausts
— this controller doesn't surface remaining-trial-days directly; use the returned `membership.endDate`/
`sessionsRemaining` from the confirm response, or the member's current-membership endpoint (§2) later.

### Acceptance Criteria
- Re-initiating a trial for an already-trialed phone number is rejected before any OTP is sent.
- A confirmed trial's `membership.planType` reads `"trial"` and its `amountPaid` is `0`.

---

## 13. Debtors (Outstanding Balances)

### Purpose
Front-desk operational list of members with an outstanding balance (from partial-pay sales, §6),
plus a throttled WhatsApp payment-reminder action and CSV export.

### User Flow
Front desk opens the debtors list (paged, or exports full CSV) → optionally reviews a summary KPI
(total outstanding, debtor count) → sends a reminder to a specific debtor (throttled to avoid spam).

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/debtors?page=&pageSize=&format=csv` | `sales.sell` |
| GET | `/api/debtors/summary` | `reports.financial.view` |
| POST | `/api/debtors/{memberId}/remind` | `sales.sell` |

Behind `[FeatureFlag("debtors")]`.

### Request/Response Contracts
- `GET /api/debtors` (default, JSON): paged `DebtorDto[]`:
  ```json
  { "memberId","fullName","phoneNumber","totalDue","oldestDueDate",
    "agingBucket": "0-7|8-30|30+", "lastPaymentAt?" }
  ```
- `GET /api/debtors?format=csv`: returns `text/csv` binary (`debtors.csv`) with **all** debtors
  (unpaged) — columns: `MemberId,FullName,PhoneNumber,TotalDue,OldestDueDate,AgingBucket,LastPaymentAt`.
  Use this only for an explicit "export" action, not as a data source for an in-app paged table.
- `GET /api/debtors/summary` → `DebtorsSummaryDto`: `{ totalOutstanding, debtorCount }`.
- `POST /api/debtors/{memberId}/remind`: no body → `{ message }`. **429** `REMINDER_THROTTLE` if
  reminded too recently (Redis-backed throttle — exact window isn't in the DTOs surveyed; treat any
  429 here as "already reminded recently, try later" without hard-coding a specific retry time
  unless confirmed against `IDebtorsService`). **404** `NO_OUTSTANDING_BALANCE` if the member has
  nothing due (stale UI state) or `MEMBER_NOT_FOUND`.

### State Management
`totalDue`/`agingBucket` are server-computed aggregates over all of a member's partially-paid sales —
re-fetch after any debt payment (§6's `POST /api/sales/{id}/payments`) rather than trying to
locally decrement.

### Validation Rules
None beyond the route/query shapes.

### Error Handling
See §0.5's Debtors code table — note `429` specifically for the throttle, distinct from the `400`
default.

### Permissions & Authorization
Summary requires the higher `reports.financial.view` permission while the list/remind use the more
general `sales.sell` — a front-desk role with just `sales.sell` can work the debtor list but not see
the aggregate KPI card unless also granted `reports.financial.view`.

### Real-time Behavior
None.

### Integration Notes
The reminder message itself is a WhatsApp template (`payment_reminder`) sent via the tenant's
configured WhatsApp provider — see `LAUNCH_CHECKLIST.md` for the template-approval requirement; a
misconfigured/unapproved template will silently no-op server-side rather than erroring back to this
endpoint (per that document's WhatsApp section), so a `200`/`{message}` response does not strictly
guarantee delivery.

### Edge Cases
`agingBucket` is a coarse 3-value bucket, not a raw day count — don't attempt to sort/filter more
granularly client-side than this without also fetching `oldestDueDate` and computing yourself (which
is fine — the raw date is available).

### Acceptance Criteria
- A reminder attempted twice in quick succession for the same member is throttled, not sent twice.

---

## 14. Renewal Call Sheet

### Purpose
Front-desk operational list of memberships expiring soon (or recently expired), a call-outcome
logging action, and a per-staff renewal-rate report.

### User Flow
Front desk opens the "expiring soon" list → calls each member → logs the outcome
(contacted/renewed/declined/no_answer) → later, a manager reviews renewal-rate-by-staff over a date
range.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/call-sheet/expiring?days=7` | `sales.sell` |
| POST | `/api/call-sheet/{membershipId}/outcome` | `sales.sell` |
| GET | `/api/call-sheet/renewal-rate?from=&to=&staffUserId=` | `reports.financial.view` |

This controller is **not** behind a `[FeatureFlag]` attribute — unlike Debtors/Refunds/etc., it has
no corresponding entry in the feature-flag list (§0.8); it cannot be disabled per-tenant via that
mechanism.

### Request/Response Contracts
- `GET .../expiring?days=7` (default 7) → flat `CallSheetEntryDto[]`:
  ```json
  { "membershipId","memberId","fullName","phoneNumber","planName","endDate","lastVisitAt?",
    "lastCallOutcome?": "contacted|renewed|declined|no_answer" }
  ```
- `RecordCallOutcomeRequest`: `{ outcome: "contacted"|"renewed"|"declined"|"no_answer", note? }` →
  `{ message }`. **404** `MEMBERSHIP_NOT_FOUND`; `400` `INVALID_OUTCOME` for any value outside the
  four listed.
- `GET .../renewal-rate?from=&to=&staffUserId=` (from/to required `DateOnly`, `staffUserId` optional
  — the **domain `AppUser.Id`**, not the JWT `sub`/Identity id, see Integration Notes) → array of
  `RenewalRateDto` (one row per staff member if `staffUserId` omitted, or one row if supplied):
  ```json
  { "staffUserId","staffName","totalCalled","renewed","renewalRatePercent" }
  ```

### State Management
Re-fetch the expiring list after logging an outcome — `lastCallOutcome` on the entry will change.

### Validation Rules
`outcome` is a closed four-value enumeration (`INVALID_OUTCOME` otherwise).

### Error Handling
See §0.5's Call Sheet code table.

### Permissions & Authorization
Same split pattern as Debtors — reporting (`renewal-rate`) needs `reports.financial.view`, the
operational actions need only `sales.sell`.

### Real-time Behavior
None.

### Integration Notes
`renewal-rate`'s `staffUserId` query parameter is the **domain `AppUser.Id`** (the same id
`CallOutcome.UserId` stores when an outcome is recorded), not the ASP.NET Identity/JWT subject id —
if you need "my own renewal rate" for the logged-in staff member, resolve their `AppUser.Id` first
(e.g. from a staff-detail lookup) rather than passing the JWT `sub` claim directly.

### Edge Cases
`days` on the expiring-list query accepts any int (no documented upper bound enforced) — a very large
value will simply return a larger window; there's no pagination on this endpoint, so an unbounded
`days` value on a large member base could return a large flat array.

### Acceptance Criteria
- Logging an invalid outcome value is rejected client-side before submit (closed set), and
  server-side regardless.

---

## 15. Bulk Import (Members + Current Memberships)

### Purpose
Excel/CSV bulk-import pipeline for onboarding a gym's existing member base: upload, auto-detected
column mapping (with manual override), dry-run validation, execution, and a time-boxed rollback.

### User Flow
Owner/Manager downloads the template → fills it with existing member/membership data → uploads →
reviews (and corrects, if needed) the auto-detected column mapping → the system dry-run validates
every row → reviews per-row errors (downloadable as CSV) → executes the import → if something's
wrong, rolls back within the allowed window; if some referenced plan names don't match an existing
plan, creates those plans first, then re-attempts.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/imports/template.xlsx` | `settings.manage` |
| POST | `/api/imports` (multipart) | `settings.manage` |
| POST | `/api/imports/{id}/mapping` | `settings.manage` |
| GET | `/api/imports/{id}` | `settings.manage` |
| GET | `/api/imports/{id}/errors.csv` | `settings.manage` |
| POST | `/api/imports/{id}/execute` | `settings.manage` |
| POST | `/api/imports/{id}/create-plans` | `plans.manage` |
| POST | `/api/imports/{id}/rollback` | `ManagerOrAbove` |

Behind `[FeatureFlag("imports")]`. Upload is capped at **5 MB** (`RequestSizeLimit`) and (per the
`ImportFailureReasons.TooManyRows` code) some maximum row count — the controller's own doc comment
states **≤10,000 rows**.

### Request/Response Contracts
- `GET .../template.xlsx` → binary `.xlsx` download (`import-template.xlsx`), no auth-gated content
  beyond the permission check itself.
- `POST /api/imports` — `multipart/form-data`, field `file` → `ImportBatchDto`:
  ```json
  { "id","fileName","status": "validating|dry_run_ready|importing|completed|rolled_back|failed",
    "totalRows","okRows","errorRows",
    "mapping": { "sourceHeader": "targetField", "...": "..." },
    "completedAt?","createdAtUtc" }
  ```
  `400` `FILE_TOO_LARGE` / `TOO_MANY_ROWS` / `UNSUPPORTED_FILE_TYPE` on rejected uploads.
- `ColumnMapRequest`: `{ mapping: { "<source header>": "fullName|phoneNumber|planName|startDate|endDate|sessionsRemaining|dateOfBirth" } }` → `ImportBatchDto` (re-validates with the corrected mapping).
- `GET /api/imports/{id}` → `ImportBatchDto` (poll this to observe `status` transitions after
  execute, since execution is asynchronous/enqueued — the `POST /execute` response itself is just an
  ack, not the final result).
- `GET .../errors.csv` → `text/csv` (`import-errors.csv`) — per-row error detail; codes drawn from
  `ImportRowErrorCodes` (§0.5's table): `PHONE_INVALID`, `PHONE_DUP_FILE`, `PHONE_EXISTS`,
  `PLAN_UNMATCHED`, `DATE_RANGE_INVALID`, `RETAINED_HAS_ACTIVITY`, `ROLLED_BACK` (comma-joined per
  row if multiple apply).
- `POST .../execute`: no body → `{ message: "Import execution started / ..." }` (ack only — poll
  `GET .../{id}` for actual completion/`status`). Idempotent against being called twice for the same
  batch (a resumed/re-triggered execute skips rows already `imported`, per this codebase's documented
  idempotency sweep — no duplicate members are created).
- `CreatePlansFromImportRequest`: `{ plans: [ { name, planType, durationDays, price } ] }` → creates
  the missing plans referenced by unmatched rows, then returns the updated `ImportBatchDto` — re-run
  mapping/validation afterward to pick up the now-resolvable plan references.
- `POST .../rollback`: no body → `ImportBatchDto` with `status: "rolled_back"`. **400**
  `ROLLBACK_WINDOW_EXPIRED` past the allowed window — per the controller's doc comment, **7 days**
  after completion.

### State Management
Treat `ImportBatchDto.status` as the single source of truth for which step's UI to show (upload →
mapping → dry-run review → execute → done/rolled-back) — don't infer step from local wizard state
alone, since execution is async and can complete or fail independently of the client's current screen.

### Validation Rules
`ColumnMapRequest.mapping` target-field values are a closed set: `fullName`, `phoneNumber`,
`planName`, `startDate`, `endDate`, `sessionsRemaining`, `dateOfBirth`.

### Error Handling
See §0.5's Imports + per-row error-code tables.

### Permissions & Authorization
Most steps need `settings.manage`; creating missing plans specifically needs `plans.manage`
(consistent with §3); rollback needs the `ManagerOrAbove` role policy, a stricter bar than the
`settings.manage` permission used for everything else in this flow.

### Real-time Behavior
None — poll `GET /api/imports/{id}` for status after `execute`.

### Integration Notes
`ImportBatchDto.mapping` reflects whatever was auto-detected OR last manually corrected — always
render the current server-side mapping back to the user for confirmation rather than assuming your
own last-submitted mapping is still what's active (a `create-plans` call, for instance, doesn't touch
mapping, but re-validation flows might refine auto-detection further).

### Edge Cases
- Calling `execute` twice (e.g. due to a UI double-click or a retried network call) is safe — already
  `imported` rows are skipped, no duplicate members result.
- Rollback past the 7-day window is permanently blocked — there's no override/force option exposed by
  this API.

### Acceptance Criteria
- The mapping step always reflects the batch's current server-side `mapping`, not a client-cached
  guess.
- A rollback attempt outside the window shows a clear "window expired" message, not a generic error.

---

## 16. Analytics Dashboards

### Purpose
Pre-aggregated (snapshot-based, not real-time-computed) KPI dashboards for owner/manager oversight.

### User Flow
Owner/Manager opens a dashboard → views overview KPIs, a revenue trend chart, an attendance heatmap,
member-status breakdown, invitation funnel, and (for a selected month) the trial conversion funnel.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/analytics/overview` | `reports.financial.view` |
| GET | `/api/analytics/revenue?months=6` | `reports.financial.view` |
| GET | `/api/analytics/heatmap` | `members.view` |
| GET | `/api/analytics/members-status` | `members.view` |
| GET | `/api/analytics/invitations` | `members.view` |
| GET | `/api/analytics/trials?month=yyyy-MM` | `reports.financial.view` |

### Request/Response Contracts
- `GET .../overview` → `DashboardOverviewDto`:
  ```json
  { "activeMembers","expiredMembers","newMembersThisMonth","revenueThisMonth",
    "checkinsToday","checkinsThisWeek","snapshotTimeUtc" }
  ```
  **This is a pre-computed snapshot** (`snapshotTimeUtc` tells you how stale it is) — do not expect
  it to reflect a sale/check-in that happened seconds ago; display `snapshotTimeUtc` (e.g. "as of
  HH:mm") so users understand the latency rather than assuming a bug when numbers lag.
- `GET .../revenue?months=6` (1–36, else `400`) → `RevenueChartDto`: `{ labels: ["Jan","Feb",...], values: [decimal, ...] }` — parallel arrays, not an array of objects.
- `GET .../heatmap` → `AttendanceHeatmapDto`: `{ data: number[7][24] }` — `data[0][0]` = Monday
  00:00–01:00 ... `data[6][23]` = Sunday 23:00–00:00 (fixed Mon-first week layout, not
  locale-adjustable from this endpoint).
- `GET .../members-status` → `MemberStatusPieDto`: `{ active, expired, frozen, cancelled, total:
  <computed sum> }`.
- `GET .../invitations` → `InvitationFunnelDto`: `{ sent, visited, converted, conversionRate:
  <0-100> }`.
- `GET .../trials?month=yyyy-MM` (required, else `400`) → `TrialAnalyticsDto`: `{ issued, converted,
  conversionRate, expired }` — this is a cohort view: `issued` counts trials **started** in that
  month, while `converted`/`expired` reflect each trial member's **current** outcome (which may have
  been reached after the cohort month ended) — don't assume the four numbers are mutually exclusive
  snapshots of the same instant.

### State Management
Cache per dashboard load; these are explicitly snapshot-based so aggressive re-fetching gains
nothing between snapshot refreshes (frequency not specified in the DTOs — treat `snapshotTimeUtc` as
the freshness signal rather than assuming any particular interval).

### Validation Rules
`months` clamped 1–36 client-side to avoid a round-trip `400`; `month` (trials) is required
`yyyy-MM`.

### Error Handling
Plain `BadRequest(result.Error)` — no ProblemDetails/code table for this controller.

### Permissions & Authorization
Split between `reports.financial.view` (revenue-adjacent) and `members.view` (member-count-adjacent)
— a Trainer with only `members.view` can see heatmap/member-status/invitations but not
overview/revenue/trials.

### Real-time Behavior
None (snapshot-based by design).

### Integration Notes
`RevenueChartDto`'s parallel-array shape (`labels`/`values`) rather than a list of `{label, value}`
objects is a deliberate charting-library-friendly shape already — don't re-zip and re-split it
unnecessarily.

### Edge Cases
A tenant with no data yet returns zeros/empty arrays, not an error — build empty-state UI around
"all zero," not around a failed request.

### Acceptance Criteria
- The overview dashboard visibly communicates its snapshot recency (`snapshotTimeUtc`) rather than
  implying live data.

---

## 17. Detailed Reports

### Purpose
Real-time (not snapshot-based, per the controller's own doc comment — "queries from source tables")
drill-down reports complementing the pre-aggregated Analytics dashboards (§16).

### User Flow
Manager selects a date range → views attendance summary, revenue detail (optionally filtered by
payment method), peak attendance hours, or overall member retention rate.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/reports/attendance-summary?from=&to=` | `members.view` |
| GET | `/api/reports/revenue-detail?from=&to=&method=` | `reports.financial.view` |
| GET | `/api/reports/peak-hours` | `members.view` |
| GET | `/api/reports/member-retention` | `reports.financial.view` |

### Request/Response Contracts
- `attendance-summary` (from/to required `DateOnly`, `from <= to` else `400`) →
  `AttendanceSummaryItemDto[]`: `{ date, checkinCount, uniqueMembers }` (one row per day in range).
- `revenue-detail` (from/to required, `method` optional filter) → `RevenueDetailItemDto[]`:
  `{ id, transactionDate, memberName, planName, amount, paymentMethod }` — a flat transaction list,
  not aggregated.
- `peak-hours` (no params) → `PeakHourItemDto[]` (**top 5** per the controller's doc comment):
  `{ timeSlot: "10:00-11:00", checkinCount, percentage }`.
- `member-retention` (no params) → `MemberRetentionDto`: `{ totalExpiredMemberships,
  renewedMemberships, retentionRate }`.

### State Management
These are computed live per-request — safe to re-fetch on every filter change without staleness
concerns (unlike §16's snapshots), but potentially heavier queries for wide date ranges.

### Validation Rules
`from > to` → `400` on both date-ranged endpoints.

### Error Handling
Plain `BadRequest(result.Error)`.

### Permissions & Authorization
Same split pattern as Analytics — `members.view` for attendance-oriented reports,
`reports.financial.view` for money-oriented ones.

### Real-time Behavior
None (each call is a live query, but there's no push — re-fetch on demand).

### Integration Notes
None of these four endpoints page — for a wide date range with many transactions, `revenue-detail`
in particular could return a large flat array; consider narrowing the default date range in the UI
rather than expecting server-side pagination that doesn't exist.

### Edge Cases
`peak-hours` has no date-range parameter at all — it's a fixed top-5-overall computation, not
scoped to a period you control from this endpoint.

### Acceptance Criteria
- An inverted date range (`from > to`) is rejected client-side before submit on both applicable
  endpoints.

---

## 18. Audit Log

### Purpose
Read-only trail of significant mutating actions across the tenant, for compliance/troubleshooting.

### User Flow
Owner/settings-manager filters the audit log by entity type/id, action, actor, or date range.

### Backend APIs Used
| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/audit?entityType=&entityId=&from=&to=&action=&page=&pageSize=` | `settings.manage` |

### Request/Response Contracts
`AuditEventQueryRequest` (all fields optional except pagination defaults) → paged `AuditEventDto[]`:
```json
{ "id","actorUserId?","action","entityType?","entityId?","beforeJson?","afterJson?","ipAddress?","createdAtUtc" }
```
`beforeJson`/`afterJson` are raw JSON **strings** (not nested objects) capturing entity state
before/after the mutation — parse them client-side per your own display needs; their shape varies by
`entityType`/`action` and isn't independently typed.

### State Management
Read-only, filter-driven — no caching concerns beyond standard list re-fetch on filter change.

### Validation Rules
None beyond the query shape itself.

### Error Handling
`Problem(detail:, statusCode: 400)`.

### Permissions & Authorization
`settings.manage` only — this is the single gate for the entire audit trail, regardless of which
entity type is being viewed.

### Real-time Behavior
None.

### Integration Notes
Given `beforeJson`/`afterJson` are opaque per-entity-type JSON blobs, a generic "diff viewer"
(rather than per-entity-type-specific rendering) is the only approach that doesn't require enumerating
every possible `entityType`/`action` combination up front.

### Edge Cases
`actorUserId` may be null for system-initiated actions (e.g. a Hangfire job) — don't assume every
audit row has a human actor.

### Acceptance Criteria
- Filtering by any single optional parameter (e.g. just `entityId`) works without requiring the
  others to be supplied.

---

## 19. Staff / Admin Management

### Purpose
Owner-only CRUD over the tenant's staff accounts (Manager/Trainer/Receptionist — never another
Owner via this endpoint) plus password resets.

### User Flow
Owner views the staff list → creates a new staff account with a role → edits role/active-status →
resets a forgotten password → deactivates (soft-deletes) a departing staff member.

### Backend APIs Used
| Method | Endpoint | Policy |
|---|---|---|
| GET | `/api/admin/staff` | `OwnerOnly` |
| GET | `/api/admin/staff/{id}` | `OwnerOnly` |
| POST | `/api/admin/staff` | `OwnerOnly` |
| PUT | `/api/admin/staff/{id}` | `OwnerOnly` |
| DELETE | `/api/admin/staff/{id}` | `OwnerOnly` |
| POST | `/api/admin/staff/{id}/reset-password` | `OwnerOnly` |

The entire controller is `[Authorize(Policy = "OwnerOnly")]` at the class level — every action, with
no permission-based alternative path for a non-Owner.

### Request/Response Contracts
- `GET /api/admin/staff` → `List<StaffListItemDto>` (excludes the Owner record itself, per the
  controller's doc comment): `{ id, fullName, email, role, isActive, lastLoginAt?, createdAtUtc }`.
- `GET .../staff/{id}` → `StaffDetailDto` (adds `updatedAtUtc?`).
- `CreateStaffRequest`: `{ fullName, email, password, role: "manager"|"trainer"|"receptionist" }` (NOT
  `"owner"` — the endpoint's own doc comment states this explicitly; attempting to create another
  Owner via this endpoint should be expected to fail) → `201 Created` + `StaffDetailDto`.
- `UpdateStaffRequest`: `{ fullName, role, isActive }` → `StaffDetailDto`.
- `DELETE`: soft-delete (marks inactive) → `{ message }`.
- `ResetPasswordRequest`: `{ newPassword }` (empty rejected client-side with `400` before even
  calling the service) → `{ message }`.

### State Management
Re-fetch the staff list after any create/update/delete.

### Validation Rules
`role` must be one of the three listed (lowercase, per `CreateStaffRequest`'s doc comment) — not
`Receptionist`/`Manager`/`Trainer` PascalCase as stored elsewhere in the domain (`AppUser.Role`,
per prior backend investigation, is actually seeded PascalCase — **confirm the exact casing
`IAdminService.CreateStaffUserAsync` expects before assuming the DTO comment's lowercase example is
authoritative**; this is a spot where the DTO's inline comment and the actual persisted convention
elsewhere in the codebase may not agree, so validate against a real create call rather than hard-coding).
`NewPassword` must be non-empty (checked in the controller before calling the service).

### Error Handling
Ad-hoc `{ error, message }`.

### Permissions & Authorization
Owner-only, full stop — there is no delegated "HR" permission for managing other staff.

### Real-time Behavior
None.

### Integration Notes
None of these endpoints can create or modify an Owner account — Owner account management (if needed)
is out of scope for this controller entirely.

### Edge Cases
Deactivating a staff member does not retroactively invalidate their currently-issued JWT (permissions
are baked in at login, per §0.4) — a just-deactivated staff member's existing session remains valid
until it naturally expires (≤15 min) or is refreshed (refresh should be expected to then fail/reject,
though the exact refresh-time deactivation check wasn't directly confirmed in this survey — treat
"instant lockout" as NOT guaranteed by this API alone).

### Acceptance Criteria
- Attempting to set `role: "owner"` via `CreateStaffRequest` is rejected (client-side, don't even
  offer it as an option, and expect a server-side rejection regardless).

---

## 20. Tenant Settings & Tax Configuration

### Purpose
Gym-level branding/contact settings, tax/VAT/invoice-footer configuration, and two staff-readable
convenience endpoints (gym code, QR poster URL).

### User Flow
Owner edits gym name/logo/contact info and separately configures VAT/tax-registration/invoice-footer
text → any staff member (not just Owner) can look up the gym's own code or fetch the printable QR
poster URL for front-desk display.

### Backend APIs Used
| Method | Endpoint | Policy |
|---|---|---|
| GET | `/api/settings` | `OwnerOnly` |
| PUT | `/api/settings` | `OwnerOnly` |
| GET | `/api/settings/gym-code` | any authenticated user |
| GET | `/api/settings/qr-poster` | any authenticated user |
| GET | `/api/settings/tax` | `OwnerOnly` |
| PUT | `/api/settings/tax` | `OwnerOnly` |

Note this controller has **no class-level `[Authorize]`** — each action declares its own policy, and
two of the six are intentionally open to any authenticated user regardless of role.

### Request/Response Contracts
- `GET /api/settings` → `TenantSettingsDto`: `{ tenantId, gymName, gymNameAr, gymCode, logoUrl?,
  phoneNumber?, address?, isActive, createdAtUtc, updatedAtUtc? }`. **404** if not found (unexpected
  in practice since a tenant always has settings once created).
- `UpdateTenantSettingsRequest`: `{ gymName, gymNameAr, logoUrl?, phoneNumber?, address? }` —
  `gymName`/`gymNameAr` are validated **non-empty by the controller itself** (before calling the
  service) with a bilingual `400` message if either is blank. Note `gymCode` is **not** editable via
  this endpoint (read-only, set at tenant creation).
- `GET /api/settings/gym-code` → `{ gymCode: "GYM-CAIRO-01" }` (anonymous shape, not
  `TenantSettingsDto`).
- `GET /api/settings/qr-poster` → `{ qrPosterUrl: "..." }`.
- `GET /api/settings/tax` → `TaxSettingsDto`: `{ vatEnabled, vatRate, taxRegistrationNumber?,
  invoiceFooterText?, invoiceFooterTextAr? }`.
- `UpdateTaxSettingsRequest` (same shape as the DTO) → `TaxSettingsDto`. Per the controller's doc
  comment, this update is **audited** (before/after captured — see §18).

### State Management
Settings change infrequently — safe to cache for a session and only re-fetch after a successful
update.

### Validation Rules
`gymName`/`gymNameAr` required non-empty (checked pre-service, in the controller itself).

### Error Handling
Ad-hoc `{ error, message }`.

### Permissions & Authorization
Two of six endpoints (`gym-code`, `qr-poster`) are intentionally accessible to **any** authenticated
staff/member — don't gate their UI behind an Owner-only check; the other four are strictly Owner.

### Real-time Behavior
None.

### Integration Notes
`vatRate`/tax settings feed directly into how Sales (§6) and Invoices (§10) compute/display VAT —
changing this here affects subsequent sales' tax computation, not retroactively past ones (invoices
snapshot their VAT rate/amount at issue time, per §10).

### Edge Cases
None beyond the required-field check on the main settings update.

### Acceptance Criteria
- `gymCode` is displayed read-only in any settings-edit UI built against `PUT /api/settings` (it
  cannot be changed through this endpoint).

---

## 21. Notifications

### Purpose
Member-facing notification inbox (push/WhatsApp-delivered) plus a staff bulk-send action.

### User Flow
- **Member:** views their own notification list (paged) → marks individual notifications as read.
- **Staff (Manager+):** composes and sends a bulk notification to specific members or all active
  members.

### Backend APIs Used
| Method | Endpoint | Policy |
|---|---|---|
| GET | `/api/notifications?page=&pageSize=` | any authenticated Member |
| POST | `/api/notifications/{id}/read` | any authenticated Member (ownership-checked) |
| POST | `/api/notifications/send-bulk` | `ManagerOrAbove` |

### Request/Response Contracts
- `GET /api/notifications` → paged `NotificationDto[]` (newest first):
  ```json
  { "id","title","titleAr","body","bodyAr","channel","sentAt?","isRead" }
  ```
  Resolved via the JWT's `member_id` claim (§0.3) — **401** `{ error: "Member ID not found in token" }`
  if called with a staff token lacking that claim (i.e., this is effectively member-only despite no
  explicit role-policy attribute enforcing it — a staff JWT simply won't carry `member_id` and will
  be rejected).
- `POST .../{id}/read`: no body → `{ message }`. **403** if the notification doesn't belong to the
  calling member (ownership enforced server-side — match on `result.Error.Contains("Forbidden")` per
  the controller's own substring-based convention, not a structured code). **404** otherwise.
- `SendBulkNotificationRequest`: `{ memberIds?: ["guid"], allMembers: bool, title, titleAr, body,
  bodyAr, channel: "push"|"whatsapp" }` → `{ message }`. Exactly one targeting mode is meaningful:
  supply `memberIds` **or** set `allMembers: true` — the DTO doesn't structurally forbid supplying
  both, so don't do so; treat `allMembers: true` as taking precedence if you must choose a fallback
  behavior, but the authoritative behavior is whatever `INotificationService.SendBulkNotificationAsync`
  actually implements.

### State Management
Mark-as-read should optimistically flip local `isRead` and reconcile on next list fetch.

### Validation Rules
None beyond the required fields implied by the DTO.

### Error Handling
Ad-hoc `{ error }` / `{ error, message }`; substring-matched `"Forbidden"` for ownership violations
(a fragile convention — match defensively, e.g. case-insensitive contains, rather than exact string
equality).

### Permissions & Authorization
Bulk-send is the only staff-facing action here and needs `ManagerOrAbove`; the two member-facing
actions are role-agnostic in their attributes but are practically member-only due to the `member_id`
claim requirement.

### Real-time Behavior
None — no push notification for "you have a new notification" beyond the underlying
WhatsApp/push-channel delivery itself (which is outside this REST API's visibility).

### Integration Notes
`channel` on `SendBulkNotificationRequest` picks the delivery mechanism (`push` vs `whatsapp`) for
that specific bulk send — it is not a per-notification-type default; every bulk send explicitly
chooses its channel.

### Edge Cases
A staff member's own JWT calling `GET /api/notifications` will get a `401` (no `member_id` claim),
not an empty list — don't build a "staff notifications" screen against this endpoint.

### Acceptance Criteria
- A member can never mark another member's notification as read (403 enforced, not just hidden
  client-side).

---

## 22. Guest Invitations

### Purpose
Member-initiated guest invitations with an atomically-enforced monthly quota (quota amount is
plan-dependent, configured via `InvitationQuotasDto`-shaped tenant config — no endpoint to read/write
that config was found in this survey's Admin/TenantSettings controllers; treat quota values as
opaque, server-derived numbers surfaced only via this feature's own responses).

### User Flow
Member sends a guest invitation with the guest's name/phone/planned visit date → sees remaining
quota → later reviews invitation history (sent/visited/converted status).

### Backend APIs Used
| Method | Endpoint | Policy |
|---|---|---|
| POST | `/api/invitation/send` | `AuthenticatedMember` |
| GET | `/api/invitation/history` | `AuthenticatedMember` |

Note the route is singular `/api/invitation` (not `/api/invitations`) — this is the actual mapped
route, not a typo to "fix" client-side.

### Request/Response Contracts
- `SendInvitationRequest`: `{ guestName, guestPhoneNumber, visitDate: "date-only" }` →
  `SendInvitationResponse`:
  ```json
  { "invitationId","guestName","visitDate","quotaUsed","quotaRemaining","message","messageAr" }
  ```
  Quota enforcement is atomic server-side — a `400` `{ error }` results if the member's monthly quota
  is exhausted (no distinct machine-readable code constant found for this specific failure; surface
  `error` verbatim).
- `GET .../history` → `List<InvitationHistoryResponse>` (flat, not paged): `{ id, guestName,
  guestPhoneNumber, visitDate, status, sentAtUtc, visitedAtUtc?, convertedAtUtc? }`. `status`'s exact
  value set wasn't independently enumerated in a constants file within this survey — treat it as a
  string to display verbatim/map defensively rather than assuming a fixed closed set without
  confirming against `IInvitationService`.

### State Management
Re-fetch quota/history after every send.

### Validation Rules
None beyond required fields; quota is the meaningful server-side constraint.

### Error Handling
Ad-hoc `{ error }`; `401` if called without a valid member identity resolvable from the JWT.

### Permissions & Authorization
`AuthenticatedMember` policy only — this is a member-self-service feature with no staff-facing
equivalent endpoint in this controller.

### Real-time Behavior
None.

### Integration Notes
The `GetMemberId()` helper in this controller resolves the **ApplicationUser id from the JWT `sub`
claim**, and the service internally resolves the corresponding `GymMember` from that — do not
separately try to source/pass a `GymMember.Id` yourself for these two endpoints, it's derived
server-side from the authenticated identity.

### Edge Cases
Quota is monthly and plan-dependent — a member on a plan with `invitationQuota: 0` (§3) will always
be quota-exhausted regardless of prior usage; surface `quotaRemaining` from the response rather than
trying to compute eligibility client-side from the plan alone.

### Acceptance Criteria
- `quotaRemaining` in the send response always reflects the state immediately after that send (i.e.,
  already decremented), not the pre-send value.

---

## 23. Payment Gateway Webhooks (Backend-to-Backend — Not a Frontend Integration Point)

### Purpose
Documented here only so frontend flows that **depend on** gateway payment completion (Memberships
§4, and any gateway-method Sales payment leg §6) are correctly modeled as asynchronous.

### What the frontend needs to know
- `POST /api/payments/paymob-webhook` and `POST /api/payments/fawry-webhook` are `[AllowAnonymous]`
  endpoints called **by the payment gateways themselves**, secured by HMAC/signature verification —
  never call these from a frontend client, and never expect a frontend-usable response from them.
- After initiating a gateway-method membership assign/renew or sale payment leg, the resulting
  membership/sale remains in a **pending**-equivalent state until the relevant webhook fires and
  successfully processes (matching on an embedded `memberId|tenantId` reference the frontend never
  constructs directly — it's assembled server-side into the gateway's own order metadata).
- **There is no push notification for webhook completion** — the frontend must poll the relevant
  read endpoint (`GET /api/memberships/{memberId}/current` for memberships, or re-fetch the sale/
  invoice for a gateway sales payment) to observe the eventual state transition, or prompt the user
  to manually refresh after completing a payment redirect/flow.

### Acceptance Criteria (for flows that depend on this)
- Any screen showing a gateway-pending membership/payment provides an explicit refresh affordance
  rather than assuming automatic completion.

---

## 24. Feature Flags — Frontend Handling Summary

This is covered fully in §0.8; summarized here as its own reference point since it cross-cuts nearly
every other feature section above:

- Six modules can be independently disabled per tenant: **sales, shifts, trials, refunds, debtors,
  imports** (Call Sheet, §14, is notably **not** in this list and cannot be disabled this way).
- No endpoint exists to read a tenant's current flag state directly — the only observable signal is
  a `404 FEATURE_DISABLED` ProblemDetails response the first time a disabled module's endpoint is
  called.
- **Recommended integration pattern:** attempt the relevant module's primary read endpoint once per
  session/app-load (e.g. `GET /api/membership-plans` as a proxy for "is Sales/Plans usable," or more
  precisely whichever endpoint is cheapest per module) and cache a "module available" flag locally
  for that session, degrading the nav/UI gracefully on a `FEATURE_DISABLED` response rather than
  surfacing it as a user-facing error.

### Acceptance Criteria
- A tenant with a disabled module never shows that module's navigation entry as clickable-but-broken
  — either it's hidden entirely, or attempting to enter it is pre-empted by the cached
  availability check above.
