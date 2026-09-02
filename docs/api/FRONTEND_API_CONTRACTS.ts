/**
 * GymFlowPro — Frontend API Contracts
 *
 * Generated directly from GMS.Api / GMS.Application / GMS.Core source (controllers, DTOs,
 * constants, enums). Every type here corresponds to a real C# class/route that exists in the
 * backend today. No endpoint, field, or status code in this file is invented.
 *
 * Companion document: FRONTEND_INTEGRATION_PROMPTS.md (feature-by-feature user flows,
 * state management, validation, edge cases). This file is the machine-shaped contract layer;
 * that file is the narrative/behavioral layer. Read both.
 *
 * ── Conventions ──
 * - JSON casing: ASP.NET Core's default System.Text.Json policy is camelCase for all controller
 *   responses/requests (no custom JsonOptions registered in Program.cs) — every field below is
 *   named as it appears on the wire, already camelCase.
 * - Guid            -> string (UUID)
 * - DateTime        -> string (ISO 8601 UTC, e.g. "2026-07-13T14:30:00Z")
 * - DateOnly        -> string ("YYYY-MM-DD")
 * - TimeOnly        -> string ("HH:mm:ss")
 * - decimal         -> number
 * - enum types used DIRECTLY as a DTO field (rare — see ManualCheckinReason below) serialize as
 *   their numeric ordinal, NOT as a string. No JsonStringEnumConverter is registered anywhere in
 *   the codebase. Every other "enum-like" concept in this API (role, status, plan type, payment
 *   method, etc.) is modeled as a plain lowercase/snake_case STRING in the DTOs themselves —
 *   those are listed below as TS string-literal unions for convenience, not because the backend
 *   enforces them via a real enum.
 * - Nullable C# properties (`Type?` / `string?`) are marked optional AND `| null` below to match
 *   exactly what System.Text.Json emits (a present-but-null field, not an absent key).
 */

// ═══════════════════════════════════════════════════════════════════════════
// § 0. Cross-cutting: transport, auth, pagination, errors, feature flags
// ═══════════════════════════════════════════════════════════════════════════

/** Every tenant-scoped request must resolve a tenant. Preferred: the `gym_code` JWT claim
 *  (set automatically once logged in). Fallback: an `X-Gym-Code` header — required only for
 *  the two auth endpoints that precede having a token (POST /api/auth/login, POST /api/auth/member-otp,
 *  POST /api/auth/member-verify all take gymCode in the body instead). TenantMiddleware caches the
 *  resolved tenant for 10 minutes per gym code. */
export interface TenantHeader {
  "X-Gym-Code"?: string;
}

/** Decoded JWT claims. Staff tokens carry role + discrete "perm" claims (17 possible values,
 *  baked in at login — no per-request DB/permission check). Member tokens carry no role claim
 *  and no working `member_id` claim (see note). `sub` is the ASP.NET Identity user id in both
 *  cases; for member accounts this IS the value to use as the member's identity for ownership
 *  checks (see NOTE below). */
export interface JwtClaims {
  sub: string; // Guid — ASP.NET Identity user id (== member id for member logins, see note)
  email: string;
  jti: string;
  tenant_id: string; // Guid
  gym_code: string;
  first_name: string;
  last_name: string;
  /** ClaimTypes.Role — one or more of: "Owner" | "Manager" | "Trainer" | "Member" | "Receptionist".
   *  Absent entirely for... actually always present; staff and member logins both add role claims
   *  via ASP.NET Identity's role assignment. */
  role?: string | string[];
  /** Repeated claim, one entry per granted permission string (see PermissionKey below). Present
   *  only for staff-type roles; member-role tokens are granted none of these by ResolvePermissionsAsync. */
  perm?: string | string[];
}
/** NOTE (verified against GMS.Infrastructure/Services/TokenService.cs and
 *  GMS.Application/Services/AuthService.cs): NO code path ever adds a `member_id` claim to any
 *  JWT — `TokenService.GenerateAccessTokenAsync` only emits sub/email/jti/tenant_id/gym_code/
 *  first_name/last_name/role/perm. `NotificationsController.GetMemberId()` looks for `member_id`
 *  first and falls back to `sub` — in practice it ALWAYS falls back to `sub`. Frontend should
 *  never expect or send a `member_id` claim; treat `sub` as the member's id for member-scoped
 *  calls (this matches what `InvitationController.GetMemberId()` does directly). */

/** GET/list endpoints that accept page/pageSize return this envelope. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

/** Shape A — RFC7807 ProblemDetails, returned by controllers that route Result<T> failures
 *  through a `ProblemFromResult(error)` helper (Sales, Shifts, Refunds, Trials, ZReports,
 *  CallSheet, Debtors, Imports). `title` is always the machine-readable CODE (e.g.
 *  "OPEN_SHIFT_REQUIRED"), `detail` is the human message, `status` is the mapped HTTP status. */
export interface ProblemDetailsError {
  type?: string;
  title: string; // machine-readable code
  status: number;
  detail: string; // human message
  instance?: string;
}

/** Shape B — ad-hoc anonymous JSON object, returned by controllers that did NOT adopt the
 *  ProblemDetails helper (Members, Memberships, Admin, Notifications, Invitation,
 *  TenantSettings, Attendance, Auth, PromoCodes, MembershipPlans, Analytics, Reports, Audit).
 *  Shape varies by call site — the two forms actually seen in source are: */
export type AdHocError =
  | { message: string }
  | { error: string; message?: string };

export type ApiError = ProblemDetailsError | AdHocError;

/** Legacy DTO type (GMS.Application.DTOs.ErrorResponse) — declared in source but not
 *  constructed/returned by any controller found in this survey. Do not build error handling
 *  against this shape; it is dead code as far as the API surface goes. */
export interface ErrorResponse {
  message: string;
  details?: string | null;
  statusCode: number;
}

/** Idempotency: ONLY POST /api/sales supports this. Send either the `X-Idempotency-Key` header
 *  or the `idempotencyKey` field in the body — the header wins if both are present. No other
 *  endpoint in the API (refund execution, invoice creation, import execution included) exposes
 *  a client-supplied idempotency key. */
export interface IdempotencyHeader {
  "X-Idempotency-Key"?: string;
}

/** Feature flags live in Tenant.Settings JSON under "feature_flags" and gate 6 modules via
 *  [FeatureFlag("...")] on the controller class. All default to true — a tenant with no explicit
 *  config has every module enabled. A disabled module returns 404 with title "FEATURE_DISABLED". */
export interface FeatureFlagsDto {
  sales: boolean;
  shifts: boolean;
  trials: boolean;
  refunds: boolean;
  debtors: boolean;
  imports: boolean;
}
/** Controllers actually decorated with [FeatureFlag(...)]: SalesController("sales"),
 *  ShiftsController("shifts"), TrialController("trials"), RefundsController("refunds"),
 *  DebtorsController("debtors"), ImportsController("imports"). NOTE: CallSheetController has NO
 *  [FeatureFlag] attribute despite being an operational sibling of these — call-sheet endpoints
 *  are NOT gated by any flag. */

/** Role policies (ASP.NET named policies, distinct from the fine-grained perm system below):
 *  OwnerOnly, ManagerOrAbove (Owner|Manager), AnyStaff (Owner|Manager|Trainer|Receptionist),
 *  AuthenticatedMember (Member role only), AnyAuthenticated (any valid JWT regardless of role). */
export type RolePolicy =
  | "OwnerOnly"
  | "ManagerOrAbove"
  | "AnyStaff"
  | "AuthenticatedMember"
  | "AnyAuthenticated";

/** Fine-grained permission strings — the "perm" JWT claim values. Exhaustive (17 total,
 *  GMS.Core.Constants.Permissions.All). Owner is granted all 17 at login; other roles get a
 *  subset resolved server-side (not documented in this DTO layer — treat as backend-authoritative,
 *  the frontend should gate UI on the actual `perm` claims present in the token, not on role name). */
