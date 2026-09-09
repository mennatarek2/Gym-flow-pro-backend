# Frontend (apps/web, apps/admin, apps/platform-console, packages/i18n) & Flutter/Member-App Audit

Scope: `D:\GMS\GMS\Frontend` (a separate git repository from the .NET backend at `D:\GMS\GMS`), plus the two Flutter planning documents at `D:\GMS\GMS\docs\flutter\`. This is a read-only audit produced for the "Local Lifetime Edition" feasibility study. No production code was modified. Every claim below cites a file path; anything that could not be verified from the repository is explicitly marked **UNKNOWN — REQUIRES VERIFICATION**.

Frontend repo top-level layout (`Frontend/`): `apps/web`, `apps/admin`, `apps/platform-console`, `packages/i18n`, `docs/`, `previews/`, `scripts/i18n/`, plus a large set of root-level `FLUTTER_*_PROMPT.md` / architecture-note markdown files that are planning documents, not source code.

---

## 1. apps/web — the staff dashboard

### 1.1 Framework reality vs. package.json

`Frontend/apps/web/package.json` lists Next.js/React/Tailwind as dependencies:

```
"dependencies": { "autoprefixer", "express": "^5.2.1", "lucide-react", "next": "^13.5.0",
                   "postcss", "react": "^18.2.0", "react-dom": "^18.2.0", "tailwindcss" }
