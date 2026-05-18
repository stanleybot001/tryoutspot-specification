using Microsoft.AspNetCore.Identity;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

namespace TryOutSpot.Web.Services;

public static class BootstrapAdminSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger)
    {
        var options = configuration
            .GetSection(BootstrapAdminOptions.SectionName)
            .Get<BootstrapAdminOptions>() ?? new BootstrapAdminOptions();

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        if (!await roleManager.RoleExistsAsync(TryOutSpotRoles.PlatformAdmin))
        {
            var role = new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = TryOutSpotRoles.PlatformAdmin,
                NormalizedName = TryOutSpotRoles.PlatformAdmin.ToUpperInvariant(),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            var roleResult = await roleManager.CreateAsync(role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(BuildIdentityErrorMessage(
                    "Could not create the PlatformAdmin role.",
                    roleResult));
            }
        }

        var email = options.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        var now = DateTime.UtcNow;
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = NormalizeName(options.FirstName, "Platform"),
                LastName = NormalizeName(options.LastName, "Admin"),
                SmsConsentAccepted = false,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true,
                LockoutEnabled = true
            };

            var createResult = await userManager.CreateAsync(user, options.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(BuildIdentityErrorMessage(
                    "Could not create the bootstrap PlatformAdmin account.",
                    createResult));
            }
        }
        else
        {
            user.EmailConfirmed = true;
            user.IsActive = true;
            user.LockoutEnabled = true;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            user.UpdatedAt = now;
            if (string.IsNullOrWhiteSpace(user.FirstName))
            {
                user.FirstName = NormalizeName(options.FirstName, "Platform");
            }

            if (string.IsNullOrWhiteSpace(user.LastName))
            {
                user.LastName = NormalizeName(options.LastName, "Admin");
            }

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(BuildIdentityErrorMessage(
                    "Could not update the bootstrap PlatformAdmin account.",
                    updateResult));
            }

            var hasPassword = await userManager.HasPasswordAsync(user);
            if (!hasPassword)
            {
                var addPasswordResult = await userManager.AddPasswordAsync(user, options.Password);
                if (!addPasswordResult.Succeeded)
                {
                    throw new InvalidOperationException(BuildIdentityErrorMessage(
                        "Could not add a password to the bootstrap PlatformAdmin account.",
                        addPasswordResult));
                }
            }
            else if (options.ResetPassword)
            {
                var removePasswordResult = await userManager.RemovePasswordAsync(user);
                if (!removePasswordResult.Succeeded)
                {
                    throw new InvalidOperationException(BuildIdentityErrorMessage(
                        "Could not reset the bootstrap PlatformAdmin account password.",
                        removePasswordResult));
                }

                var addPasswordResult = await userManager.AddPasswordAsync(user, options.Password);
                if (!addPasswordResult.Succeeded)
                {
                    throw new InvalidOperationException(BuildIdentityErrorMessage(
                        "Could not set the bootstrap PlatformAdmin account password.",
                        addPasswordResult));
                }
            }
        }

        if (!await userManager.IsInRoleAsync(user, TryOutSpotRoles.PlatformAdmin))
        {
            var roleResult = await userManager.AddToRoleAsync(user, TryOutSpotRoles.PlatformAdmin);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(BuildIdentityErrorMessage(
                    "Could not add the PlatformAdmin role to the bootstrap account.",
                    roleResult));
            }
        }

        logger.LogInformation("Bootstrap PlatformAdmin account ensured for {Email}.", email);
    }

    private static string NormalizeName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string BuildIdentityErrorMessage(string prefix, IdentityResult result)
    {
        return $"{prefix} {string.Join(" ", result.Errors.Select(error => error.Description))}";
    }
}