export type PermissionKey =
  | "members.view"
  | "members.create"
  | "members.edit"
  | "checkin.manual"
  | "sales.sell"
  | "sales.discount.apply"
  | "sales.discount.override"
  | "payments.cash.accept"
  | "payments.refund.request"
  | "payments.refund.approve"
  | "shift.open"
  | "shift.close"
  | "shift.reconcile.approve"
  | "memberships.freeze"
  | "plans.manage"
  | "reports.financial.view"
  | "settings.manage";

/** Staff role strings as they actually appear on the wire (StaffListItemDto.Role,
 *  StaffDetailDto.Role, LoginResponse.User.Role, UpdateStaffRequest.Role are all lowercase).
 *  CAVEAT: CreateStaffRequest's doc-comment says lowercase too, but this has NOT been reconciled
 *  against how DataSeeder elsewhere seeds roles (PascalCase, matching ASP.NET Identity's
 *  ClaimTypes.Role convention) — confirm actual casing against a real login response before
 *  hardcoding a comparison. */
export type StaffRole = "owner" | "manager" | "trainer" | "receptionist";

// ═══════════════════════════════════════════════════════════════════════════
// § 1. Authentication & Session (AuthController — api/auth, no [Authorize] prefix)
// ═══════════════════════════════════════════════════════════════════════════

export interface LoginRequest {
  email: string;
  password: string;
  gymCode: string;
}
export interface UserInfo {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string;
  gymCode: string;
}
export interface LoginResponse {
  accessToken: string; // 15 min expiry
  refreshToken: string; // 30 day expiry
  expiresAtUtc: string;
  user: UserInfo;
}
export interface RefreshTokenRequest {
  refreshToken: string;
}
export interface MemberOtpRequest {
  phoneNumber: string; // international format e.g. +201234567890
  gymCode: string;
}
export interface MemberOtpVerifyRequest {
  phoneNumber: string;
  gymCode: string;
  otp: string; // 6-digit
}

