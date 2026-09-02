# BJ-01 — Session generation monitoring

**Recurring job:** `activity-session-generation`  
**Implementation:** `GMS.Application/Jobs/SessionGenerationJob.cs`  
**Scheduler:** `SessionGenerationJobScheduler` registered via `AddHostedService` in `ApplicationServiceExtensions`  
**Cron:** `5 * * * *` (hourly at :05, Egypt Standard Time)

## What it does

For each active tenant:

1. `GenerateUpcomingSessionsAsync` — materialize upcoming class sessions from schedules  
2. `FinalizeElapsedSessionsAsync` — complete elapsed sessions / mark no-shows  

## Acceptance (BJ-01)

Classes are not empty when schedules exist for upcoming days.

## Monitor checklist

| Check | How | Pass |
| ----- | --- | ---- |
| Job registered | Hangfire dashboard → recurring `activity-session-generation` | Present |
| Recent runs | Hangfire succeeded / failed | No sustained failures |
| Tenant with schedules | Member Classes / staff Classes shows upcoming sessions after :05 | Non-empty when schedules exist |
| Logs | Search `SessionGenerationJob tenant` | created/no-show counts or quiet success |

## Failure response

1. Confirm Hangfire server is running with the API.  
2. Confirm tenant has active class schedules (not only one-off past sessions).  
3. Manually trigger `SessionGenerationJob.ExecuteAsync` from Hangfire if needed.  
4. If job fails per-tenant, inspect that tenant’s schedule/capacity data (do not clear other tenants).
