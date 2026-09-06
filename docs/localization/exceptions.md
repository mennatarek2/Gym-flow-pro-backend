# Localization exceptions register

Explicit remaining items that are **not** silent. Shrink as migrations land.

| File | Line / area | String / pattern | Classification | Reason | Why safe to remain |
|------|-------------|------------------|----------------|--------|-------------------|
| `Frontend/scripts/i18n/hardcoded-allowlist.json` → `apps/web/src/app/(dashboard)/` | many | English `toast('…')` | A | Progressive toast→catalog migration | CI still fails new toasts **outside** allowlist; inventory tracks remaining |
| `apps/web` `data-en`/`data-ar` pairs | HTML | Dual attributes | A (legacy) | Pre-catalog bilingual system | Functionally localized; migrate to `data-i18n` keys over time |
| `apps/platform-console/src/features/**` | pages | English UI copy | A | Large ops surface; nav/shell done first | Locale + RTL active; labels still EN until page pass |
| `GMS.Application/Services/**` | ~750 | `Failure("EN / AR")` | A | Catalog wave not finished | Slash bilingual already shows AR; AppError path ready |
| Permission keys e.g. `members.view` | JWT / authz | codes | B | Machine identifiers | Must not translate |
| Member/product/gym names | DTOs | user data | C | User-generated | Must not translate |
| `MemberOrder` Stage 0 | N/A | no invoice strings | B | Product scope | Unrelated to i18n |
| Technical logs | Serilog | English | B | Searchable logs | Per plan §28 |

No unclassified silent exceptions: everything remaining is either allowlisted (A pending), B technical, or C user data.
