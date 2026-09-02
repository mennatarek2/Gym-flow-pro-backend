# Staff web — local API must use HTTPS `:5001`

**Task:** W-04  
**Symptom:** Staff web login shows **Network error** while the API appears to be running.

## Cause

Staff web (`apps/web`) defaults to:

```text
https://localhost:5001/api
```

See `apps/web/src/app/shared/api-config.js` (`LOCAL_API`).

`dotnet run` **without** a launch profile often binds **http://localhost:5000** only. The browser then fails TLS/CORS to `:5001`.

## Correct local API start

From the API project directory:

```bash
dotnet run --launch-profile https
```

Confirm the console shows:

```text
Now listening on: https://localhost:5001
```

Then open staff web and sign in.

## Do not

- Point staff web at `http://localhost:5000` unless you intentionally change `gfp_api_base` / meta `gfp-api-base`.
- Assume HTTP and HTTPS are interchangeable for the desk.

## Related

- Swagger: `https://localhost:5001/swagger/ui`
- Certificate trust: `docs/deployment/HTTPS_SSL_CERTIFICATE_FIX.md`
- Release verify (staging/prod): `docs/deployment/RELEASE_VERIFY_CHECKLIST.md` (DO-02)
