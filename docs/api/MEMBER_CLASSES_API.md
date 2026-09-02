# Member App — Classes API (Read-Only)

**Status:** Implemented  
**Date:** 2026-09-02

Member-facing read-only endpoints for browsing upcoming gym classes.  
**No online booking or payment** — reception handles booking and payment in person.

---

## Domain mapping

GMS uses **Activity** (`Kind = class`) + **ActivitySession** (scheduled instance), not a separate `Class` entity.

| Member App concept | Backend source |
|---|---|
| Class list item `id` | `ActivitySession.Id` |
| Name / description | `Activity.Name`, `Activity.Description` |
| Trainer | `ActivitySession.CoachUser` → `AppUser` |
| Schedule | `ActivitySession.StartsAtUtc` / `EndsAtUtc` (Cairo date/time in response) |
| Price | `Activity.DropInPrice` (null when not a paid drop-in) |
| Capacity | `ActivitySession.Capacity` |
| Available seats | `Capacity − count(bookings with status booked\|checked_in)` |
| Status | `ActivitySession.Status` (`upcoming`, `completed`, `cancelled`) |

**Facility / room name:** not modeled separately in the current schema. No `facility` field is returned.

---

## Authentication

All endpoints require:

- Policy: `AuthenticatedMember` (JWT role `Member`)
- Tenant context (existing `TenantMiddleware`)
- Linked `GymMember` profile (Identity `sub` → `AppUser.UserId` → `GymMember.AppUserId`)

---

## Endpoints

### `GET /api/member/classes`

Upcoming class sessions visible to members.

**Query parameters**

| Name | Type | Required | Description |
|---|---|---|---|
| `activityId` | `guid` | No | Filter to one activity |
| `fromUtc` | `datetime` | No | Lower bound (defaults to now UTC) |
| `limit` | `int` | No | Max rows (default 100, max 200) |

**Response:** `200 OK` → `MemberClassListItemDto[]`

**Business rules**

- Only `Activity.Kind = class`
- Only `Activity.VisibleToMembers = true` and active
- Excludes cancelled sessions
- Excludes past sessions (`StartsAtUtc < now`)
- Lazy session generation from schedules (same as staff Classes desk)
- **Read-only** — no booking/payment side effects

**Errors**

| Status | When |
|---|---|
| `401` | Missing/invalid JWT or tenant |
| `400` | Member profile not found |

---

### `GET /api/member/classes/{id}`

Class session details. `{id}` is **`ActivitySession.Id`**.

**Response:** `200 OK` → `MemberClassDetailsDto`

**Business rules**

- Same visibility filters as list
- Returns `404` for unknown id, cancelled session, or past session
- Does not expose other members' booking identities (staff-only data)
- **Read-only**

**Errors**

| Status | When |
|---|---|
| `401` | Missing/invalid JWT or tenant |
| `404` | Class not found / not visible / past / cancelled |
| `400` | Member profile not found |

---

## DTOs

### `MemberClassListItemDto`

```json
{
  "id": "session-guid",
  "activityId": "activity-guid",
  "name": "Yoga Flow",
  "nameAr": "يوغا",
  "description": "Morning yoga",
  "trainerName": "Coach Sam",
  "trainerId": "app-user-guid",
  "date": "2026-09-05",
  "startTime": "09:00:00",
  "endTime": "10:00:00",
  "durationMinutes": 60,
  "startsAtUtc": "2026-09-05T06:00:00Z",
  "endsAtUtc": "2026-09-05T07:00:00Z",
  "price": 200.00,
  "capacity": 15,
  "bookedCount": 3,
  "availableSeats": 12,
  "status": "upcoming"
}
```

### `MemberClassDetailsDto`

Nested `schedule`, `trainer`, and `availability` objects. Includes `descriptionAr`, `bookingRequired`, and full availability breakdown.

---

## Related (out of scope for this task)

| Route | Purpose |
|---|---|
| `GET /api/member/activity-bookings/activities` | Activity catalog + eligibility/quota (booking flow) |
| `GET /api/member/activity-bookings/sessions` | Bookable sessions with `canBook` flags |
| `POST /api/member/activity-bookings/sessions/{id}/book` | Online booking (not used in reception-first flow) |
| Staff `/api/activity-sessions`, `/api/activity-bookings` | Reception/admin booking (unchanged) |

---

## Flutter notes

1. Use **`id` = session id** when deep-linking to class details.
2. Schedule fields use **Africa/Cairo** for `date` / `startTime` / `endTime`; UTC fields also provided.
3. `price` may be `null` for membership-included classes.
4. Booking at reception: staff uses existing `/api/activity-bookings` after payment.
5. No facility/room field until the domain adds one.
