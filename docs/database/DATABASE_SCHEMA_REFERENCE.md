# 🗄️ GymFlowPro Database Schema Reference

**Version:** v1.0.0  
**.NET 8 + Entity Framework Core**  
**Database:** SQL Server (LocalDB / Production)

---

## 📑 Table of Contents

1. [Entity Relationship Diagram (ERD)](#entity-relationship-diagram)
2. [Table Definitions](#table-definitions)
3. [Relationships](#relationships)
4. [Indexes](#indexes)
5. [Stored Procedures](#stored-procedures)
6. [Constraints & Rules](#constraints--rules)
7. [Migration History](#migration-history)

---

## Entity Relationship Diagram

```
┌─────────────────────────────────┐
│         Tenants                 │
│ ┌─────────────────────────────┐ │
│ │ Id (PK, GUID)               │ │
│ │ Name (VARCHAR 255)          │ │
│ │ CreatedAt (DATETIME)        │ │
│ │ UpdatedAt (DATETIME)        │ │
└─┼─────────────────────────────┘ │
  │ 1                             │
  │                               │
  │ N       ┌─────────────────────┴────────────────┐
  └─────────┤ One Tenant has many:                │
            │ - GymMembers                        │
            │ - Memberships                       │
            │ - MembershipPlans                   │
            │ - GymAttendance                     │
            │ - MemberInvitations                 │
            │ - AppUsers                          │
            └─────────────────────────────────────┘

┌─────────────────────────┐        ┌─────────────────────────┐
│    GymMembers           │        │   MembershipPlans       │
│ ┌─────────────────────┐ │        │ ┌─────────────────────┐ │
│ │ Id (PK)             │ │        │ │ Id (PK)             │ │
│ │ TenantId (FK)   ────┼─┼────┬───┼─┤ TenantId (FK)       │ │
│ │ FullName            │ │    │   │ │ Name                │ │
│ │ PhoneNumber         │ │    │   │ │ PriceEGP            │ │
│ │ Email               │ │    │   │ │ Type (Standard/Prem)│ │
│ │ JoinedAt            │ │    │   │ │ SessionsPerMonth    │ │
│ │ ActiveMembership ───┼─┼────┤   │ └─────────────────────┘ │
│ │ (FK)                │ │    │   │                         │
│ └─────────────────────┘ │    │   └─────────────────────────┘
└─────────────────────────┘    │
          │                    │
          │ 1                  │ N
          │                    │
          │ N         1        │
          └────────────────────┤
                               │
                ┌──────────────┴─────────────┐
                │    Memberships             │
                │ ┌──────────────────────┐  │
                │ │ Id (PK)              │  │
                │ │ MemberId (FK) ──────┘  │
                │ │ TenantId (FK)           │
                │ │ PlanId (FK) ────────┐   │
                │ │ StartDate           │   │
                │ │ EndDate             │   │
                │ │ IsFrozen            │   │
                │ │ FrozenUntil         │   │
                │ │ Status              │   │
                │ │ PaymentStatus       │   │
                │ └──────────────────────┘  │
                └────────────────────────────┘

┌────────────────────────────────────┐
│      GymAttendance                 │
│ ┌────────────────────────────────┐ │
│ │ Id (PK)                        │ │
│ │ MemberId (FK) ────────────┐    │ │
│ │ TenantId (FK)             │    │ │
│ │ CheckInTime (DATETIME)    │    │ │
│ │ CheckOutTime (DATETIME?)  │    │ │
│ │ EntryMethod (qr/manual)   │    │ │
│ │ StaffUserId (FK, nullable)│    │ │
│ └────────────────────────────┘    │
└────────────────────────────────────┘
           │
           └─ Belongs to GymMember (1:N)

┌────────────────────────────────────┐
│   MemberInvitations                │
│ ┌────────────────────────────────┐ │
│ │ Id (PK)                        │ │
│ │ MemberId (FK) ────────────┐    │ │
│ │ TenantId (FK)             │    │ │
│ │ GuestName                 │    │ │
│ │ GuestPhone                │    │ │
│ │ SentAt (DATETIME)         │    │ │
│ │ ExpirationDate (DATETIME) │    │ │
│ │ Status (Active/Expired)   │    │ │
│ └────────────────────────────┘    │
└────────────────────────────────────┘
           │
           └─ Belongs to GymMember (1:N)

┌────────────────────────────────────┐
│        AppUsers (Identity)         │
│ ┌────────────────────────────────┐ │
│ │ Id (PK, GUID)                  │ │
│ │ TenantId (FK)                  │ │
│ │ Email (VARCHAR, UNIQUE)        │ │
│ │ PasswordHash                   │ │
│ │ PhoneNumber                    │ │
│ │ Role (Staff/Manager/Admin)     │ │
│ │ IsActive (BOOL)                │ │
│ │ LastLoginAt (DATETIME?)        │ │
│ │ CreatedAt (DATETIME)           │ │
│ └────────────────────────────────┘ │
└────────────────────────────────────┘
```

---

## Table Definitions

### 1. Tenants

**Purpose:** Isolate multi-tenant data at database level

```sql
CREATE TABLE [dbo].[Tenants] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(255) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [IsActive] BIT NOT NULL DEFAULT 1
);
```

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | UNIQUEIDENTIFIER | ❌ | Primary key (GUID) |
| `Name` | NVARCHAR(255) | ❌ | Gym name (e.g., "Power Gym Cairo") |
| `CreatedAt` | DATETIME2 | ❌ | Tenant creation timestamp |
| `UpdatedAt` | DATETIME2 | ❌ | Last update timestamp |
| `IsActive` | BIT | ❌ | Soft delete flag |

---

### 2. GymMembers

**Purpose:** Store member/customer information

```sql
CREATE TABLE [dbo].[GymMembers] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [FullName] NVARCHAR(200) NOT NULL,
    [PhoneNumber] NVARCHAR(20) NOT NULL,
    [Email] NVARCHAR(255) NULL,
    [DateOfBirth] DATE NULL,
    [Gender] NVARCHAR(10) NULL, -- 'Male', 'Female', 'Other'
    [JoinedAt] DATETIME2 NOT NULL,
    [ActiveMembershipId] UNIQUEIDENTIFIER NULL FOREIGN KEY REFERENCES [Memberships]([Id]),
    [Notes] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT UK_GymMembers_Phone UNIQUE ([TenantId], [PhoneNumber])
);
```

| Column | Type | Notes |
|--------|------|-------|
| `Id` | UNIQUEIDENTIFIER | PK |
| `TenantId` | UNIQUEIDENTIFIER | FK to Tenants |
| `FullName` | NVARCHAR(200) | Required |
| `PhoneNumber` | NVARCHAR(20) | Unique per tenant, searchable |
| `Email` | NVARCHAR(255) | Optional |
| `DateOfBirth` | DATE | Optional |
| `Gender` | NVARCHAR(10) | Optional |
| `JoinedAt` | DATETIME2 | Registration date |
| `ActiveMembershipId` | UNIQUEIDENTIFIER | FK to current active Membership |
| `Notes` | NVARCHAR(MAX) | Free text (medical notes, etc.) |
| `CreatedAt` | DATETIME2 | Auto-set to UTC now |
| `UpdatedAt` | DATETIME2 | Auto-update on changes |

---

### 3. MembershipPlans

**Purpose:** Define subscription tiers (Standard, Premium, VIP, etc.)

```sql
CREATE TABLE [dbo].[MembershipPlans] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [Name] NVARCHAR(100) NOT NULL, -- 'Standard', 'Premium', 'VIP'
    [Description] NVARCHAR(MAX) NULL,
    [PriceEGP] DECIMAL(10, 2) NOT NULL, -- Monthly price
    [Type] NVARCHAR(50) NOT NULL, -- 'Standard', 'Premium'
    [SessionsPerMonth] INT NULL, -- Unlimited if NULL
    [DurationDays] INT NOT NULL, -- Usually 30
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [IsActive] BIT NOT NULL DEFAULT 1
);
```

| Column | Type | Description |
|--------|------|-------------|
| `Id` | UNIQUEIDENTIFIER | PK |
| `TenantId` | UNIQUEIDENTIFIER | FK to Tenants |
| `Name` | NVARCHAR(100) | "Standard", "Premium", "VIP" |
| `Description` | NVARCHAR(MAX) | Benefits list |
| `PriceEGP` | DECIMAL(10, 2) | Monthly price in EGP |
| `Type` | NVARCHAR(50) | Plan tier type |
| `SessionsPerMonth` | INT | NULL = unlimited |
| `DurationDays` | INT | Usually 30 |
| `IsActive` | BIT | Is this plan available? |

---

### 4. Memberships

**Purpose:** Track individual member subscriptions (lifecycle)

```sql
CREATE TABLE [dbo].[Memberships] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [MemberId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [GymMembers]([Id]),
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [MembershipPlanId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [MembershipPlans]([Id]),
    [StartDate] DATE NOT NULL,
    [EndDate] DATE NOT NULL,
    [Status] NVARCHAR(50) NOT NULL, -- 'Active', 'Expired', 'Cancelled'
    [PaymentStatus] NVARCHAR(50) NOT NULL, -- 'Paid', 'Pending', 'Failed'
    [IsFrozen] BIT NOT NULL DEFAULT 0,
    [FrozenUntil] DATETIME2 NULL, -- When freeze expires
    [FreezeReason] NVARCHAR(200) NULL,
    [SessionsUsedThisMonth] INT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

| Column | Type | Notes |
|--------|------|-------|
| `Id` | UNIQUEIDENTIFIER | PK |
| `MemberId` | UNIQUEIDENTIFIER | FK to GymMembers |
| `TenantId` | UNIQUEIDENTIFIER | FK to Tenants |
| `MembershipPlanId` | UNIQUEIDENTIFIER | FK to MembershipPlans |
| `StartDate` | DATE | When membership begins |
| `EndDate` | DATE | When membership expires |
| `Status` | NVARCHAR(50) | 'Active', 'Expired', 'Cancelled' |
| `PaymentStatus` | NVARCHAR(50) | 'Paid', 'Pending', 'Failed' |
| `IsFrozen` | BIT | Is membership frozen? |
| `FrozenUntil` | DATETIME2 | When freeze expires |
| `SessionsUsedThisMonth` | INT | Tracks usage against quota |

---

### 5. GymAttendance

**Purpose:** Log all check-in events

```sql
CREATE TABLE [dbo].[GymAttendance] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [MemberId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [GymMembers]([Id]),
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [CheckInTime] DATETIME2 NOT NULL,
    [CheckOutTime] DATETIME2 NULL,
    [EntryMethod] NVARCHAR(50) NOT NULL, -- 'qr' or 'manual'
    [StaffUserId] UNIQUEIDENTIFIER NULL FOREIGN KEY REFERENCES [AppUsers]([Id]),
    [Notes] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT CK_EntryMethod CHECK ([EntryMethod] IN ('qr', 'manual'))
);

CREATE INDEX [IX_GymAttendance_MemberId_CheckInTime] 
    ON [dbo].[GymAttendance]([MemberId], [CheckInTime]);
```

| Column | Type | Purpose |
|--------|------|---------|
| `Id` | UNIQUEIDENTIFIER | PK |
| `MemberId` | UNIQUEIDENTIFIER | Which member checked in |
| `TenantId` | UNIQUEIDENTIFIER | Which gym |
| `CheckInTime` | DATETIME2 | Check-in timestamp |
| `CheckOutTime` | DATETIME2 | Optional check-out time |
| `EntryMethod` | NVARCHAR(50) | 'qr' or 'manual' |
| `StaffUserId` | UNIQUEIDENTIFIER | Who did the manual check-in (if manual) |
| `Notes` | NVARCHAR(MAX) | Any notes |

---

### 6. MemberInvitations

**Purpose:** Track guest invitations with monthly quota

```sql
CREATE TABLE [dbo].[MemberInvitations] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [MemberId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [GymMembers]([Id]),
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [GuestName] NVARCHAR(200) NOT NULL,
    [GuestPhone] NVARCHAR(20) NOT NULL,
    [SentAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ExpirationDate] DATETIME2 NOT NULL, -- Usually 7 days from SentAt
    [Status] NVARCHAR(50) NOT NULL, -- 'Active', 'Expired', 'Used', 'Revoked'
    [Notes] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

| Column | Type | Description |
|--------|------|-------------|
| `Id` | UNIQUEIDENTIFIER | PK |
| `MemberId` | UNIQUEIDENTIFIER | Who sent the invite |
| `TenantId` | UNIQUEIDENTIFIER | Which gym |
| `GuestName` | NVARCHAR(200) | Guest's name |
| `GuestPhone` | NVARCHAR(20) | Guest's phone (for OTP?) |
| `SentAt` | DATETIME2 | When invited |
| `ExpirationDate` | DATETIME2 | When invitation expires (usually +7 days) |
| `Status` | NVARCHAR(50) | 'Active', 'Expired', 'Used', 'Revoked' |

**Monthly Quota Rule:**
- Count invitations for this member in the current calendar month
- If count >= 3, reject new invitations
- Quota resets on the 1st of each month

---

### 7. AppUsers

**Purpose:** Staff/Admin authentication (Identity users)

```sql
CREATE TABLE [dbo].[AppUsers] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [Tenants]([Id]),
    [Email] NVARCHAR(256) NOT NULL,
    [NormalizedEmail] NVARCHAR(256) NOT NULL,
    [PhoneNumber] NVARCHAR(20) NULL,
    [PasswordHash] NVARCHAR(MAX) NULL,
    [SecurityStamp] NVARCHAR(MAX) NULL,
    [ConcurrencyStamp] NVARCHAR(MAX) NULL,
    [TwoFactorEnabled] BIT NOT NULL DEFAULT 0,
    [LockoutEnd] DATETIMEOFFSET NULL,
    [LockoutEnabled] BIT NOT NULL DEFAULT 1,
    [AccessFailedCount] INT NOT NULL DEFAULT 0,
    [Role] NVARCHAR(50) NOT NULL, -- 'Staff', 'Manager', 'Admin'
    [IsActive] BIT NOT NULL DEFAULT 1,
    [LastLoginAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT UK_AppUsers_Email UNIQUE ([TenantId], [Email])
);
```

| Column | Type | Notes |
|--------|------|-------|
| `Id` | UNIQUEIDENTIFIER | PK (extends ASP.NET Identity) |
| `TenantId` | UNIQUEIDENTIFIER | FK to Tenants |
| `Email` | NVARCHAR(256) | Login email, unique per tenant |
| `PasswordHash` | NVARCHAR(MAX) | Hashed password |
| `Role` | NVARCHAR(50) | 'Staff', 'Manager', 'Admin' |
| `IsActive` | BIT | Soft deactivation |
| `LastLoginAt` | DATETIME2 | Track last login |

---

## Relationships

### Foreign Keys

```sql
-- GymMembers → Tenants
ALTER TABLE [GymMembers] 
    ADD CONSTRAINT [FK_GymMembers_Tenants] 
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants]([Id]) ON DELETE CASCADE;

-- Memberships → GymMembers
ALTER TABLE [Memberships] 
    ADD CONSTRAINT [FK_Memberships_GymMembers] 
    FOREIGN KEY ([MemberId]) REFERENCES [GymMembers]([Id]) ON DELETE CASCADE;

-- Memberships → MembershipPlans
ALTER TABLE [Memberships] 
    ADD CONSTRAINT [FK_Memberships_MembershipPlans] 
    FOREIGN KEY ([MembershipPlanId]) REFERENCES [MembershipPlans]([Id]);

-- GymAttendance → GymMembers
ALTER TABLE [GymAttendance] 
    ADD CONSTRAINT [FK_GymAttendance_GymMembers] 
    FOREIGN KEY ([MemberId]) REFERENCES [GymMembers]([Id]) ON DELETE CASCADE;

-- MemberInvitations → GymMembers
ALTER TABLE [MemberInvitations] 
    ADD CONSTRAINT [FK_MemberInvitations_GymMembers] 
    FOREIGN KEY ([MemberId]) REFERENCES [GymMembers]([Id]) ON DELETE CASCADE;
```

### Cascade Delete Behavior

```
Tenant Deleted
    ↓
GymMembers (CASCADE)
    ↓
├─ Memberships (CASCADE)
├─ GymAttendance (CASCADE)
└─ MemberInvitations (CASCADE)

AppUsers (CASCADE)
```

---

## Indexes

### Performance Indexes

```sql
-- Search members by phone
CREATE INDEX [IX_GymMembers_TenantId_PhoneNumber] 
    ON [dbo].[GymMembers]([TenantId], [PhoneNumber]);

-- Check attendance history
CREATE INDEX [IX_GymAttendance_MemberId_CheckInTime] 
    ON [dbo].[GymAttendance]([MemberId], [CheckInTime] DESC);

-- Find active memberships
CREATE INDEX [IX_Memberships_TenantId_Status] 
    ON [dbo].[Memberships]([TenantId], [Status])
    WHERE [Status] = 'Active';

-- Tenant isolation
CREATE INDEX [IX_GymMembers_TenantId] 
    ON [dbo].[GymMembers]([TenantId]);

CREATE INDEX [IX_Memberships_TenantId] 
    ON [dbo].[Memberships]([TenantId]);

CREATE INDEX [IX_GymAttendance_TenantId] 
    ON [dbo].[GymAttendance]([TenantId]);

-- Invitation quota check (monthly)
CREATE INDEX [IX_MemberInvitations_MemberId_SentAt] 
    ON [dbo].[MemberInvitations]([MemberId], [SentAt] DESC);
```

---

## Constraints & Rules

### Data Validation Rules

| Table | Column | Rule | Example |
|-------|--------|------|---------|
| GymMembers | PhoneNumber | Format: +20XXXXXXXXXXX | +201234567890 |
| MembershipPlans | PriceEGP | >= 0 | 500.00 |
| Memberships | StartDate | Before EndDate | 2026-05-01 < 2026-06-01 |
| Memberships | EndDate | After StartDate | - |
| GymAttendance | CheckInTime | Before CheckOutTime (if set) | - |
| MembershipPlans | DurationDays | > 0 | 30 |
| MembershipPlans | SessionsPerMonth | >= 0 or NULL (unlimited) | NULL or 12 |

### Business Rules (Application Layer)

```csharp
// 1. Monthly Invitation Quota
// Enforce in service: SELECT COUNT(*) WHERE DATEPART(MONTH, SentAt) = MONTH(NOW())
// Max: 3 invitations per month per member

// 2. Membership Session Limit
// Check: SessionsUsedThisMonth < MembershipPlan.SessionsPerMonth
// Reset on 1st of month

// 3. Frozen Membership Check-in Block
// Before check-in: IF IsFrozen = 1 AND FrozenUntil > NOW() → REJECT

// 4. Membership Status
// Active: StartDate <= TODAY AND EndDate >= TODAY AND Status = 'Active'
// Expired: EndDate < TODAY
// Cancelled: Status = 'Cancelled'

// 5. Payment Gateway Idempotency
// Store ExternalRef (Paymob ID, Fawry RefNumber) to prevent duplicate processing
```

---

## Migration History

### Initial Create (20260505230815)

```powershell
dotnet ef migrations add InitialCreate --project GMS.Infrastructure --startup-project GMS.Api
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
```

**Creates:**
- ✅ Tenants
- ✅ AppUsers
- ✅ GymMembers
- ✅ MembershipPlans
- ✅ Memberships
- ✅ GymAttendance
- ✅ MemberInvitations

---

## Query Examples

### Find Active Members

```sql
SELECT gm.*
FROM GymMembers gm
WHERE gm.TenantId = @TenantId
  AND gm.ActiveMembershipId IS NOT NULL;
```

### Count Monthly Invitations

```sql
SELECT COUNT(*)
FROM MemberInvitations
WHERE MemberId = @MemberId
  AND TenantId = @TenantId
  AND YEAR(SentAt) = YEAR(GETUTCDATE())
  AND MONTH(SentAt) = MONTH(GETUTCDATE());
```

### Attendance Report (Last 7 Days)

```sql
SELECT 
    gm.FullName,
    COUNT(*) as CheckInCount,
    MAX(ga.CheckInTime) as LastCheckIn
FROM GymAttendance ga
JOIN GymMembers gm ON ga.MemberId = gm.Id
WHERE ga.TenantId = @TenantId
  AND ga.CheckInTime >= DATEADD(DAY, -7, GETUTCDATE())
GROUP BY gm.Id, gm.FullName
ORDER BY CheckInCount DESC;
```

---

**Database Version:** v1.0.0  
**Last Updated:** May 2, 2026