export const AUTH_ENDPOINTS = {
  login: { method: "POST", path: "/api/auth/login" }, // body: LoginRequest -> LoginResponse
  refresh: { method: "POST", path: "/api/auth/refresh" }, // body: RefreshTokenRequest -> LoginResponse
  memberOtpSend: { method: "POST", path: "/api/auth/member-otp" }, // body: MemberOtpRequest
  memberOtpVerify: { method: "POST", path: "/api/auth/member-verify" }, // body: MemberOtpVerifyRequest -> LoginResponse
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 2. Members (MembersController — api/members, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface MemberListItemDto {
  id: string;
  memberNumber: string;
  fullName: string;
  fullNameAr: string;
  phone: string;
  isActive: boolean;
  activePlan?: string | null;
  activePlanAr?: string | null;
  expiryDate?: string | null; // DateOnly
  membershipStatus?: string | null;
  planType?: string | null;
  sessionsRemaining?: number | null;
}
export interface MembershipSummaryDto {
  id: string;
  planName: string;
  planNameAr: string;
  planType: string;
  status: string; // active | expired | frozen | cancelled | pending
  startDate: string; // DateOnly
  endDate: string; // DateOnly
  sessionsRemaining?: number | null;
  frozenFromDate?: string | null;
  frozenUntilDate?: string | null;
  amountPaid: number;
  paymentMethod: string;
}
export interface AttendanceSummaryDto {
  id: string;
  checkInAtUtc: string;
  checkOutAtUtc?: string | null;
  entryMethod: string;
}
export interface MemberDetailDto {
  id: string;
  memberNumber: string;
  fullName: string;
  fullNameAr: string;
  phone: string;
  email: string;
  dateOfBirth: string; // DateOnly
  profilePhotoUrl?: string | null;
  notes?: string | null;
  isActive: boolean;
  invitationQuotaRemaining: number;
  createdAtUtc: string;
  currentMembership?: MembershipSummaryDto | null;
  recentAttendance: AttendanceSummaryDto[]; // last 5
}
export interface CreateMemberRequest {
  fullName: string;
  fullNameAr: string;
  phone: string;
  dateOfBirth: string; // DateOnly
  nationalId?: string | null;
  emergencyContact?: string | null;
  email?: string | null;
  notes?: string | null;
}
export interface UpdateMemberRequest {
  fullName?: string | null;
  fullNameAr?: string | null;
  phone?: string | null;
  dateOfBirth?: string | null;
  nationalId?: string | null;
  emergencyContact?: string | null;
  email?: string | null;
  notes?: string | null;
}
export interface FreezeMembershipRequest {
  frozenUntil: string; // DateTime
  reason?: string | null;
}
export interface MemberCreditEntryDto {
  id: string;
  amount: number;
  entryType: "refund" | "payment_use" | "adjustment";
  referenceId?: string | null;
  reason?: string | null;
  createdAtUtc: string;
}
export interface MemberCreditSummaryDto {
  balance: number;
  entries: MemberCreditEntryDto[];
}

export const MEMBERS_ENDPOINTS = {
  list: { method: "GET", path: "/api/members" }, // query: search?, status?, page=1, pageSize=20 -> PagedResult<MemberListItemDto> ; perm members.view
  get: (id: string) => ({ method: "GET", path: `/api/members/${id}` }), // -> MemberDetailDto ; perm members.view
  create: { method: "POST", path: "/api/members" }, // body: CreateMemberRequest ; perm members.create
  update: (id: string) => ({ method: "PUT", path: `/api/members/${id}` }), // body: UpdateMemberRequest ; perm members.edit
  deactivate: (id: string) => ({ method: "DELETE", path: `/api/members/${id}` }), // policy OwnerOnly
  attendance: (id: string) => ({ method: "GET", path: `/api/members/${id}/attendance` }), // query: page=1, pageSize=20 ; perm members.view
  currentMembership: (id: string) => ({ method: "GET", path: `/api/members/${id}/membership` }), // -> MembershipSummaryDto ; perm members.view
  freeze: (id: string) => ({ method: "POST", path: `/api/members/${id}/freeze` }), // body: FreezeMembershipRequest ; perm memberships.freeze
  unfreeze: (id: string) => ({ method: "POST", path: `/api/members/${id}/unfreeze` }), // perm memberships.freeze
  credits: (id: string) => ({ method: "GET", path: `/api/members/${id}/credits` }), // -> MemberCreditSummaryDto ; perm members.view
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 3. Membership Plans (MembershipPlansController — api/membership-plans, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

/** PlanType is a free string, not a real C# enum. Valid values per doc-comments in
 *  CreatePlanRequest/UpdatePlanRequest: */
export type PlanType =
  | "monthly_unlimited"
  | "session_pack"
  | "time_limited"
  | "pt_credits"
  | "family"
  | "trial"
  | "day_pass";

export interface PlanListItemDto {
  id: string;
  name: string;
  nameAr: string;
  planType: string;
  price: number;
  currency: string; // default "EGP"
  durationDays: number;
  isActive: boolean;
  createdAtUtc: string;
}
export interface PlanDetailDto {
  id: string;
  name: string;
  nameAr: string;
  description: string;
  descriptionAr: string;
  planType: string;
  price: number;
  currency: string;
  durationDays: number;
  sessionCount?: number | null;
  timeRestrictionStart?: string | null; // TimeOnly
  timeRestrictionEnd?: string | null;
  invitationQuota: number;
  trialVisitLimit?: number | null;
  isActive: boolean;
  activeMemberships: number;
  totalMemberships: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}
export interface CreatePlanRequest {
  name: string;
  nameAr: string;
  description?: string | null;
  descriptionAr?: string | null;
  planType: PlanType; // default "monthly_unlimited"
  price: number;
  durationDays: number;
  sessionCount?: number | null; // session_pack: 10, 20, or 50
  timeRestrictionStart?: string | null; // time_limited plans
  timeRestrictionEnd?: string | null;
  invitationQuota?: number; // family plans, default 0
  trialVisitLimit?: number | null; // trial plans
}
export type UpdatePlanRequest = CreatePlanRequest;

export const PLANS_ENDPOINTS = {
  list: { method: "GET", path: "/api/membership-plans" }, // -> PlanListItemDto[] (NOT paged) ; perm plans.manage
  get: (id: string) => ({ method: "GET", path: `/api/membership-plans/${id}` }), // -> PlanDetailDto ; perm plans.manage
  create: { method: "POST", path: "/api/membership-plans" }, // body: CreatePlanRequest ; perm plans.manage
  update: (id: string) => ({ method: "PUT", path: `/api/membership-plans/${id}` }), // body: UpdatePlanRequest ; perm plans.manage
  delete: (id: string) => ({ method: "DELETE", path: `/api/membership-plans/${id}` }), // perm plans.manage
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 4. Memberships — Assign/Renew/History (MembershipsController — api/memberships, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface MembershipDto {
  id: string;
  planName: string;
  planNameAr: string;
  planType: string;
  startDate: string;
  endDate: string;
  status: string;
  sessionsRemaining?: number | null;
  amountPaid: number;
  paymentMethod: string;
  paymentDate?: string | null;
  autoRenew: boolean;
  frozenFromDate?: string | null;
  frozenUntilDate?: string | null;
  daysRemaining: number; // computed server-side
}
export interface MembershipHistoryItemDto {
  id: string;
  planName: string;
  planNameAr: string;
  planType: string;
  startDate: string;
  endDate: string;
  status: string;
  amountPaid: number;
  paymentMethod: string;
  paymentDate?: string | null;
  createdAtUtc: string;
}
export interface AssignMembershipRequest {
  planId: string;
  /** 'cash' | 'paymob' | 'fawry' — cash activates immediately; gateway methods create a
   *  'pending' membership, activated later by a payment webhook. */
  paymentMethod: "cash" | "paymob" | "fawry";
}
export interface RenewMembershipRequest {
  planId?: string | null; // null = renew same plan
  paymentMethod: "cash" | "paymob" | "fawry" | "vodafone_cash"; // default "cash"
  amountPaid: number;
}

export const MEMBERSHIPS_ENDPOINTS = {
  current: (memberId: string) => ({ method: "GET", path: `/api/memberships/${memberId}/current` }), // policy AnyStaff -> MembershipDto
  history: (memberId: string) => ({ method: "GET", path: `/api/memberships/${memberId}/history` }), // query: page=1, pageSize=20 ; policy AnyStaff -> PagedResult<MembershipHistoryItemDto>
  assign: (memberId: string) => ({ method: "POST", path: `/api/memberships/${memberId}/assign` }), // body: AssignMembershipRequest ; policy ManagerOrAbove -> 201 MembershipDto | 409 if already active
  renew: (memberId: string) => ({ method: "POST", path: `/api/memberships/${memberId}/renew` }), // body: RenewMembershipRequest ; policy ManagerOrAbove -> MembershipDto
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 5. Attendance & Check-In (AttendanceController — api/attendance)
// ═══════════════════════════════════════════════════════════════════════════

/** ManualCheckinReason is a REAL C# enum used directly as a DTO field type — it serializes as
 *  its numeric ordinal (no string converter registered). Send/expect the number, not the name. */
export const ManualCheckinReason = {
  DeadPhone: 1,
  NoAppYet: 2,
  AppIssue: 3,
  Other: 4,
} as const;
export type ManualCheckinReason = typeof ManualCheckinReason[keyof typeof ManualCheckinReason];

export interface QrCheckinRequest {
  gymCode: string; // from the gym's static QR code
}
export interface QrCheckinResponse {
  attendanceId: string;
  memberName: string;
  memberNameAr: string;
  checkInAtUtc: string;
  planName: string;
  planNameAr: string;
  sessionsRemaining?: number | null;
  message: string;
  messageAr: string;
}
export interface ManualCheckinRequest {
  memberId: string;
  reason: ManualCheckinReason; // numeric ordinal
  notes?: string | null; // for reason = Other(4)
}
export interface ManualCheckinResponse extends QrCheckinResponse {
  staffName: string;
}
export interface MemberSearchRequest {
  query: string;
  includeInactive?: boolean; // default false
}
export interface MemberSearchResult {
  id: string;
  memberNumber: string;
  fullName: string;
  fullNameAr: string;
  phoneNumber: string;
  profilePhotoUrl: string;
  membershipStatus: string; // active | expired | frozen | cancelled | none
  planName: string;
  planNameAr: string;
  planType?: string;
  sessionsRemaining?: number | null;
  isSelectable: boolean;
  unselectableReason?: string | null;
  unselectableReasonAr?: string | null;
}
export interface TodayAttendanceDto {
  id: string;
  memberId: string;
  memberNumber: string;
  memberName: string;
  memberNameAr: string;
  checkInAtUtc: string;
  checkOutAtUtc?: string | null;
  entryMethod: string;
  planName?: string | null;
}

export const ATTENDANCE_ENDPOINTS = {
  qrCheckin: { method: "POST", path: "/api/attendance/qr-checkin" }, // policy AuthenticatedMember ; body: QrCheckinRequest -> QrCheckinResponse
  manualCheckin: { method: "POST", path: "/api/attendance/manual-checkin" }, // perm checkin.manual ; body: ManualCheckinRequest -> ManualCheckinResponse
  searchMembers: { method: "GET", path: "/api/attendance/search-members" }, // perm checkin.manual ; query: MemberSearchRequest -> MemberSearchResult[]
  today: { method: "GET", path: "/api/attendance/today" }, // perm members.view ; query: filter="all" -> TodayAttendanceDto[]
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 6. Point of Sale / Sales (SalesController — api/sales, [Authorize], [FeatureFlag("sales")])
// ═══════════════════════════════════════════════════════════════════════════

export type PaymentMethod = "cash" | "card_paymob" | "fawry" | "vodafone" | "instapay" | "account_credit";

export interface NewMemberRequest {
  fullName: string;
  fullNameAr?: string | null;
  phoneNumber: string;
  dateOfBirth?: string | null; // optional for walk-ins
}
export interface ManualDiscountRequest {
  amount: number;
  reason: string;
}
export interface SalePaymentRequest {
  method: PaymentMethod;
  amount: number;
}
export interface PartialPaymentRequest {
  dueDate: string; // DateOnly — presence of this object signals intent to leave a balance due
}
export interface CreateSaleRequest {
  idempotencyKey?: string | null; // or use X-Idempotency-Key header instead (header wins)
  memberId?: string | null; // exactly one of memberId / newMember must be set
  newMember?: NewMemberRequest | null;
  planId: string;
  promoCode?: string | null;
  manualDiscount?: ManualDiscountRequest | null;
  payments: SalePaymentRequest[];
  partialPayment?: PartialPaymentRequest | null;
}
export interface RecordPaymentRequest {
  method: PaymentMethod;
  amount: number;
}
export interface SaleTotalsDto {
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
  paid: number;
  amountDue: number;
}
export interface SaleResponse {
  saleId: string;
  membershipId?: string | null;
  isReplay: boolean; // true if this response came from an idempotency-key replay
  invoiceStatus: "queued" | "skipped" | "not_applicable";
  totals: SaleTotalsDto;
  warnings: string[];
  receiptUrl?: string | null; // set only by RecordPayment responses
}
export interface ValidatePromoRequest {
  code: string;
  planId: string;
  memberId: string;
}
export interface PromoValidationResult {
  isValid: boolean;
  failureReason?: PromoValidationReasonCode | null;
  promoCodeId?: string | null;
  code?: string | null;
  type?: "percent" | "fixed" | null;
  originalPrice?: number | null;
  discountAmount?: number | null;
  finalPrice?: number | null;
}
export type PromoValidationReasonCode =
  | "CODE_NOT_FOUND"
  | "CODE_INACTIVE"
  | "DATE_RANGE_INVALID"
  | "MAX_USES_REACHED"
  | "MEMBER_MAX_USES_REACHED"
  | "PLAN_NOT_IN_SCOPE"
  | "BELOW_MIN_PRICE"
  | "PLAN_NOT_FOUND";
/** ProblemDetails.title values for POST /api/sales and /api/sales/{id}/payments. */
export type SaleFailureCode =
  | "STAFF_USER_NOT_FOUND" // 400
  | "MEMBER_NOT_FOUND" // 400
  | "MEMBER_CREATE_FAILED" // 400
  | "PLAN_NOT_FOUND" // 400
  | "FORBIDDEN_DISCOUNT_OVERRIDE" // 403
  | "OVERPAY" // 400
  | "PAYMENT_INCOMPLETE" // 400
  | "OPEN_SHIFT_REQUIRED" // 409
  | "PROMO_RACE_LOST" // 400
  | "SALE_NOT_FOUND" // 400
  | "PAYMENT_EXCEEDS_AMOUNT_DUE" // 400
  | "INSUFFICIENT_CREDIT"; // 400

export const SALES_ENDPOINTS = {
  validatePromo: { method: "POST", path: "/api/sales/validate-promo" }, // perm sales.sell ; body: ValidatePromoRequest -> PromoValidationResult
  create: { method: "POST", path: "/api/sales" }, // perm sales.sell ; body: CreateSaleRequest ; header X-Idempotency-Key? -> SaleResponse
  recordPayment: (id: string) => ({ method: "POST", path: `/api/sales/${id}/payments` }), // perm sales.sell ; body: RecordPaymentRequest -> SaleResponse
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 7. Promo Codes (PromoCodesController — api/promo-codes, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface PromoCodeDto {
  id: string;
  code: string;
  type: "percent" | "fixed";
  value: number;
  appliesTo?: string[] | null; // plan ids, null = all plans
  validFrom: string; // DateOnly
  validTo: string;
  maxUses?: number | null;
  maxUsesPerMember?: number | null;
  usesCount: number;
  minPrice?: number | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}
export interface CreatePromoCodeRequest {
  code: string; // stored uppercased regardless of input case
  type: "percent" | "fixed"; // default "percent"
  value: number;
  appliesTo?: string[] | null;
  validFrom: string;
  validTo: string;
  maxUses?: number | null;
  maxUsesPerMember?: number | null;
  minPrice?: number | null;
}
export type UpdatePromoCodeRequest = CreatePromoCodeRequest;

export const PROMO_CODES_ENDPOINTS = {
  list: { method: "GET", path: "/api/promo-codes" }, // perm sales.sell ; query: activeOnly?, validToday?, page=1, pageSize=20 -> PagedResult<PromoCodeDto>
  get: (id: string) => ({ method: "GET", path: `/api/promo-codes/${id}` }), // perm sales.sell -> PromoCodeDto
  create: { method: "POST", path: "/api/promo-codes" }, // perm plans.manage ; body: CreatePromoCodeRequest
  update: (id: string) => ({ method: "PUT", path: `/api/promo-codes/${id}` }), // perm plans.manage ; body: UpdatePromoCodeRequest
  deactivate: (id: string) => ({ method: "DELETE", path: `/api/promo-codes/${id}` }), // perm plans.manage
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 8. Cash Drawer / Shifts (ShiftsController — api/shifts, [Authorize], [FeatureFlag("shifts")])
// ═══════════════════════════════════════════════════════════════════════════

export interface CashMovementDto {
  id: string;
  type: "sale" | "refund" | "paid_in" | "paid_out" | "float_adjust";
  amount: number; // signed: positive = cash in, negative = cash out
  referenceId?: string | null;
  reason?: string | null;
  createdByUserId: string;
  createdAtUtc: string;
}
export interface ShiftDto {
  id: string;
  userId: string;
  userName?: string | null;
  openedAt: string;
  closedAt?: string | null;
  openingFloat: number;
  /** Null while open (blind-count design: counting staff must not see the expected total before
   *  entering their physical count). Populated only once closed. */
  expectedCash?: number | null;
  countedCash?: number | null;
  variance?: number | null;
  varianceNote?: string | null;
  approvedByUserId?: string | null;
  status: "open" | "closed" | "approved";
  movements: CashMovementDto[];
}
export interface OpenShiftRequest {
  openingFloat: number;
}
export interface CloseShiftRequest {
  countedCash: number;
  varianceNote?: string | null;
}
export interface RecordMovementRequest {
  /** Only 'paid_in' | 'paid_out' | 'float_adjust' may be POSTed — 'sale'/'refund' rows are
   *  recorded internally by the sale/refund flows. */
  type: "paid_in" | "paid_out" | "float_adjust";
  /** SIGN CONVENTION (verified in ShiftService.RecordMovementAsync/NormalizeSignedAmount):
   *  for paid_in/paid_out, send a POSITIVE magnitude — the server normalizes the sign itself
   *  (paid_in -> positive, paid_out -> negative) regardless of what you send. For float_adjust,
   *  the server uses your literal signed value as-is (positive or negative). */
  amount: number;
  referenceId?: string | null;
  reason?: string | null;
}
export interface ApproveShiftRequest {
  note?: string | null;
}
export interface ShiftOpenSummaryDto {
  openShifts: ShiftDto[];
  totalCashInDrawers: number;
}
/** ProblemDetails.title values for shift endpoints. */
export type ShiftFailureCode =
  | "STAFF_USER_NOT_FOUND"
  | "SHIFT_ALREADY_OPEN" // 409
  | "NO_OPEN_SHIFT" // 409
  | "SHIFT_NOT_FOUND" // 404
  | "NOT_AWAITING_APPROVAL" // 409
  | "MANAGER_APPROVAL_REQUIRED" // 403
  | "INVALID_MOVEMENT_TYPE"
  | "SHIFT_NOT_OPEN"; // 409

export const SHIFTS_ENDPOINTS = {
  open: { method: "POST", path: "/api/shifts/open" }, // perm shift.open ; body: OpenShiftRequest -> ShiftDto
  current: { method: "GET", path: "/api/shifts/current" }, // perm shift.open -> ShiftDto
  closeCurrent: { method: "POST", path: "/api/shifts/current/close" }, // perm shift.close ; body: CloseShiftRequest -> ShiftDto
  recordMovement: { method: "POST", path: "/api/shifts/current/movements" }, // perm shift.open ; body: RecordMovementRequest
  approve: (id: string) => ({ method: "POST", path: `/api/shifts/${id}/approve` }), // perm shift.reconcile.approve ; body: ApproveShiftRequest
  forceClose: (id: string) => ({ method: "POST", path: `/api/shifts/${id}/force-close` }), // policy ManagerOrAbove
  list: { method: "GET", path: "/api/shifts" }, // policy ManagerOrAbove ; query: from?, to?, userId?, page=1, pageSize=20 -> PagedResult<ShiftDto>
  openSummary: { method: "GET", path: "/api/shifts/open-summary" }, // perm reports.financial.view -> ShiftOpenSummaryDto
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 9. Refunds & Account Credit (RefundsController — api/refunds, [Authorize], [FeatureFlag("refunds")])
// ═══════════════════════════════════════════════════════════════════════════

export interface RefundDto {
  id: string;
  saleId: string;
  paymentTransactionId?: string | null;
  amount: number;
  method: "cash" | "gateway" | "credit";
  reason: string;
  requestedByUserId: string;
  approvedByUserId?: string | null;
  status: "requested" | "approved" | "executed" | "rejected";
  rejectionNote?: string | null;
  creditNoteInvoiceId?: string | null;
  executedAt?: string | null;
  createdAtUtc: string;
}
export interface RequestRefundRequest {
  saleId: string;
  amount: number;
  /** 'gateway' is a listed value but NOT implemented server-side — requesting it fails with
   *  GATEWAY_REFUND_UNSUPPORTED. Only 'cash' and 'credit' currently work end-to-end. */
  method: "cash" | "gateway" | "credit";
  reason: string;
}
export interface RejectRefundRequest {
  note: string;
}
/** ProblemDetails.title values for refund endpoints. */
export type RefundFailureCode =
  | "STAFF_USER_NOT_FOUND"
  | "SALE_NOT_FOUND" // 404
  | "REFUND_NOT_FOUND" // 404
  | "REFUND_EXCEEDS_REMAINDER"
  | "SALE_FULLY_REFUNDED" // 409
  | "NOT_AWAITING_APPROVAL" // 409
  | "SELF_APPROVAL_FORBIDDEN" // 403 — approver cannot be the same user who requested it
  | "OPEN_SHIFT_REQUIRED" // 409
  | "GATEWAY_REFUND_UNSUPPORTED" // 409 — method:"gateway" always fails this today
  | "INSUFFICIENT_CREDIT";

export const REFUNDS_ENDPOINTS = {
  request: { method: "POST", path: "/api/refunds" }, // perm payments.refund.request ; body: RequestRefundRequest
  approve: (id: string) => ({ method: "POST", path: `/api/refunds/${id}/approve` }), // perm payments.refund.approve
  reject: (id: string) => ({ method: "POST", path: `/api/refunds/${id}/reject` }), // perm payments.refund.approve ; body: RejectRefundRequest
  list: { method: "GET", path: "/api/refunds" }, // perm payments.refund.approve ; query: saleId?, memberId?, status? -> RefundDto[]
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 10. Invoices & Receipts (InvoicesController — api/invoices, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface InvoiceLineSnapshotDto {
  description: string;
  descriptionAr?: string | null;
  qty: number; // default 1
  unitPrice: number;
  lineTotal: number;
}
export interface InvoiceDto {
  id: string;
  type: "invoice" | "credit_note";
  invoiceNumber: string;
  saleId?: string | null;
  originalInvoiceId?: string | null;
  memberNameSnapshot: string;
  memberPhoneSnapshot: string;
  lines: InvoiceLineSnapshotDto[];
  subtotal: number;
  discountAmount: number;
  vatRate: number;
  vatAmount: number;
  total: number;
  currency: string;
  issuedAt: string;
  pdfUrl?: string | null;
  status: "issued" | "voided";
  voidReason?: string | null;
}
export interface InvoiceQueryRequest {
  from?: string | null;
  to?: string | null;
  memberId?: string | null;
  status?: "issued" | "voided" | null;
  type?: "invoice" | "credit_note" | null;
  page?: number; // default 1
  pageSize?: number; // default 20
}
export interface VoidInvoiceRequest {
  reason: string;
}
export interface PaymentReceiptInfoDto {
  amount: number;
  paidAtUtc: string;
  method?: string | null;
}

export const INVOICES_ENDPOINTS = {
  list: { method: "GET", path: "/api/invoices" }, // perm reports.financial.view ; query: InvoiceQueryRequest -> PagedResult<InvoiceDto>
  get: (id: string) => ({ method: "GET", path: `/api/invoices/${id}` }), // perm reports.financial.view -> InvoiceDto
  void: (id: string) => ({ method: "POST", path: `/api/invoices/${id}/void` }), // perm payments.refund.approve ; body: VoidInvoiceRequest
  resend: (id: string) => ({ method: "POST", path: `/api/invoices/${id}/resend` }), // perm sales.sell
  receiptHtml: (id: string, paymentId?: string) => ({
    method: "GET",
    path: `/api/invoices/${id}/receipt-html${paymentId ? `?paymentId=${paymentId}` : ""}`,
  }), // perm sales.sell -> text/html; paymentId adds a "Payment Received" section (PaymentReceiptInfoDto data)
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 11. Daily Z-Report (ZReportController — api/reports/z, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface ZReportMethodTotalDto {
  method: string; // cash | card_paymob | fawry | vodafone | instapay | account_credit
  count: number;
  total: number;
}
export interface ZReportLineTypeTotalDto {
  lineType: "membership" | "trial" | "day_pass" | "retail" | "fee";
  count: number;
  revenue: number;
}
export interface ZReportShiftRowDto {
  userId: string;
  userName: string;
  openedAt: string;
  closedAt?: string | null;
  openingFloat: number;
  expectedCash?: number | null;
  countedCash?: number | null;
  variance?: number | null;
  status: "open" | "closed" | "approved";
}
export interface ZReportDto {
  id: string;
  tenantId: string;
  reportDate: string; // DateOnly
  pdfUrl?: string | null;
  generatedAt: string;
  generatedByUserId?: string | null;
  methodTotals: ZReportMethodTotalDto[];
  lineTypeTotals: ZReportLineTypeTotalDto[];
  promoDiscountTotal: number;
  manualDiscountTotal: number;
  manualDiscountCount: number;
  refundsTotal: number;
  shifts: ZReportShiftRowDto[];
  outstandingAddedToday: number;
  membershipRevenueToday: number;
}

export const ZREPORT_ENDPOINTS = {
  get: (date: string) => ({ method: "GET", path: `/api/reports/z/${date}` }), // date=YYYY-MM-DD ; perm reports.financial.view -> ZReportDto (404 ZREPORT_NOT_FOUND if not yet generated)
  pdf: (date: string) => ({ method: "GET", path: `/api/reports/z/${date}/pdf` }), // perm reports.financial.view -> application/pdf
  regenerate: (date: string) => ({ method: "POST", path: `/api/reports/z/${date}/regenerate` }), // policy ManagerOrAbove
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 12. Free Trials (TrialController — api/trials, [Authorize], [FeatureFlag("trials")])
// ═══════════════════════════════════════════════════════════════════════════

export interface TrialInitiateRequest {
  fullName: string;
  fullNameAr?: string | null;
  phoneNumber: string;
  planId: string; // must reference a plan with planType "trial"
}
export interface TrialInitiateResponse {
  otpSent: boolean;
  expiresInSeconds: number;
}
export interface TrialConfirmRequest {
  phoneNumber: string;
  otp: string;
}
export interface TrialConfirmResponse {
  member: MemberDetailDto;
  membership: MembershipSummaryDto;
}
/** ProblemDetails.title values for trial endpoints. */
export type TrialFailureCode =
  | "PLAN_NOT_FOUND" // 404
  | "PLAN_NOT_TRIAL"
  | "TRIAL_ALREADY_USED" // 409
  | "OTP_INVALID"
  | "PENDING_TRIAL_NOT_FOUND" // 404
  | "STAFF_USER_NOT_FOUND";

export const TRIALS_ENDPOINTS = {
  initiate: { method: "POST", path: "/api/trials/initiate" }, // perm sales.sell ; body: TrialInitiateRequest -> TrialInitiateResponse
  confirm: { method: "POST", path: "/api/trials/confirm" }, // perm sales.sell ; body: TrialConfirmRequest -> TrialConfirmResponse
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 13. Debtors (DebtorsController — api/debtors, [Authorize], [FeatureFlag("debtors")])
// ═══════════════════════════════════════════════════════════════════════════

export interface DebtorDto {
  memberId: string;
  fullName: string;
  phoneNumber: string;
  totalDue: number;
  oldestDueDate: string;
  agingBucket: "0-7" | "8-30" | "30+";
  lastPaymentAt?: string | null;
}
export interface DebtorsSummaryDto {
  totalOutstanding: number;
  debtorCount: number;
}
/** ProblemDetails.title values for debtors endpoints. */
export type DebtorFailureCode =
  | "MEMBER_NOT_FOUND" // 404
  | "NO_OUTSTANDING_BALANCE" // 404
  | "REMINDER_THROTTLE"; // 429

export const DEBTORS_ENDPOINTS = {
  list: { method: "GET", path: "/api/debtors" }, // perm sales.sell ; query: page=1, pageSize=20, format? ("csv" streams text/csv instead of JSON) -> PagedResult<DebtorDto>
  summary: { method: "GET", path: "/api/debtors/summary" }, // perm reports.financial.view -> DebtorsSummaryDto
  remind: (memberId: string) => ({ method: "POST", path: `/api/debtors/${memberId}/remind` }), // perm sales.sell ; 429 REMINDER_THROTTLE if reminded too recently
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 14. Renewal Call Sheet (CallSheetController — api/call-sheet, [Authorize] — NO [FeatureFlag])
// ═══════════════════════════════════════════════════════════════════════════

export interface CallSheetEntryDto {
  membershipId: string;
  memberId: string;
  fullName: string;
  phoneNumber: string;
  planName: string;
  endDate: string;
  lastVisitAt?: string | null;
  lastCallOutcome?: "contacted" | "renewed" | "declined" | "no_answer" | null;
}
export interface RecordCallOutcomeRequest {
  outcome: "contacted" | "renewed" | "declined" | "no_answer";
  note?: string | null;
}
export interface RenewalRateDto {
  staffUserId: string;
  staffName: string;
  totalCalled: number;
  renewed: number;
  renewalRatePercent: number;
}
/** ProblemDetails.title values for call-sheet endpoints. */
export type CallSheetFailureCode = "MEMBERSHIP_NOT_FOUND" | "STAFF_USER_NOT_FOUND" | "INVALID_OUTCOME";

export const CALL_SHEET_ENDPOINTS = {
  expiring: { method: "GET", path: "/api/call-sheet/expiring" }, // perm sales.sell ; query: days=7 -> CallSheetEntryDto[]
  recordOutcome: (membershipId: string) => ({ method: "POST", path: `/api/call-sheet/${membershipId}/outcome` }), // perm sales.sell ; body: RecordCallOutcomeRequest
  renewalRate: { method: "GET", path: "/api/call-sheet/renewal-rate" }, // perm reports.financial.view ; query: from (DateOnly, required), to (DateOnly, required), staffUserId? -> RenewalRateDto[]
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 15. Bulk Import (ImportsController — api/imports, [Authorize], [FeatureFlag("imports")])
// ═══════════════════════════════════════════════════════════════════════════

export interface ImportBatchDto {
  id: string;
  fileName: string;
  status: "validating" | "dry_run_ready" | "importing" | "completed" | "rolled_back" | "failed";
  totalRows: number;
  okRows: number;
  errorRows: number;
  mapping: Record<string, string>; // sourceHeader -> targetField
  completedAt?: string | null;
  createdAtUtc: string;
}
export interface ColumnMapRequest {
  /** target field values: fullName | phoneNumber | planName | startDate | endDate |
   *  sessionsRemaining | dateOfBirth */
  mapping: Record<string, string>;
}
export interface ImportPlanSpec {
  name: string;
  planType: PlanType; // default "monthly_unlimited"
  durationDays: number;
  price: number;
}
export interface CreatePlansFromImportRequest {
  plans: ImportPlanSpec[];
}
/** Per-row validation codes (ImportRow.ErrorCodes, comma-separated if multiple apply). */
export type ImportRowErrorCode =
  | "PHONE_INVALID"
  | "PHONE_DUP_FILE"
  | "PHONE_EXISTS"
  | "PLAN_UNMATCHED"
  | "DATE_RANGE_INVALID"
  | "RETAINED_HAS_ACTIVITY"
  | "ROLLED_BACK";
/** ProblemDetails.title values for batch-level import endpoints. */
export type ImportFailureCode =
  | "BATCH_NOT_FOUND" // 404
  | "INVALID_STATUS"
  | "FILE_TOO_LARGE"
  | "TOO_MANY_ROWS"
  | "UNSUPPORTED_FILE_TYPE"
  | "ROLLBACK_WINDOW_EXPIRED";

export const IMPORTS_ENDPOINTS = {
  upload: { method: "POST", path: "/api/imports" }, // perm settings.manage ; multipart/form-data file: File -> ImportBatchDto
  setMapping: (id: string) => ({ method: "POST", path: `/api/imports/${id}/mapping` }), // perm settings.manage ; body: ColumnMapRequest
  get: (id: string) => ({ method: "GET", path: `/api/imports/${id}` }), // perm settings.manage -> ImportBatchDto
  errorsCsv: (id: string) => ({ method: "GET", path: `/api/imports/${id}/errors.csv` }), // perm settings.manage -> text/csv
  execute: (id: string) => ({ method: "POST", path: `/api/imports/${id}/execute` }), // perm settings.manage
  rollback: (id: string) => ({ method: "POST", path: `/api/imports/${id}/rollback` }), // policy ManagerOrAbove
  createPlans: (id: string) => ({ method: "POST", path: `/api/imports/${id}/create-plans` }), // perm plans.manage ; body: CreatePlansFromImportRequest
  template: { method: "GET", path: "/api/imports/template.xlsx" }, // perm settings.manage -> binary xlsx
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 16. Analytics Dashboards (AnalyticsController — api/analytics, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

/** Legacy analytics snapshot; retained for compatibility only. */
export interface LegacyAnalyticsDashboardOverviewDto {
  activeMembers: number;
  expiredMembers: number;
  newMembersThisMonth: number;
  revenueThisMonth: number;
  checkinsToday: number;
  checkinsThisWeek: number;
  snapshotTimeUtc: string; // pre-computed snapshot, not live
}

export interface DashboardOverviewDto {
  period: { key: string; from: string; to: string };
  today: Record<string, number | null>;
  financial?: DashboardFinancialDto | null;
  business?: Record<string, number> | null;
  operations: Record<string, unknown>;
  attention: { items: Array<{ key: string; count: number; amount?: number | null }> };
  quickActions: Array<{ key: string }>;
  dataIssues: Array<{ section: string; code: string }>;
}
export interface DashboardFinancialDto {
  calculationVersion: string;
  cashCollected: number;
  collections: number;
  settledCashInflow: number;
  settledCashAvailable: boolean;
  revenue: number;
  revenueAdjustments: number;
  refunds: number;
  cashRefunds: number;
  creditRefunds: number;
  outstanding: number;
  expenses?: number | null;
  cogs?: number | null;
  grossProfit?: number | null;
  payrollExpense?: number | null;
  operatingExpenses?: number | null;
  netProfit?: number | null;
  profitMargin?: number | null;
  cashOutflows: number;
  netCashFlow: number;
  cashFlowAvailable: boolean;
  supplierCashPaymentsAvailable: boolean;
  payrollCoverageStatus: "NO_PAYROLL_PERIOD" | "PAYROLL_DATA_INCOMPLETE" | "COMPLETE";
  accountsReceivable: number;
  accountsReceivableCount: number;
  accountsPayable: number;
  cogsAvailable: boolean;
  payrollAvailable: boolean;
  netProfitAvailable: boolean;
  financialDataIssues: string[];
  trustStates: Record<string, "TRUSTWORTHY" | "CONDITIONALLY_TRUSTWORTHY" | "UNAVAILABLE" | "REQUIRES_RECONCILIATION">;
  breakdown: Array<{ key: string; amount: number; count: number }>;
  revenueTrend: Array<{ date: string; value: number }>;
}
export interface RevenueChartDto {
  labels: string[]; // month names, e.g. "Jan"
  values: number[];
}
/** 7 days (Mon=0..Sun=6) x 24 hours matrix of check-in counts. */
export interface AttendanceHeatmapDto {
  data: number[][];
}
export interface MemberStatusPieDto {
  active: number;
  expired: number;
  frozen: number;
  cancelled: number;
  total: number; // computed server-side
}
export interface InvitationTypeFunnelDto {
  sent: number;
  visited: number;
  converted: number;
  conversionRate: number; // 0-100
}
export interface InvitationFunnelDto {
  sent: number;
  visited: number;
  converted: number;
  conversionRate: number; // 0-100 — all types (backward compatible)
  guestPass: InvitationTypeFunnelDto;
  referral: InvitationTypeFunnelDto;
  newMembersThisMonth: number;
  referralConvertedMembersThisMonth: number;
  percentNewMembersFromReferrals: number;
}
export interface TrialAnalyticsDto {
  issued: number;
  converted: number;
  conversionRate: number;
  expired: number;
}

export const ANALYTICS_ENDPOINTS = {
  overview: { method: "GET", path: "/api/analytics/overview" }, // legacy analytics snapshot -> LegacyAnalyticsDashboardOverviewDto
  dashboardOverview: { method: "GET", path: "/api/dashboard/overview" }, // role-filtered canonical dashboard -> DashboardOverviewDto
  revenue: { method: "GET", path: "/api/analytics/revenue" }, // perm reports.financial.view ; query: months=6 -> RevenueChartDto
  heatmap: { method: "GET", path: "/api/analytics/heatmap" }, // perm members.view -> AttendanceHeatmapDto
  memberStatus: { method: "GET", path: "/api/analytics/members-status" }, // perm members.view -> MemberStatusPieDto
  invitations: { method: "GET", path: "/api/analytics/invitations" }, // perm members.view -> InvitationFunnelDto
  trials: { method: "GET", path: "/api/analytics/trials" }, // perm reports.financial.view ; query: month (required, format not further specified — pass "YYYY-MM") -> TrialAnalyticsDto
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 17. Detailed Reports (ReportsController — api/reports, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface AttendanceSummaryItemDto {
  date: string; // DateOnly
  checkinCount: number;
  uniqueMembers: number;
}
export interface RevenueDetailItemDto {
  id: string;
  transactionDate: string;
  memberName: string;
  planName: string;
  amount: number;
  paymentMethod: string;
}
export interface PeakHourItemDto {
  timeSlot: string; // e.g. "10:00-11:00"
  checkinCount: number;
  percentage: number;
}
export interface MemberRetentionDto {
  totalExpiredMemberships: number;
  renewedMemberships: number;
  retentionRate: number; // 0-100
}

export interface ProfitabilityDto {
  calculationVersion: string;
  from: string;
  to: string;
  collections: number;
  settledCashInflow: number;
  settledCashAvailable: boolean;
  revenue: number;
  revenueAdjustments: number;
  refunds: number;
  cashRefunds: number;
  creditRefunds: number;
  cogs: number | null;
  operatingExpenses: number;
  payrollExpense: number | null;
  payrollCashDisbursements: number;
  supplierCashPayments: number;
  grossProfit: number | null;
  netProfit: number | null;
  netProfitAvailable: boolean;
  profitMargin: number | null;
  cashOutflows: number;
  netCashFlow: number;
  accountsReceivable: number;
  accountsReceivableCount: number;
  accountsPayable: number;
  cogsAvailable: boolean;
  payrollAvailable: boolean;
  payrollCoverageStatus: "NO_PAYROLL_PERIOD" | "PAYROLL_DATA_INCOMPLETE" | "COMPLETE";
  cashFlowAvailable: boolean;
  supplierCashPaymentsAvailable: boolean;
  accountsPayableAvailable: boolean;
  dataIssues: string[];
  trustStates: Record<string, "TRUSTWORTHY" | "CONDITIONALLY_TRUSTWORTHY" | "UNAVAILABLE" | "REQUIRES_RECONCILIATION">;
  revenueBreakdown: Array<{ key: string; amount: number; count: number }>;
  revenueTrend: Array<{ date: string; value: number }>;
}
export interface CashFlowDto {
  calculationVersion: string;
  from: string;
  to: string;
  collections: number;
  settledCashInflow: number;
  settledCashAvailable: boolean;
  cashRefunds: number;
  operatingExpenseCashOutflows: number;
  payrollCashDisbursements: number;
  supplierCashPayments: number;
  cashOutflows: number;
  netCashFlow: number;
  cashFlowAvailable: boolean;
  supplierCashPaymentsAvailable: boolean;
  payrollCoverageStatus: "NO_PAYROLL_PERIOD" | "PAYROLL_DATA_INCOMPLETE" | "COMPLETE";
  dataIssues: string[];
}
export interface CashExpenseDto {
  id: string;
  expenseDate: string;
  category: string;
  amount: number;
  status: "posted" | "void";
  note?: string | null;
  paymentMethod?: string | null;
  payee?: string | null;
  description?: string | null;
  sourceType?: string | null;
  sourceReference?: string | null;
  idempotencyKey?: string | null;
  shiftId?: string | null;
}
export interface CogsBackfillItemDto {
  saleLineId: string;
  oldCost: number | null;
  evidence: string;
  reconstructedCost: number | null;
  status: "RECONSTRUCTABLE" | "UNAVAILABLE" | "ALREADY_RELIABLE";
}
export interface CogsBackfillDto {
  scanned: number;
  backfilled: number;
  skipped: number;
  skippedSaleLineIds: string[];
  items: CogsBackfillItemDto[];
}
export interface SaleAdjustmentDto {
  id: string;
  saleId: string;
  amount: number;
  type: "write_off" | "cancellation";
  status: "posted";
  reason: string;
  createdByUserId: string;
  createdAtUtc: string;
}
export interface SaleBalanceReconciliationDto {
  saleId: string;
  previousAmountDue: number;
  canonicalAmountDue: number;
  allocatedPayments: number;
  postedAdjustments: number;
  status: "reconciled" | "already_reconciled";
}

export const REPORTS_ENDPOINTS = {
  attendanceSummary: { method: "GET", path: "/api/reports/attendance-summary" }, // perm members.view -> AttendanceSummaryItemDto[]
  revenueDetail: { method: "GET", path: "/api/reports/revenue-detail" }, // perm reports.financial.view -> RevenueDetailItemDto[]
  peakHours: { method: "GET", path: "/api/reports/peak-hours" }, // perm members.view -> PeakHourItemDto[]
  memberRetention: { method: "GET", path: "/api/reports/member-retention" }, // perm reports.financial.view -> MemberRetentionDto
  profitability: { method: "GET", path: "/api/reports/profitability" }, // canonical report -> ProfitabilityDto
  reconciliation: { method: "GET", path: "/api/reports/financial-reconciliation" }, // canonical compatibility alias -> ProfitabilityDto
  cashFlow: { method: "GET", path: "/api/reports/cash-flow" }, // canonical cash-only report -> CashFlowDto
  backfillCogs: { method: "POST", path: "/api/reports/profitability/backfill-cogs" }, // perm inventory.manage; traceable stock evidence only
  adjustments: { method: "GET", path: "/api/sales/adjustments" }, // perm reports.financial.view -> SaleAdjustmentDto[]
  reconcileSaleBalance: { method: "POST", path: "/api/sales/{saleId}/reconcile-balance" }, // perm payments.refund.approve; auditable denormalized-balance repair
} as const;

export const EXPENSES_ENDPOINTS = {
  list: { method: "GET", path: "/api/expenses" }, // perm reports.expenses.view; query from,to -> CashExpenseDto[]
  create: { method: "POST", path: "/api/expenses" }, // perm reports.expenses.manage -> CashExpenseDto
  update: (id: string) => ({ method: "PATCH", path: `/api/expenses/${id}` }), // posted/void transition
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 18. Audit Log (AuditController — api/audit, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════

export interface AuditEventDto {
  id: string;
  actorUserId?: string | null;
  action: string;
  entityType?: string | null;
  entityId?: string | null;
  beforeJson?: string | null;
  afterJson?: string | null;
  ipAddress?: string | null;
  createdAtUtc: string;
}
export interface AuditEventQueryRequest {
  entityType?: string | null;
  entityId?: string | null;
  action?: string | null;
  from?: string | null;
  to?: string | null;
  page?: number; // default 1
  pageSize?: number; // default 20
}

export const AUDIT_ENDPOINTS = {
  list: { method: "GET", path: "/api/audit" }, // perm settings.manage ; query: AuditEventQueryRequest -> PagedResult<AuditEventDto>
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 19. Staff / Admin Management (AdminController — api/admin, policy OwnerOnly at class level)
// ═══════════════════════════════════════════════════════════════════════════

export interface StaffListItemDto {
  id: string;
  fullName: string;
  email: string;
  role: string; // owner | manager | trainer | receptionist
  isActive: boolean;
  lastLoginAt?: string | null;
  createdAtUtc: string;
}
export interface StaffDetailDto extends StaffListItemDto {
  updatedAtUtc?: string | null;
}
export interface CreateStaffRequest {
  fullName: string;
  email: string;
  password: string;
  role: "manager" | "trainer" | "receptionist"; // NOT "owner" — default "trainer"
}
export interface UpdateStaffRequest {
  fullName: string;
  role: "manager" | "trainer" | "receptionist";
  isActive: boolean;
}
export interface ResetPasswordRequest {
  newPassword: string;
}

export const ADMIN_ENDPOINTS = {
  staffList: { method: "GET", path: "/api/admin/staff" }, // -> StaffListItemDto[] (excludes owner)
  staffGet: (id: string) => ({ method: "GET", path: `/api/admin/staff/${id}` }), // -> StaffDetailDto
  staffCreate: { method: "POST", path: "/api/admin/staff" }, // body: CreateStaffRequest
  staffUpdate: (id: string) => ({ method: "PUT", path: `/api/admin/staff/${id}` }), // body: UpdateStaffRequest
  staffDelete: (id: string) => ({ method: "DELETE", path: `/api/admin/staff/${id}` }),
  staffResetPassword: (id: string) => ({ method: "POST", path: `/api/admin/staff/${id}/reset-password` }), // body: ResetPasswordRequest
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 20. Tenant Settings & Tax Configuration (TenantSettingsController — api/settings)
// ═══════════════════════════════════════════════════════════════════════════
// NOTE: this controller has NO class-level [Authorize] — GetTenantSettings/UpdateTenantSettings/
// GetTaxSettings/UpdateTaxSettings each declare [Authorize(Policy="OwnerOnly")] individually, but
// GetGymCode and GetQRPosterUrl declare only plain [Authorize] (any authenticated staff or member).

export interface TenantSettingsDto {
  tenantId: string;
  gymName: string;
  gymNameAr: string;
  gymCode: string;
  logoUrl?: string | null;
  phoneNumber?: string | null;
  address?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}
export interface UpdateTenantSettingsRequest {
  gymName: string;
  gymNameAr: string;
  logoUrl?: string | null;
  phoneNumber?: string | null;
  address?: string | null;
}
export interface TaxSettingsDto {
  vatEnabled: boolean;
  vatRate: number;
  taxRegistrationNumber?: string | null;
  invoiceFooterText?: string | null;
  invoiceFooterTextAr?: string | null;
}
export type UpdateTaxSettingsRequest = TaxSettingsDto;

export const TENANT_SETTINGS_ENDPOINTS = {
  get: { method: "GET", path: "/api/settings" }, // policy OwnerOnly -> TenantSettingsDto
  update: { method: "PUT", path: "/api/settings" }, // policy OwnerOnly ; body: UpdateTenantSettingsRequest
  gymCode: { method: "GET", path: "/api/settings/gym-code" }, // [Authorize] any staff -> { gymCode: string }
  qrPoster: { method: "GET", path: "/api/settings/qr-poster" }, // [Authorize] any staff -> { qrPosterUrl: string }
  tax: { method: "GET", path: "/api/settings/tax" }, // policy OwnerOnly -> TaxSettingsDto
  updateTax: { method: "PUT", path: "/api/settings/tax" }, // policy OwnerOnly ; body: UpdateTaxSettingsRequest
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 21. Notifications (NotificationsController — api/notifications, [Authorize])
// ═══════════════════════════════════════════════════════════════════════════
// NOTE: GetMyNotifications/MarkAsRead carry only [Authorize] in the attributes — they are scoped
// to "whichever member the JWT's sub claim resolves to" at runtime (see JwtClaims note above),
// not restricted by role in the attribute itself.

export interface NotificationDto {
  id: string;
  title: string;
  titleAr: string;
  body: string;
  bodyAr: string;
  channel: string; // "push" | "whatsapp"
  sentAt?: string | null;
  isRead: boolean;
}
export interface SendBulkNotificationRequest {
  memberIds?: string[] | null; // null = use allMembers flag
  allMembers: boolean;
  title: string;
  titleAr: string;
  body: string;
  bodyAr: string;
  channel: "push" | "whatsapp"; // default "push"
}

export const NOTIFICATIONS_ENDPOINTS = {
  mine: { method: "GET", path: "/api/notifications" }, // [Authorize] ; query: page=1, pageSize=20 -> PagedResult<NotificationDto>
  markRead: (id: string) => ({ method: "POST", path: `/api/notifications/${id}/read` }), // [Authorize] ; 403 if not the owning member
  sendBulk: { method: "POST", path: "/api/notifications/send-bulk" }, // policy ManagerOrAbove ; body: SendBulkNotificationRequest
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 22. Guest Invitations (InvitationController — api/invitation)
// ═══════════════════════════════════════════════════════════════════════════

export interface SendInvitationRequest {
  guestName: string;
  guestPhoneNumber: string;
  visitDate: string; // DateOnly
}
export interface SendInvitationResponse {
  invitationId: string;
  guestName: string;
  visitDate: string;
  quotaUsed: number;
  quotaRemaining: number;
  message: string;
  messageAr: string;
}
export interface InvitationHistoryResponse {
  id: string;
  guestName: string;
  guestPhoneNumber: string;
  visitDate: string;
  status: string;
  sentAtUtc: string;
  visitedAtUtc?: string | null;
  convertedAtUtc?: string | null;
}
export interface InvitationQuotasDto {
  quotasByPlanType: Record<string, number>; // e.g. { monthly_unlimited: 3, family: 5 }
}

export const INVITATION_ENDPOINTS = {
  send: { method: "POST", path: "/api/invitation/send" }, // policy AuthenticatedMember ; body: SendInvitationRequest -> SendInvitationResponse
  history: { method: "GET", path: "/api/invitation/history" }, // policy AuthenticatedMember -> InvitationHistoryResponse[]
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 23. Payment Gateway Webhooks (PaymentsController — api/payments)
// ═══════════════════════════════════════════════════════════════════════════
// NOT a frontend integration point. These are server-to-server webhook receivers called
// directly by Paymob/Fawry, not by the SPA/mobile client. Listed for completeness only.

export const PAYMENTS_WEBHOOK_ENDPOINTS = {
  paymob: { method: "POST", path: "/api/payments/paymob-webhook" }, // gateway callback only
  fawry: { method: "POST", path: "/api/payments/fawry-webhook" }, // gateway callback only
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// § 24. Real-time — SignalR (AttendanceHub at /hubs/attendance)
// ═══════════════════════════════════════════════════════════════════════════
// Connect with the same JWT access token (as an access_token query param or Authorization
// header per the SignalR JS client's accessTokenFactory). Clients are auto-joined to a
// tenant-isolated group "tenant-{tenantId}" server-side — no client-side group join call needed.
// Delivery is best-effort: a failed/absent SignalR push never blocks or fails the underlying
// check-in HTTP call.

export interface MemberCheckedInEvent {
  attendanceId: string;
  memberId: string;
  memberName: string;
  memberNameAr: string;
  checkInAtUtc: string;
  entryMethod: string;
}
export const SIGNALR = {
  hubUrl: "/hubs/attendance",
  events: {
    /** The ONLY event emitted by this hub. Payload matches QrCheckinResponse/ManualCheckinResponse
     *  data at the time of check-in (verify exact field set against SignalRCheckinNotifier if the
     *  UI needs pixel-perfect parity — this type reflects the fields common to both). */
    memberCheckedIn: "MemberCheckedIn",
  },
} as const;