```

and scripts `"build": "next build"`, `"start": "next start -p 3000"`, `"lint": "next lint"`. However, `Frontend/apps/web/server.js` (415 lines) is a hand-written Express app that serves static HTML/CSS/vanilla-JS files by reading them off disk with `fs.readFileSync` / `res.sendFile` (lines 118‑169, 207‑214, 253‑388) — there is no Next.js request handler, no React SSR, and no `next()`-based Next.js runtime anywhere in `server.js`. The actual dev/serve scripts (`"dev": "node server.js"`, `"serve": "node server.js"`) never invoke Next.js at all.

- **Only one JSX/TSX file exists in the whole app**: `Frontend/apps/web/src/app/(dashboard)/components/membership-status-card.jsx` (found by `find apps/web/src -name "*.tsx" -o -name "*.jsx"`). There is a sibling `membership-status-card.html` in the same directory, which is what `server.js`'s static routing actually serves — nothing in `server.js` compiles or imports the `.jsx` file. There is no other `.tsx`/`.jsx` under `apps/web/src`.
- `Frontend/apps/web/.next/` exists on disk (build-manifest.json, server/, static/, trace) confirming `next build` has been run at some point, but the runtime scripts (`dev`, `serve`, `dev:web` in the root `Frontend/package.json`) never call `next start`/`next dev` — they call `node server.js`.
- **Conclusion**: apps/web is a 100% server-rendered vanilla JS/HTML app via a custom Express server. Next.js, React, react-dom, lucide-react, tailwindcss, autoprefixer, postcss in `apps/web/package.json` are dead/vestigial dependencies for the actual runtime path (`node server.js`). The `build`/`start`/`lint`/`type-check` scripts are Next.js-shaped scaffolding left over from an earlier or abandoned migration attempt and are not part of the live app's serving path — only `dev`/`serve` (identical, both `node server.js`) are meaningful.

### 1.2 Routing (`Frontend/apps/web/server.js`)

All routing happens in Express middleware (no router library):

- `GET /` → `res.redirect('/dashboard/')` (line 217).
- `/shared/:file` → serves any file by basename from `src/app/shared/` (lines 207‑214) — used for the shared JS/CSS asset bundle.
- A single `app.use((req,res,next)=>{...})` middleware (lines 220‑391) does all page routing by string-matching `req.path`:
  - `/auth/*` → maps to `src/app/auth/**`, resolving `index.html` for directories, 301-redirecting non-trailing-slash directory paths (lines 223‑251).
  - `/dashboard*` → maps to `src/app/(dashboard)/**` (`DASH_ROOT`), same exact-file / directory-index logic, plus **dynamic segment support**: `/dashboard/members/<id>` resolves to `(dashboard)/members/[id]/index.html`, and a 3-level asset path `/dashboard/members/<id>/style.css` resolves to `(dashboard)/members/[id]/style.css` (lines 253‑321). This "[id]" folder-name convention mimics Next.js dynamic routes but is implemented by hand with `path.join` and `fs.existsSync`, not by Next.js.
  - `/member*` → maps to `src/app/member/**` (Member App / customer-facing pages), same pattern (lines 324‑356).
  - `/dev*` → maps to `src/app/dev/**` (font preview tooling, "not in customer nav", line 358).
  - Falls through to a hardcoded 404 HTML page (lines 393‑400).
- Every HTML response passes through `sendHtml()` (lines 118‑169), which injects: a viewport meta tag if missing, a `<meta name="gfp-api-base">` tag when `CONFIGURED_API_BASE` is set server-side, an early inline theme-boot `<style>`/`<script>` block (dark-mode flash prevention + branding CSS vars from localStorage, lines 139‑147), and any of the `SHARED_SCRIPTS`/`SHARED_STYLES` (or `MEMBER_SHARED_SCRIPTS`/`MEMBER_SHARED_STYLES` for `/member` pages) that the raw HTML file doesn't already reference (lines 45‑100, 149‑150).

### 1.3 Authentication / JWT storage

`Frontend/apps/web/src/app/shared/api-client.js`:
- Tokens are stored under keys `gfp_access_token`, `gfp_refresh_token`, `gfp_user`, `gfp_expires_at`, and a `gfp_persist` flag (lines 13‑19).
- Storage is **`localStorage` or `sessionStorage`**, chosen per-session by the `gfp_persist` flag (`'1'` = localStorage/"remember me", `'0'` = sessionStorage) — see `storeForWrite()` (lines 27‑35) and `writeBothClearOther()` (lines 41‑45), which always clears the token from the *other* store when writing. No cookies, no `httpOnly` storage — tokens are plain browser storage, readable by any script on the page (XSS-exposed by design, standard for this style of app).
- Silent refresh only triggers on a `Token-Expired: true` response header (not any 401) and is single-flighted via a shared `refreshPromise` (lines 140‑200); refresh failure calls `logoutToLogin()` once (no retry loop, lines 151‑159).
- Member App pages set `window.GFP_LOGIN_PATH = '/member/login/'` before this script loads so that an expired member session redirects to the member login instead of staff login (line 153‑155 comment).

### 1.4 API client / backend base URL resolution

Two layers:

1. **Server-side** (`Frontend/apps/web/server.js` lines 25‑39): `CONFIGURED_API_BASE` is computed from `process.env.GFP_API_BASE || process.env.NEXT_PUBLIC_API_URL || ''`, normalized to always end in `/api`, and — if non-empty — injected into every served HTML page as `<meta name="gfp-api-base" content="...">` (lines 135‑138). Env vars are loaded from `apps/web/.env` then `apps/web/.env.local` via a hand-rolled `loadEnvFile()` (lines 5‑23, 32‑33) — no `dotenv` package dependency is used.
2. **Client-side** (`Frontend/apps/web/src/app/shared/api-config.js`): `resolveDefaultBase()` (lines 21‑39) resolution order is: (a) the `<meta name="gfp-api-base">` tag injected by the server, (b) `localStorage.gfp_api_base` / `sessionStorage.gfp_api_base` (a documented dev override), (c) if `window.location.hostname` is `localhost`/`127.0.0.1`, hardcoded `LOCAL_API = 'https://localhost:5001/api'` (line 12), (d) otherwise `window.location.origin + '/api'` (same-origin production default). The resolved value is exposed globally as `window.API_BASE` and `window.GFP_DEFAULT_API_BASE` (lines 41‑46), and `api-client.js`'s `apiBase()` reads `global.API_BASE || global.GFP_DEFAULT_API_BASE` (api-client.js line 24).

**So the "official" mechanism is configurable** via `GFP_API_BASE`/`NEXT_PUBLIC_API_URL` env vars → meta tag → `window.API_BASE`, with sensible localhost/same-origin fallbacks and a documented `localStorage.gfp_api_base` dev override. However, **many individual page scripts bypass this and hardcode a specific ngrok tunnel URL as their own local fallback constant** instead of relying purely on `window.API_BASE`/`GfpApi` — see §1.6 below. This is an important nuance for the Local Lifetime Edition: the *intended* configuration path is env-var driven and already local/offline-friendly, but it is not used 100% consistently across every page script.

### 1.5 State management

No Redux/Zustand/MobX/etc. anywhere under `apps/web` (confirmed: `apps/web/package.json` dependencies list has no state library, and no `store`/`redux`/`zustand` files exist under `apps/web/src`). Each page is plain DOM manipulation: a page's own `*-app.js` file (e.g. `Frontend/apps/web/src/app/(dashboard)/pos/pos-app.js`, `.../members/[id]/member-detail.js`) does manual `document.querySelector`/`innerHTML`/event-listener wiring against data fetched via `GfpApi`/`fetch`. Shared cross-cutting concerns (nav, theme, i18n, toasts, session guard) are each their own small IIFE module attached to `window` (e.g. `window.GfpApi`, `window.GfpTheme`, `window.GfpI18n`) rather than a unified state container — see the full list of ~25 shared modules in `Frontend/apps/web/src/app/shared/` (api-client.js, theme.js, i18n.js, nav.js, shell.js, toast.js, session-guard.js, network-status.js, error-handler.js, analytics.js, etc.).

### 1.6 i18n architecture

- `Frontend/apps/web/src/app/shared/i18n.js` (13KB): manages the active locale in `localStorage['gfp_locale']` (`getLocale()`/`setLocale()`, lines 9‑24), sets `html.lang`/`html.dir`/`data-locale` and toggles `gfp-rtl`/`gfp-ltr` body classes (lines 26‑40), dispatches a `gfp:locale` custom event on change, and does key lookup + `{param}`/ICU-lite plural interpolation (`t()`, lines 46‑80+) against a catalog object supplied by a separate file, `shared/i18n-catalog.js` (21KB, loaded as its own shared script in `server.js`'s `SHARED_SCRIPTS`/`MEMBER_SHARED_SCRIPTS` lists).
- The same file also applies a `data-en`/`data-ar` attribute pattern (`applyDataLocaleAttributes`/`applyDataI18nAttributes`, referenced at i18n.js line 39) — static HTML elements can carry both English and Arabic text inline (e.g. `data-en="Save" data-ar="حفظ"`) and the locale switch swaps the visible text without a re-render/reload.
- **This is a separate system from `packages/i18n`** — see §4.

### 1.7 Theme system

`Frontend/apps/web/src/app/shared/theme.css` (11.7KB) defines a `:root` token palette (`--gfp-bg-app`, `--gfp-surface`, `--gfp-text`, etc., lines 8‑32) that maps to lightness-scale custom properties (`--lbg`, `--ls1`, `--ltp`, ...); an `html[data-theme="dark"]` block (starting line 34) overrides those lightness-scale variables for dark mode, and `color-scheme: dark` is set for native form-control theming. `Frontend/apps/web/src/app/shared/theme.js` manages the `light|dark|system` preference: stored per-browser in `localStorage['gfp_appearance']`, optionally scoped per-user (`gfp_appearance:<userId>`, lines 19‑22), resolved against `matchMedia('(prefers-color-scheme: dark)')` when set to `system` (lines 38‑43), and applied by setting `data-appearance`/`data-theme` attributes + `element.style.colorScheme` on `<html>` (lines 45‑56). `server.js` additionally inlines a "theme-boot" `<style data-gfp-theme-boot>` block at the very top of `<head>` (lines 139‑147) to avoid a light-mode flash before the deferred `theme.js` runs.

### 1.8 Hardcoded URL / localhost / ngrok sweep — `apps/web`

Aggressive grep for `localhost`, `127.0.0.1`, `ngrok`, and `https?://` across `apps/web` (excluding `node_modules`, `.next`). Findings:

**Committed dev fallback constants that hardcode a specific ngrok tunnel `https://reach-lullaby-tighten.ngrok-free.dev`** (a non-relative, non-localhost, third-party tunnel domain baked directly into source as an `||` fallback when `window.API_BASE` is falsy). Not guarded by any environment check — it is a plain literal fallback:

| File | Line(s) | Context |
|---|---|---|
| `apps/web/src/app/(dashboard)/trials/trials-app.js` | 6 | `const API_BASE = window.API_BASE \|\| 'https://reach-lullaby-tighten.ngrok-free.dev/api';` |
| `apps/web/src/app/(dashboard)/trials/index.html` | 99 | same pattern, inline `<script>` |
| `apps/web/src/app/(dashboard)/staff/index.html` | 119 | same pattern |
| `apps/web/src/app/(dashboard)/memberships/memberships-helpers.js` | 135 | same pattern |
| `apps/web/src/app/(dashboard)/promo-codes/promo-codes-app.js` | 3 | same pattern |
| `apps/web/src/app/(dashboard)/promo-codes/index.html` | 103 | same pattern |
| `apps/web/src/app/(dashboard)/attendance/attendance-app.js` | 300, 306 | API base fallback + SignalR hub URL fallback (`.../hubs/attendance`) |
| `apps/web/src/app/(dashboard)/pos/pos-app.js` | 214, 216, 244 | origin fallback + media/asset URL building |
| `apps/web/src/app/(dashboard)/members/[id]/member-detail.js` | 667 | same pattern |
| `apps/web/src/app/(dashboard)/inventory/suppliers/suppliers-app.js` | 137, 139 | same pattern |
| `apps/web/src/app/(dashboard)/settings/settings-app.js` | 28, 154, 155 | same pattern (origin + allowed-origins list builder) |
| `apps/web/src/app/(dashboard)/settings/index.html` | 331 | same pattern |
| `apps/web/src/app/(dashboard)/members/member-modals.js` | 73 | same pattern |
| `apps/web/src/app/(dashboard)/roles/index.html` | 82 | same pattern |
| `apps/web/src/app/(dashboard)/imports/imports-app.js` | 3 | same pattern |
| `apps/web/src/app/(dashboard)/imports/index.html` | 157 | same pattern |
| `apps/web/src/app/(dashboard)/inventory/products/products-app.js` | 358, 360, 1451 | same pattern (×2 origin + one API_BASE) |
| `apps/web/src/app/(dashboard)/member-orders/member-orders-app.js` | 156, 158 | same pattern |
| `apps/web/src/app/(dashboard)/inventory/purchase-orders/purchase-orders-app.js` | 69, 71 | same pattern |

In every case above, the pattern is `window.API_BASE || 'https://reach-lullaby-tighten.ngrok-free.dev/api'` (or the bare origin without `/api`). Because `shared/api-config.js` always sets `window.API_BASE` before page scripts run (server.js injects it near the top of `<head>`; see `SHARED_SCRIPTS` order in server.js lines 45‑67), this ngrok literal is a dormant fallback in normal operation — but it is still committed, non-relative, third-party-tunnel source code that a Local Lifetime Edition build must scrub, and it would activate if `api-config.js` ever failed to load/execute on a given page.

**Hardcoded `https://localhost:5001/api` fallback** (different literal, inconsistent with the ngrok fallback used elsewhere):
| File | Line | Context |
|---|---|---|
| `apps/web/src/app/shared/api-config.js` | 12 | `var LOCAL_API = 'https://localhost:5001/api';` — used only when `window.location.hostname` is `localhost`/`127.0.0.1` (line 35), i.e. a legitimate, guarded local-dev default. |
| `apps/web/src/app/(dashboard)/hr/documents/documents-app.js` | 13 | `var API_BASE = window.API_BASE \|\| 'https://localhost:5001/api';` — ungated fallback literal (same issue class as the ngrok ones, just a different URL). |
| `apps/web/src/app/(dashboard)/hr/employees/employees-app.js` | 13 | same pattern |

**Console-log-only occurrences of `http://localhost:${PORT}`** (not a runtime dependency, purely dev-server startup banner text): `apps/web/server.js` lines 407‑411 (the ASCII-art startup banner), including a fallback `'http://localhost:5000/api'` printed only when `CONFIGURED_API_BASE` is empty (line 411) — cosmetic, not a code path.

**Third-party/service URLs found in apps/web (not backend URLs, but non-relative external dependencies worth flagging individually):**
- `apps/web/src/app/(dashboard)/call-sheet/call-sheet-app.js:120` — `return 'https://wa.me/' + d;` (builds a WhatsApp deep link for member follow-up).
- `apps/web/src/app/(dashboard)/settings/settings-app.js:758` — `'https://api.qrserver.com/v1/create-qr-code/?size=220x220&margin=8&data=' + ...` — a **third-party QR-code-generation API** call (api.qrserver.com), a genuine external network dependency that would break under an offline "Local Lifetime Edition" unless replaced with a local QR generator.
- `apps/web/src/app/shared/api-config.js:9` (comment) references `your-domain.com` as documentation example text only, not a live URL.

**`.env` files** (not committed to git — see §6): `Frontend/apps/web/.env.local` currently on disk sets `NEXT_PUBLIC_API_URL=https://reach-lullaby-tighten.ngrok-free.dev` (matches the ngrok host hardcoded across the pages above — this is presumably how that literal ended up copy-pasted into so many files during development). `apps/web/.env.local` is excluded by `Frontend/.gitignore` (`.env` / `.env.*` with `!.env.example` exceptions) and does not appear in `git ls-files`, so it is a local, machine-specific dev artifact, not something distributed to other clones of the repo.

### 1.9 Production build process

- `apps/web/package.json` scripts: `dev`/`serve` → `node server.js` (the only scripts actually used to run the app); `build` → `next build`; `start` → `next start -p 3000`; `lint` → `next lint`; `type-check` → `tsc --noEmit`. Given §1.1's finding that the runtime is not Next.js, `build`/`start`/`lint` are **not part of the real deployment path** — running `next build` would build a Next.js app with essentially no pages (only the stray `.jsx` component), disconnected from what `server.js` actually serves.
- There is also `Frontend/apps/web/scripts/prepare-wwwroot.mjs`, wired to `npm run prepare:wwwroot` and `npm run publish:monsterasp` (`apps/web/package.json` lines 8‑9) — this suggests the real production deployment path is "copy the raw static `src/app/**` tree to a `wwwroot`-style folder for a host called MonsterASP," not a Next.js build. **UNKNOWN — REQUIRES VERIFICATION**: exact behavior of `prepare-wwwroot.mjs` was not read in full during this audit; recommend reviewing it directly if the Local Lifetime Edition needs to replicate the production artifact layout.
- Net effect: apps/web is served as raw, unbundled static files via `node server.js` (Express) at runtime. There is no minification/bundling/transpilation step in the actual serving path.

---

## 2. apps/admin

- **Purpose**: A parallel, in-progress React reimplementation of (part of) the staff dashboard. `Frontend/apps/admin/README.md` states: *"HyMotion Admin (Vite) — Prompt 1 — API client & auth infrastructure."* Implemented surface area (from `Frontend/apps/admin/src/`) is narrow: staff login (`features/auth/StaffLoginPage.tsx`), a member OTP stub page, a dashboard page, and members list/detail/form pages (`features/members/*`) — i.e. a small slice of what `apps/web` already covers in vanilla JS, not a full replacement yet.
- **Framework**: Real React 19 + Vite (not vestigial like apps/web) — `Frontend/apps/admin/package.json`: `react@^19.2.7`, `react-dom@^19.2.7`, `react-router-dom@^7.18.1`, `zustand@^5.0.14`, `@tanstack/react-query@^5.101.2`, `axios@^1.18.1`, `@gymflowpro/i18n` (workspace package — see §4). Build tool is Vite with `@vitejs/plugin-react` and `@tailwindcss/vite` (`Frontend/apps/admin/vite.config.ts`).
- **Build/serve**: `"dev": "vite"` (serves `http://localhost:5173` per `apps/admin/README.md`), `"build": "tsc -b && vite build"`, `"preview": "vite preview"`. This is a genuine Vite build pipeline (bundling, TS type-check, tree-shaking), unlike apps/web. A pre-built `Frontend/apps/admin/dist/` (index.html + one JS/CSS bundle) is present on disk **and is tracked in git** (`git ls-files apps/admin/dist` returns `apps/admin/dist/assets/index-4fj6FZtu.css`, `apps/admin/dist/assets/index-D2TYHetb.js`, `apps/admin/dist/index.html`) — a committed build artifact.
- **Backend**: Same backend API as apps/web. `Frontend/apps/admin/vite.config.ts` (lines 27‑44) proxies `/api` and `/hubs` (SignalR) to `VITE_API_PROXY_TARGET` in dev; `Frontend/apps/admin/src/lib/api/client.ts` builds requests against `import.meta.env.VITE_API_BASE_URL` (line 19) with the same bearer-token + `Token-Expired` silent-refresh pattern as apps/web's `api-client.js` (compare `client.ts` lines 94‑128 to `api-client.js` lines 239‑253). Session/token storage: `Frontend/apps/admin/src/lib/api/session.ts` stores the whole session object as one JSON blob under `localStorage['gymflowpro.session']` (line 3), distinct from apps/web's multi-key `gfp_*` scheme — **these two apps do not share a login session/localStorage schema**, so a user logged into apps/web is not automatically logged into apps/admin or vice versa. **UNKNOWN — REQUIRES VERIFICATION**: whether apps/admin is intended to fully replace apps/web or run alongside it long-term.
- **Hardcoded URLs**: `Frontend/apps/admin/vite.config.ts:11` — `const apiTarget = env.VITE_API_PROXY_TARGET || 'https://reach-lullaby-tighten.ngrok-free.dev'` (same ngrok tunnel literal as apps/web, used as the Vite dev-proxy default when `VITE_API_PROXY_TARGET` is unset). `Frontend/apps/admin/.env.example` also hardcodes the same ngrok URL as its documented default (lines 5‑6). `Frontend/apps/admin/.env` (gitignored, not tracked — confirmed via `git ls-files`) currently sets `VITE_API_PROXY_TARGET=https://reach-lullaby-tighten.ngrok-free.dev` on this machine. `Frontend/apps/admin/README.md:16` documents a *different* fallback (`https://localhost:7001`) than what's actually in `vite.config.ts` — a stale/inconsistent doc.

---

## 3. apps/platform-console

- **Purpose confirmed**: This is a distinct, internal cross-tenant admin console, not the per-gym staff dashboard. `Frontend/apps/platform-console/README.md`: *"Internal ops tool for GymFlow staff. **Separate auth** from the tenant admin dashboard (`apps/admin` / `apps/web`)"*; screens listed are Login/MFA setup/MFA challenge, a cross-tenant Tenants list (search/filter), and a read-only Tenant detail (subscription, history, invoices) — see `Frontend/apps/platform-console/src/features/tenants/*` (`TenantsListPage.tsx`, `TenantDetailPage.tsx`, `TenantBillingTab.tsx`, `TenantOverviewTab.tsx`, `HealthScorePanel.tsx`, `SubscriptionLifecyclePanel.test.ts`, etc.) plus platform-wide `AuditLogPage.tsx`, `MetricsPage.tsx`, `PlansPage.tsx` (billing plans), `PlatformUsersPage.tsx`, `RiskQueuePage.tsx`, `TrialsPage.tsx`, `UsagePage.tsx`.
- **Framework**: React 18.3 + Vite, `Frontend/apps/platform-console/package.json`: `react@^18.3.1`, `react-router-dom@^6.30.0`, `zustand@^5.0.14`, `@tanstack/react-query@^5.101.2`, `axios@^1.18.1`, `qrcode.react` (MFA QR display), `@gymflowpro/i18n`. Test stack: `vitest`, `msw` (API mocking), `@testing-library/*`, `axe-core` (a11y) — this app has real automated tests (`*.test.ts`/`*.msw.test.ts` files throughout `src/features/`), unlike apps/web (which has hand-rolled `*.selftest.js` scripts) or apps/admin (a couple of `*.selftest.ts` files only).
- **Build/serve**: `"dev": "vite"` (port 5174 per `vite.config.ts` line 22 and README), `"build": "tsc -b && vite build"`, supports mode-based builds (`npm run build -w platform-console -- --mode staging`). A pre-built `Frontend/apps/platform-console/dist/` is present and **tracked in git** (same as apps/admin — `git ls-files` confirms `apps/platform-console/dist/assets/index-CRU4NZmV.js`, `index-DU5F_P1n.css`, `index.html`).
- **Auth model differs sharply from apps/web/apps/admin**: `Frontend/apps/platform-console/src/lib/api/token.ts` — *"Platform access token lives in module memory only. Never write to localStorage/sessionStorage — cross-tenant blast radius."* (lines 1‑17); there is no refresh token at all per the README ("Access token is memory-only... There is no refresh token — expired sessions re-login"). This is a materially different, more conservative security posture than the other two apps' localStorage-persisted refresh-token flows.
- **Backend coupling / GMS.Platform**: The app's API calls are proxied under the path prefix `/platform-api` (`Frontend/apps/platform-console/vite.config.ts` lines 24‑30: `proxy: { '/platform-api': { target: apiTarget, ... } }`), and `README.md` documents platform-specific endpoints such as `POST /platform-api/auth/login`, `POST /platform-api/auth/mfa/setup`. This distinct `/platform-api` prefix and the cross-tenant feature set (tenant provisioning `ProvisionGymDialog.tsx`, tenant billing, staff/impersonation `ImpersonateButton.tsx`/`ImpersonationSessionBanner.tsx`) is consistent with this console assuming the existence of a separate backend surface (i.e., a "GMS.Platform"-style class library / controller set distinct from the per-tenant `GMS.Api` surface apps/web and apps/admin call under `/api`). This audit did not re-verify the backend project structure (out of scope — this is the frontend repo); treat the backend-side correspondence as **UNKNOWN — REQUIRES VERIFICATION (see the backend audit for GMS.Platform's actual controllers/migrations)**.
- **Hardcoded URLs**: `Frontend/apps/platform-console/vite.config.ts:12` — same `'https://reach-lullaby-tighten.ngrok-free.dev'` ngrok literal as apps/web and apps/admin, used as the dev-proxy default. `Frontend/apps/platform-console/.env.example` also hardcodes it (line 2). `Frontend/apps/platform-console/.env` (tracked? — checked: not in the `git ls-files .env` output, so gitignored/local) currently sets `VITE_API_PROXY_TARGET=https://localhost:5001/` and `VITE_TENANT_ADMIN_BASE_URL=https://localhost:5173/app` on this machine. **`.env.production`** (`VITE_API_BASE_URL=https://api.gymflow.pro`, `VITE_API_PROXY_TARGET=https://api.gymflow.pro`) and **`.env.staging`** (`https://staging-api.gymflow.pro`) **are committed to git** (`git ls-files` confirms both paths) — real production/staging backend domain names are checked into source control for this app. `Frontend/apps/platform-console/src/stores/impersonation-session-store.ts:66` hardcodes `'http://localhost:5173'` as a fallback for the tenant-admin base URL used when building impersonation hand-off links.

---

## 4. packages/i18n

`Frontend/packages/i18n` (`@gymflowpro/i18n`, `Frontend/packages/i18n/package.json`) is a **separate, newer i18n system from apps/web's own `shared/i18n.js` + `shared/i18n-catalog.js`** — they are not the same mechanism and should not be assumed interchangeable:

- `packages/i18n` is an ES-module npm workspace package (`"type": "module"`, `"main": "./src/index.js"`) exporting `t()`, `hasKey()`, `listKeys()`, `pickBilingual()` (`Frontend/packages/i18n/src/index.js`), reading locale catalogs from `Frontend/packages/i18n/locales/en.json` and `.../ar.json` via `fs.readFileSync` — i.e. it is Node/build-time-oriented (used from `apps/admin` and `apps/platform-console`, both of which list `"@gymflowpro/i18n": "*"` as a dependency in their `package.json`), and it also underpins `Frontend/scripts/i18n/*.mjs` tooling (`sync-web-catalog.mjs`, `validate.mjs`, `check-hardcoded.mjs`, `check-hymotion-branding.mjs`, wired into the root `Frontend/package.json`'s `i18n:*` scripts, e.g. `"i18n:sync": "node scripts/i18n/sync-web-catalog.mjs"`).
- apps/web's own `shared/i18n.js` + `shared/i18n-catalog.js` is a **browser-only, `window`-global IIFE system** with its own locale-storage key (`gfp_locale`) and its own `data-en`/`data-ar` DOM-attribute swap mechanism (§1.6) — it does not import from `packages/i18n` at runtime (apps/web's `package.json`, read in full at §1.1, lists no `@gymflowpro/i18n` dependency).
- The script name `i18n:sync` → `sync-web-catalog.mjs` strongly implies `packages/i18n`'s locale JSON is a **source of truth that gets synced into** apps/web's `shared/i18n-catalog.js` catalog (i.e. one shared translation-string source feeding two different runtime consumption mechanisms — the browser-global one for apps/web, and the ES-module one for apps/admin/apps/platform-console). This audit did not execute or fully trace `sync-web-catalog.mjs`'s logic; treat the exact sync mechanics as **UNKNOWN — REQUIRES VERIFICATION** if precise behavior matters for the offline edition.

---

## 5. Full external-URL sweep of the Frontend repo

A repo-wide grep for `https?://[a-zA-Z0-9._-]+` (excluding `node_modules`, `.next`, `.git`, `dist`, `package-lock.json`) across `apps/`, `packages/`, `scripts/`, `previews/`, `docs/` returned several hundred hits. The large majority are innocuous and are grouped rather than listed individually:

- **CDN/font/spec/doc links** (safe to ignore as a group): `fonts.googleapis.com`, `fonts.gstatic.com`, `developer.mozilla.org`, `w3.org`/`whatwg.org`/`tc39.es`/`ietf.org` spec references, `github.com`, `npmjs.com`, `nodejs.org`, `reactjs.org`/`react.dev`, `tailwindcss.com`, `eslint.org`, `testing-library.com`, and similar — these came almost entirely from license/comment headers inside vendored `node_modules` packages picked up incidentally, or from a couple of design/preview HTML files' `<link>` tags (e.g. `Frontend/design-system.html:7-9` → Google Fonts).
- **Placeholder/example domains** (not real): `example.com`, `example.org`, `test.example.org`, `your-domain.com` (documentation placeholder in `Frontend/apps/web/src/app/shared/api-config.js:9` comment and `Frontend/docs/CORS_PRODUCTION.md:10`).

**Individually flagged — real backend/API/service/payment-adjacent endpoints:**

| Domain | Where | Nature |
|---|---|---|
| `reach-lullaby-tighten.ngrok-free.dev` | `apps/web` (18+ files, §1.8), `apps/admin/vite.config.ts:11`, `apps/admin/.env.example:5-6`, `apps/platform-console/vite.config.ts:12`, `apps/platform-console/.env.example:2` | A live ngrok dev tunnel used repo-wide as the default backend target for local development across all three apps. Committed to source (in `.env.example` files and `vite.config.ts` fallbacks), not just local `.env`. |
| `api.gymflow.pro` | `apps/platform-console/.env.production` (tracked in git) | Real production backend domain for the platform console. |
| `staging-api.gymflow.pro` | `apps/platform-console/.env.staging` (tracked in git) | Real staging backend domain. |
| `api.qrserver.com` | `apps/web/src/app/(dashboard)/settings/settings-app.js:758` | Third-party QR-code image generation API called directly from the browser — an external network dependency with no local/offline fallback found in this file. |
| `wa.me` | `apps/web/src/app/(dashboard)/call-sheet/call-sheet-app.js:120` | WhatsApp deep-link builder (`https://wa.me/<phone>`) — external service dependency, though it only needs to work when the staff member's device opens WhatsApp, not a server call. |
| `images.unsplash.com` | Found in the raw domain sweep (exact file not isolated in this pass) | Likely a placeholder/demo image URL in a preview or seed-data file — **UNKNOWN — REQUIRES VERIFICATION** of exact file/line and whether it's live production code or only a `previews/*.html` mockup. |
| `api.gymflowpro.com` / `staging-api.gymflowpro.test` / `api.gymflowpro.test` | Only in root-level `FLUTTER_*_PROMPT.md` / `flutter_member_app_prompts.md` planning documents and `docs/flutter/FLUTTER_INTEGRATION_GUIDE.md:770-771` | Planning-document sample code only — **not implemented anywhere in `apps/`** (no Dart/Flutter project exists — see §6/Flutter section below). |
| `10.0.2.2` | Root-level Flutter prompt markdown files (e.g. `FLUTTER_MEMBER_APP_PROMPT.md`) | The standard Android-emulator loopback alias, again only in planning docs, not in any app source. |

---

## 6. Environment/runtime configuration — pointing at a different backend

| App | Mechanism today | Already configurable? |
|---|---|---|
| **apps/web** | `GFP_API_BASE` or `NEXT_PUBLIC_API_URL` env var (read server-side in `server.js` lines 37‑39 from `apps/web/.env`/`.env.local`, loaded by a hand-rolled parser) → injected as `<meta name="gfp-api-base">` → read client-side by `api-config.js`'s `resolveDefaultBase()` → exposed as `window.API_BASE`. There is also a documented pure-client override via `localStorage.gfp_api_base` (no server restart needed) per the comment header in `api-config.js` lines 1‑10. **Yes — the intended path is already environment-variable-driven** and would support pointing at `http://localhost:5000`, a LAN IP like `http://192.168.1.50:5000`, or a cloud URL simply by setting `GFP_API_BASE` and restarting `node server.js` (or setting `localStorage.gfp_api_base` for a quick client-only override). The caveat is §1.8: a long tail of individual page scripts have an ngrok-URL literal as their own local `\|\|` fallback, which only matters if `api-config.js` fails to set `window.API_BASE` (not the normal case) — but those literals would need to be scrubbed/audited for a clean offline build regardless, since they are non-relative committed URLs. |
| **apps/admin** | `VITE_API_BASE_URL` (used directly as axios `baseURL`, `apps/admin/src/lib/api/client.ts:19`) and `VITE_API_PROXY_TARGET` (Vite dev-server proxy target for `/api` and `/hubs`, `apps/admin/vite.config.ts:11,27-44`) — both are build-time Vite env vars read from `apps/admin/.env`/`.env.example`. **Yes, configurable** the standard Vite way (copy `.env.example` → `.env`, per `apps/admin/README.md`), but the *default* baked into `vite.config.ts` when the env var is unset is the ngrok tunnel, not localhost — a production/offline build must explicitly set `VITE_API_BASE_URL` (for a built `dist/`) since Vite proxy only applies to the dev server, not the built static bundle. |
| **apps/platform-console** | Same Vite env-var pattern: `VITE_API_BASE_URL`, `VITE_API_PROXY_TARGET`, plus `VITE_TENANT_ADMIN_BASE_URL` (used to build impersonation links back to apps/admin/apps/web, `impersonation-session-store.ts:66`). Distinct `.env`/`.env.production`/`.env.staging` files already model per-environment backend URLs (`apps/platform-console/README.md`'s table). **Yes, configurable** by design (mode-based builds: `vite build --mode staging`), and this app already has the cleanest per-environment separation of the three apps — but its production/staging URLs are committed to git (§3), which itself is not a Local-Lifetime-Edition blocker (they're just defaults) but is worth noting as a source-control hygiene point. |

For a Local Lifetime Edition (offline, single-tenant, no cloud dependency), the main concrete blockers found in this audit are: (1) the third-party QR API call in `apps/web/.../settings-app.js:758` (needs a local QR generator), (2) the WhatsApp deep link in `call-sheet-app.js:120` (inherently online/device-dependent, likely acceptable to leave as-is or feature-flag off), and (3) the scattered ngrok-URL literal fallbacks across ~18 apps/web page scripts (§1.8) which should be replaced with a purely env/meta-tag-driven default (or simply removed, since `window.API_BASE` is already reliably set before those scripts run) to avoid ever shipping a hardcoded external tunnel URL in an offline build.

---

## 7. Flutter / Member mobile app

**No Flutter/Dart source code exists anywhere on this machine.** A search of `D:\GMS\GMS` (both the `Frontend` repo and the parent GMS workspace) found no `pubspec.yaml`, no `.dart` files, and no Flutter project directory. What exists instead is documentation:

- `D:\GMS\GMS\docs\flutter\FLUTTER_INTEGRATION_GUIDE.md` (797 lines) — explicitly headed: *"⚠️ STATUS (REM-F11): The Flutter member app described here is SPEC ONLY — no Dart project exists in this repository. Backend endpoints are implemented; the client is not."* (line 3). Contents are illustrative Dart code snippets (not a real project) covering: `AuthService` (member phone-OTP login using `flutter_secure_storage` for token storage, lines 37‑129), QR check-in (`QrCheckinService` using `mobile_scanner`, lines 145‑229), a generic `ApiClient` HTTP wrapper (lines 238‑337), member profile/attendance/membership data services and DTOs (lines 341‑498), example UI widgets (`QrScannerScreen`, `MemberProfileScreen`, lines 504‑725), error-handling conventions (lines 729‑761), and an `ApiConfig` class showing dev/staging/prod base URLs selected via Dart's `String.fromEnvironment('ENV')` (lines 765‑786). Suggested `pubspec.yaml` dependencies (lines 9‑24): `http`, `flutter_secure_storage`, `jwt_decoder`, `provider`, `connectivity_plus`, `intl`, `dio` (optional), plus `mobile_scanner`, `qr_flutter`, `sqflite` (mentioned line 794‑796 for local caching).
- `D:\GMS\GMS\docs\flutter\FRONTEND_FLUTTER_API_DOCUMENTATION.md` (2,410 lines) — same disclaimer at line 3: *"This document describes a Flutter member app that is SPECIFICATION ONLY — no Flutter/Dart project exists in this repository. The backend endpoints described here are implemented and verified; the mobile client is not."* This is the fuller API contract reference the planned app would depend on: Authentication Overview (staff email/password + member phone-OTP, JWT with 15‑minute access / 30‑day refresh lifetime, `Token-Expired: true` header convention, JWT claims `sub`/`email`/`role`/`tenant_id`/`member_id`/`gym_code`, lines 40‑72), Authorization Policies/Roles (lines 75‑85), a standard `ErrorResponse` DTO and HTTP status code conventions (lines 87‑120), then a full **API Endpoints by Domain** catalog covering 13 controllers — Auth, Members, Memberships, Membership Plans, Attendance/Check-in, Admin/Staff Management, Tenant Settings, Notifications, Invitations, Analytics, Reports, Payments (Webhooks), Health (lines 122‑1578) — followed by a DTO reference section (lines 1578‑2128), enums (lines 2128‑2195), a SignalR real-time section (lines 2195‑2210), rate limiting (2210‑2222), and 10 numbered "Flutter Integration Tips" (HTTP client setup, token storage, auto-refresh, `DateOnly`/`TimeOnly` serialization quirks, enum values, bilingual fields, QR check-in flow, member OTP flow, SignalR usage — lines 2222‑2354), ending in a full endpoint quick-reference (line 2354+). The document states the API base URL is `https://your-domain.com` in production or `http://localhost:5000` for local dev (line 5), with Swagger available at the root URL in development (line 9).
- The `Frontend` repo root additionally contains ~20 `FLUTTER_*_PROMPT.md` files (e.g. `FLUTTER_MEMBER_APP_PROMPT.md`, `FLUTTER_MEMBER_ATTENDANCE_PROMPT.md`, `FLUTTER_QR_ATTENDANCE_PROMPT.md`, `FLUTTER_EMPLOYEE_APP_PROMPT.md`, etc.) and one `flutter_member_app_prompts.md` — these read as prompt/spec documents intended to brief a future coding agent or developer building the Flutter app feature-by-feature; they were not found to carry the same "SPEC ONLY" disclaimer banner as the two `docs/flutter/` documents, but no corresponding Dart implementation exists for any of them either. **UNKNOWN — REQUIRES VERIFICATION** whether these root-level prompt files are current/authoritative or superseded drafts, and whether an actual Flutter client repository exists elsewhere (not present on this machine).

**Conclusion for the Local Lifetime Edition feasibility study**: A genuine Flutter code audit is impossible in this workspace — there is no client to audit. The dependency surface the *planned* Member app would need (per the two docs above) is: JWT bearer auth against `/api/auth/*` (staff + member-OTP flows), a same 15‑min-access/30‑day-refresh token lifecycle as the web apps, `flutter_secure_storage` for token persistence, `mobile_scanner`/`qr_flutter` for QR check-in, `sqflite` for local caching, `connectivity_plus` for offline detection, and SignalR for real-time attendance — all of which point at the same `GMS.Api` backend surface the web apps consume, with no Flutter-specific backend already implemented beyond what's documented as already live. Status: **UNKNOWN — REQUIRES VERIFICATION (Flutter app likely lives in a separate repository not present on this machine).**

---

## Summary of key file citations

- Runtime/routing: `Frontend/apps/web/server.js`
- Vestigial Next.js manifest: `Frontend/apps/web/package.json`, `Frontend/apps/web/next.config.js`, `Frontend/apps/web/.next/`
- Only JSX in the app (unused by runtime): `Frontend/apps/web/src/app/(dashboard)/components/membership-status-card.jsx`
- Auth/token storage: `Frontend/apps/web/src/app/shared/api-client.js`
- API base resolution: `Frontend/apps/web/src/app/shared/api-config.js`
- i18n (apps/web): `Frontend/apps/web/src/app/shared/i18n.js`, `Frontend/apps/web/src/app/shared/i18n-catalog.js`
- Theme: `Frontend/apps/web/src/app/shared/theme.css`, `Frontend/apps/web/src/app/shared/theme.js`
- Shared i18n package: `Frontend/packages/i18n/src/index.js`, `Frontend/packages/i18n/package.json`
- apps/admin: `Frontend/apps/admin/package.json`, `Frontend/apps/admin/vite.config.ts`, `Frontend/apps/admin/src/lib/api/client.ts`, `Frontend/apps/admin/src/lib/api/session.ts`
- apps/platform-console: `Frontend/apps/platform-console/package.json`, `Frontend/apps/platform-console/vite.config.ts`, `Frontend/apps/platform-console/src/lib/api/token.ts`, `Frontend/apps/platform-console/.env.production`, `Frontend/apps/platform-console/.env.staging`
- Flutter docs: `D:\GMS\GMS\docs\flutter\FLUTTER_INTEGRATION_GUIDE.md`, `D:\GMS\GMS\docs\flutter\FRONTEND_FLUTTER_API_DOCUMENTATION.md`
