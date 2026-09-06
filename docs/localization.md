# GymFlowPro Localization

## Supported locales

| Code | Language | Direction |
|------|----------|-----------|
| `en` | English | LTR |
| `ar` | Arabic | RTL |

## Architecture

### Shared catalog

Package: [`Frontend/packages/i18n`](../Frontend/packages/i18n)

- Dictionaries: `locales/en.json`, `locales/ar.json`
- API: `t(key, params?, locale?)`, `statusLabel`, `formatMoney` / `formatNumber` / `formatDate` / `formatDateTime`
- Bilingual API helpers: `pickBilingual`, `splitSlashBilingual`, `displayBilingualText`, `tLabel`

### Staff web (`apps/web`)

- Runtime: `GfpI18n` in [`apps/web/src/app/shared/i18n.js`](../Frontend/apps/web/src/app/shared/i18n.js)
- Catalog IIFE: `shared/i18n-catalog.js` (generated — run `npm run i18n:sync`)
- Locale key: `localStorage.gfp_locale` (`en` | `ar`)
- RTL: `html[dir=rtl]` + `shared/rtl.css`
- Legacy: `data-en` / `data-ar` still supported during migration
- New: `data-i18n="common.save"` resolved via catalog; `GfpI18n.t('common.save')`

### Admin (`apps/admin`)

- Prefer `@gymflowpro/i18n` `t()` + Zustand `ui-store` locale (persisted `gymflowpro.ui`)
- Re-exports bilingual helpers from the shared package

### Platform console

- Same package; locale on `ui-store` + document `dir`/`lang`
- Status codes stay machine values (`trialing`, `active`, …); labels via `platform.subscription.status.*` / `status.*`

### Backend

- **Public API stays bilingual / dual-field** (`message` + `messageAr`, or `"EN / AR"` slash strings in `{ error }`) for Flutter compatibility
- Additive shape: `{ code, error, message, messageAr }` via `AppError`
- `Accept-Language` selects email/OTP template language only — does **not** strip `*Ar` DTO fields
- Resources: `GMS.Application/Localization/AppMessages.*.json` keyed by error code

## Key naming

```
common.save
members.title
members.errors.notFound
status.active
errors.generic
```

Do **not** use English phrases as keys.

## Classification (inventory)

| Type | Meaning |
|------|---------|
| A | Must localize (UI chrome, toasts, validation shown to users) |
| B | Technical (URLs, permission keys, HTTP, CSS, error codes) |
| C | User-generated (member/product/gym names, notes) — never translate |

## Adding a translation

1. Add the key to **both** `packages/i18n/locales/en.json` and `ar.json`
2. Run `npm run i18n:validate`
3. Run `npm run i18n:sync` (updates web catalog)
4. Use `t('your.key')` / `GfpI18n.t('your.key')`

## Suppressing the hardcoded-string checker

- Path allowlist: `Frontend/scripts/i18n/hardcoded-allowlist.json` (shrink over time)
- Inline: `// i18n-ignore-next-line` or `// i18n-ignore` on the same line

## CI

```bash
npm run i18n:validate
npm run i18n:hardcoded
npm run i18n:test
npm run i18n:inventory   # refresh docs/localization/inventory
```

Missing EN/AR keys, placeholder mismatches, or unexpected hardcoded toasts outside the allowlist **fail CI**.

## Financial / business rules

Locale changes **presentation only**. Calculations, decimals stored in DB, payment allocation, COGS, and inventory math must be identical under `en` and `ar`.

## RTL rules

- Set `html.dir` / `html.lang` globally — do not `text-align: right` every node
- Keep emails, URLs, IDs, phones as LTR islands where needed (`dir="ltr"`)
- Mirror directional icons only when meaning is directional
