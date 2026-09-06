# Localization coverage report (2026-09-03)

Generated after Phase 0–4 foundation + first migration wave.
Inventory source: `docs/localization/inventory/summary.json` (heuristic Type A counts are upper bounds — many literals are technical/false positives).

## Localization Coverage

```text
Web user-facing strings discovered (heuristic): 938
Web localized (catalog keys + data-en/ar pages + GfpI18n.t wired): catalog 150 keys; ~743 data-en attrs pre-existing; shell/RTL/formatters/status maps live
Web remaining: toast-heavy *-app.js modules (~500 English toasts) + ~17 EN-only HTML shells — tracked via hardcoded-allowlist paths

Platform Console strings discovered (heuristic): 1840
Platform Console localized: nav groups/items, session banner, locale toggle, RTL dir, statusLabel helper
Platform Console remaining: feature page body copy (tenants/subscriptions/… panels) — allowlisted for progressive migration

Admin strings discovered (heuristic): 271
Admin localized: shared @gymflowpro/i18n package, TopBar logout/language, bilingual helpers + catalog selftest
Admin remaining: feature pages still on tLabel(en,ar) pairs — migrate to t(key) progressively

Backend user-facing strings discovered (heuristic): 762 Failure/WithMessage
Backend localized: AppError + AppMessageCatalog (EN/AR JSON), MemberActivate FV bilingual, MEMBER_NOT_FOUND attendance path, RequestLocale OTP templates, culture-invariant decimal test
Backend remaining: bulk Failure("EN / AR") sites (~750) — migrate to AppMessageCatalog codes in module waves
```

Catalog keys (required parity): **150** — `npm run i18n:validate` PASS (missing required translations = 0).

## Tests

```text
Existing tests: suite builds (stop GMS.Api if DLL lock)
Localization tests: LocalizationAppErrorTests — 6 passed
RTL tests: shell.selftest.js locale dir rtl/ltr + GfpI18n.t
Backend localization tests: AppError + OTP Accept-Language + culture math
Integration tests: deferred to module waves (logic covered by existing suite)
Hardcoded-string audit: npm run i18n:hardcoded PASS (allowlist = pending migration trees)
Build: npm run i18n:ci PASS
CI: .github/workflows/ci.yml job frontend-i18n + build-test
```

## Remaining Exceptions

See [exceptions.md](./exceptions.md).

## How to continue migration

1. Pick a module path out of `Frontend/scripts/i18n/hardcoded-allowlist.json`
2. Replace toasts/`tLabel` with `GfpI18n.t` / `t('key')` and add keys to both locale JSON files
3. `npm run i18n:validate && npm run i18n:sync`
4. Shrink allowlist
5. Backend: replace `Failure("…")` with `Failure(AppMessageCatalog.Get("CODE"))`
