# Member App — My Orders API (Isolation Contract)

**Status:** Hardened 2026-09-02  
**Related service:** `MemberStoreService`  
**Staff inbox (NOT for Member App):** `GET /api/member-orders` (requires `member_orders.view`)

---

## Rule (binding)

> The Member Orders endpoint always scopes results to the authenticated member and never trusts a client-supplied member ID as the authorization source.

---

## How the current member is resolved

```text
JWT `sub` / NameIdentifier  (ASP.NET Identity user id)
        ↓
AppUser.UserId == sub
        ↓
AppUser.Id
        ↓
GymMember.AppUserId
        ↓
GymMember.Id  (= MemberOrder.MemberId)
```

Same two-hop chain as `MemberBookingService`. Do **not** compare JWT `sub` to `GymMember.AppUserId`.

---

## Endpoints (Member App)

Auth: `AuthenticatedMember` + tenant context + feature flag `inventory`.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/api/member/orders` | Preferred Profile → Orders list |
| `GET` | `/api/member/orders/{id}` | Detail; IDOR → 404 |
| `GET` | `/api/member-store/orders` | Legacy alias (same service) |
| `GET` | `/api/member-store/orders/{id}` | Legacy alias |
| `POST` | `/api/member-store/orders` | Create own order |

### Query parameters

| Name | Behavior |
|---|---|
| `memberId` | **Ignored** if present. Cannot bypass isolation. |

### Filtering

```text
Tenant (EF global filter + explicit TenantId)
  → MemberId == authenticated GymMember.Id
  → OrderBy CreatedAtUtc DESC
  → Take(100)
```

Pagination/limit is applied **after** the member filter.

### Errors

| Status | When |
|---|---|
| `401` | Missing JWT / tenant |
| `400` | Member profile not linked |
| `404` | Order missing **or** belongs to another member (no existence leak) |

---

## Do not use

| Route | Audience |
|---|---|
| `GET /api/member-orders` | Staff inbox — all members in the gym |

---

## DTOs

`MemberOrderListItemDto` / `MemberOrderDto` — product line snapshots + own member name/number.  
No staff actor ids on member responses beyond existing status timestamps.
