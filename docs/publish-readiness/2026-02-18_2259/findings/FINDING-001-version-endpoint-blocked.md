# FINDING-001: Version Endpoint Blocked by API Authentication Middleware

| Field | Value |
|-------|-------|
| **ID** | FINDING-001 |
| **Date** | 2026-02-18 |
| **Category** | API / Middleware |
| **Severity** | LOW |

## Expected Behavior

`GET /api/v1/version` should return version info without authentication, as it has `.AllowAnonymous()` applied in `Program.cs` line ~1318.

## Actual Behavior

Returns HTTP 401 with `{"detail":"Missing X-API-Key header"}`.

## Evidence

```bash
$ curl -s http://localhost:5000/api/v1/version
# Returns 401 Unauthorized
```

**Program.cs** (line ~1318):
```csharp
app.MapGet("/api/v1/version", () => Results.Ok(new { ... })).AllowAnonymous();
```

**ApiAuthenticationMiddleware.cs** (line 28):
```csharp
if (!context.Request.Path.StartsWithSegments("/api"))
{
    await _next(context);
    return;
}
```

The middleware intercepts ALL `/api/*` paths before endpoint authorization runs. `/api/v1/version` is not listed in `IsInternalWebUiEndpoint()`.

## Root Cause

ASP.NET Core middleware executes before endpoint authorization. `ApiAuthenticationMiddleware` blocks any `/api/*` path without an API key unless it's in the `IsInternalWebUiEndpoint()` bypass list. The version endpoint was registered with `.AllowAnonymous()` but this attribute only affects the authorization middleware, not custom middleware that runs earlier in the pipeline.

## Fix Recommendation

Add `/api/v1/version` to `IsInternalWebUiEndpoint()` in `ApiAuthenticationMiddleware.cs`, or add a specific bypass before the API key check:

```csharp
// In IsInternalWebUiEndpoint():
if (path.StartsWithSegments("/api/v1/version", StringComparison.OrdinalIgnoreCase))
    return true;
```

Also add a dedicated anonymous bypass like the Game/Localization/Signup endpoints.

## Verification Plan

```bash
curl -s http://localhost:5000/api/v1/version | jq .
# Should return 200 with version JSON
```

## Confidence

**95%** — Reproduced at runtime, root cause confirmed in source.
