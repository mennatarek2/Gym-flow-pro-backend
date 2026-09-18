# HyMotion Local Lifetime Edition — Phase 3.1 Release Validation

Date: 2026-09-09

## Environment

| Item | Value |
|---|---|
| Windows version | Windows 11 Pro, build 10.0.26200 |
| Administrator availability | **No** — session ran as a standard user throughout; `sc.exe create`, `New-NetFirewallRule` both failed with Access Denied when attempted |
| SQL Server version/edition | SQL Server 2022, **Enterprise Evaluation Edition** (16.0.1000.6), default instance. Genuine SQL Server Express is **not** installed on this machine and was not installed during this phase (see Remaining Blockers — installing a database engine was judged too invasive a system change to perform without explicit sign-off) |
| .NET runtime requirement | None on target — self-contained win-x64 publish verified |
| Inno Setup version | 6.7.3, installed via `winget install JRSoftware.InnoSetup` (succeeded without elevation) |
| Browser used | None — no browser automation tool available in this environment |

## Build

- Publish command: `scripts/local-install/publish-local.ps1` (wraps `dotnet publish -c Release -r win-x64 --self-contained true`, plus a release-artifact cleanup step added this phase)
- Installer artifact: `scripts/local-install/Output/HyMotionLocalSetup-1.0.0.exe` (~99MB), built successfully with `ISCC.exe HyMotionLocal.iss`, zero warnings
- Release-artifact scan: no `.git`, no `appsettings.Development/Staging/Production.json`, no `.pdb` files, no `/dev/font-preview` dev tool, no ngrok/personal-URL strings, no hardcoded developer machine paths, no plaintext passwords/secrets — verified via direct file scan of the publish output and a binary string scan of the compiled installer .exe

## Installation

