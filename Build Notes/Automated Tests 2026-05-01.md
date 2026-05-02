# Automated Tests 2026-05-01

## Test Project

Created `Application/TryOutSpot.Web.Tests` as an xUnit integration test project.

The tests use `WebApplicationFactory<Program>` with:

- ASP.NET Core test server
- EF Core in-memory database
- Fake password reset sender
- Real ASP.NET Core Identity services

The test environment is named `Testing`. `Program.cs` skips the normal PostgreSQL registration in that environment so tests cannot touch the real database.

## Covered Scenarios

Account API:

- Registration with multiple account types
- Rejection of unsupported roles
- Rejection of public `PlatformAdmin` registration
- Duplicate email rejection
- Weak password rejection
- Forgot-password non-enumeration response
- Valid reset token changes password
- Invalid reset token is rejected
- Valid delete request soft-deletes and locks the account
- Wrong delete password leaves account active
- Soft-deleted account cannot reset password

Entitlements:

- Billing plan and feature catalog endpoints return data
- Free parent gets free player/parent features only
- Active Premium Player subscription adds paid player features
- Past-due subscription does not grant paid features
- Active Team Professional subscription grants team features

## Verified Command

```powershell
dotnet test Application\TryOutSpot.Web.Tests\TryOutSpot.Web.Tests.csproj --no-restore --verbosity minimal
```

Result on 2026-05-01: 16 passed, 0 failed.