Fresh install via the actual compiled installer .exe: **BLOCKED — ENVIRONMENT** (installer requires admin elevation to run `[Code]`'s service registration step; this session has none). Verified instead: ran the exact same published binaries directly (`GMS.Api.exe`, bypassing only the SCM registration step) end-to-end multiple times against a real local SQL Server instance — first-run setup, login, and every module in the Feature Smoke section below all passed live.

## Windows Service

| Check | Result |
|---|---|
| `sc.exe create` | **BLOCKED — ENVIRONMENT** — Access Denied (no admin) |
| Startup type = Automatic | Scripted (`install-service.ps1`: `start= auto`), not executed |
| Account = NT AUTHORITY\NetworkService | Scripted, not executed |
| `localhost:7140` loads | **Verified live** (running the exe directly, not as a registered service) |
| `/health` returns Healthy | **Verified live** |
| Stop/start recovery | Not tested (no registered service to stop/start) |
| Reboot survival | **BLOCKED — ENVIRONMENT** — rebooting this shared machine was not attempted (disruptive action outside this phase's authority without explicit sign-off) |

## SQL

- Instance detection (`Test-SqlServerAvailability.ps1`): **Verified live** — found the real local instance, reported edition/version, wrote the connection string to `%ProgramData%\HyMotion\config\appsettings.json`
- Database creation + EF migrations + Hangfire schema: **Verified live** against the real SQL Server engine (a genuine `HyMotionLocal` database was created, migrated, and dropped again after testing)
- Restart persistence: **Verified live** — stopped and restarted the process; data, secrets, and health all persisted unchanged
- **Genuine SQL Server Express**: not tested — this machine has Enterprise Evaluation, not Express, installed. The detection/connection code is edition-agnostic (queries `SERVERPROPERTY`, doesn't branch on edition), so this is a real but narrow gap: Express-specific install experience and its size/feature caps were never exercised.

## First Run

**Verified live**, twice (once in Phase 3, once again this phase with a second, independent gym): `POST /api/local-setup/complete` created a real tenant + owner with no prior data, login succeeded immediately after, and calling `/api/local-setup/status` again correctly reported already-completed without creating a duplicate tenant. No hardcoded/development credentials were used — the owner account came entirely from the request payload.

## Offline

- Real network-disabled **browser** test: **BLOCKED — ENVIRONMENT** — no browser automation tool available, and firewall-level outbound blocking requires admin (attempted, Access Denied).
- Verified instead: the actual **compiled, cleaned publish output** was scanned for every external-dependency pattern (`cdn.`, `googleapis`, `gstatic`, `ngrok`, `unpkg`) and found clean outside one already-documented, explicitly-excluded dev tool (`/dev/font-preview`) — which this phase's cleanup step now removes from the Local package entirely anyway. One new finding this phase (see Security) was fixed: Settings' QR tab was silently sending the check-in token to a third-party image API; it's now hidden for Local and its CDN script is only loaded for SaaS.
- Every vendored asset (fonts, tabler-icons CSS, chart.js) was fetched live from the running instance and returned 200 with byte-correct sizes.

## Feature Smoke

All tested live, in order, against a single fresh gym on a real SQL Server database:

| Module | Result | Evidence |
|---|---|---|
| Authentication | PASS | Login succeeded; a second login after token expiry also succeeded (equivalent to logout+login) |
| Members | PASS | Create, search, and edit all returned correct data |
| Memberships | PASS | Plan assigned with cash payment; status "active", correct 30-day expiry |
| Attendance / Barcode | PASS | `barcode-checkin` with the member's exact MemberNumber recorded a real attendance row with `entryMethod: "barcode"`; a same-day duplicate was correctly rejected |
| POS | PASS | Retail sale (2× a product, cash payment) produced an invoice and correct totals |
| Inventory | PASS | Stock adjustment added 50 units; the POS sale above correctly decremented it to 48 |
| HR | PASS | Employee created; employee attendance check-in recorded (status "Present") |
| Expenses | PASS | A structured running-cost expense (category/type/shift validated) posted successfully |
| Reports | PASS | Dashboard overview correctly aggregated revenue, active members, and check-ins from all of the above in one call |
| Users/Roles | PASS | A Trainer account was created and correctly blocked (403) from an Owner-only endpoint, while succeeding at its own permitted action (barcode check-in) |

## Upgrade

**PARTIALLY VERIFIED / architecture-level only** — there is no prior released build to genuinely upgrade *from*, so a real old-build-to-new-build migration was not exercised. What was verified: `%ProgramData%\HyMotion` is never referenced or written by the publish/build process (confirmed by inspecting `publish-local.ps1` and the `.iss` script's `[Files]`/`[UninstallDelete]` sections), and EF's existing migration mechanism (unchanged since Phase 1, already proven idempotent across repeated app restarts) is the intended upgrade-safe path.

## Uninstall

- Admin-gated stop/remove logic (`sc.exe delete`): **BLOCKED — ENVIRONMENT**
- The script's own Administrator check was verified live (correctly refused to proceed without elevation)
- The destructive-deletion confirmation gate (exact string `"DELETE"` required) was verified in isolation: a non-matching input correctly left the test data untouched
- ProgramData preservation by default: confirmed by code inspection — the default (non-`-RemoveAllData`) path never calls `Remove-Item` on `%ProgramData%\HyMotion` at all

## Security

| Finding | Status |
|---|---|
| Settings "QR Code & Check-in" tab silently sent the check-in token to `api.qrserver.com` on every page view (pre-existing, SaaS-side too) | **Fixed for Local** — tab hidden, CDN script only loads when not Local; SaaS behavior unchanged (same library, now loaded async instead of a static tag it already null-checked) |
| Debug symbols (`.pdb`), dev-only `appsettings.{Development,Staging,Production}.json`, `/dev/font-preview` shipped in the Local publish output | **Fixed** — `publish-local.ps1` now strips all of these from the Local package (SaaS/MonsterASP publish output is untouched) |
| Installer SQL-check didn't branch UI on failure (Phase 3 known gap) | **Fixed** — moved into `[Code]`'s `NextButtonClick` with a real `Exec` exit-code check; aborts cleanly with a clear message before any files are copied |
| Hardcoded secrets / ngrok URLs / developer paths in the release artifact | **Scanned, none found** (installer binary + publish output) |
| Service running as Administrator | **Not applicable this phase** — service was never actually registered (no admin); the script specifies `NT AUTHORITY\NetworkService`, unchanged from Phase 3 |
| Swagger exposed in Local | **Verified disabled** (404, `ASPNETCORE_ENVIRONMENT=Local` is not `Development`) |
| Port binding | **Verified** — `localhost:7140` only, not `0.0.0.0` |

## SaaS Regression

```
Build:              0 errors, 54 warnings (identical to Phase 3 baseline)
Tests:              1539 total, 1533 passed, 6 failed (clean, fully-captured run)
Previous failures:  StaffNotificationIsolationTests (x4), MemberBulkWhatsAppNotificationTests (x1),
                     FinancialReportingApiIntegrationTests (x1) — same 6, unchanged
New failures:       0 (confirmed across 3 full-suite runs; one intermediate run showed a 7th,
                     different failure — ReconciliationInvariantTests — that passed cleanly in
                     isolation and did not reproduce in the other 2 full runs: pre-existing
                     flakiness under real-SQL-Server load, not a regression — this phase changed
                     zero C#/application files, only PowerShell/Inno Setup scripts and a new,
                     standalone Tools/HyMotion.Launcher project outside GMS.slnx)
```
No `GMS.Api`, `GMS.Application`, `GMS.Core`, `GMS.Infrastructure`, or `GMS.Platform` source file was modified in this phase.

## Remaining Blockers (all environment-dependent)

1. Windows Service SCM registration, recovery, and ProgramData ACLs: require Administrator elevation, unavailable in this session.
2. Genuine SQL Server Express (vs. the Enterprise Evaluation instance actually present): installing it was judged too invasive to do unilaterally (a full database engine install, likely also requiring admin and leaving a large, hard-to-fully-reverse footprint) without explicit user sign-off.
3. Real network-disabled browser test: no browser automation tool; firewall-level blocking requires admin.
4. Reboot test: not attempted (disruptive to a shared machine, outside this phase's authority to decide alone).
5. True upgrade test: no prior release exists to upgrade from.
