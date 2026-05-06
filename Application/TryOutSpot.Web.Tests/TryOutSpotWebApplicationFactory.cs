using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class TryOutSpotWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"TryOutSpotTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IAccountEmailSender>();
            services.RemoveAll<IAccountSmsSender>();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            services.AddSingleton<TestAccountEmailSender>();
            services.AddScoped<IAccountEmailSender>(serviceProvider =>
                serviceProvider.GetRequiredService<TestAccountEmailSender>());
            services.AddSingleton<TestAccountSmsSender>();
            services.AddScoped<IAccountSmsSender>(serviceProvider =>
                serviceProvider.GetRequiredService<TestAccountSmsSender>());

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();
        });
    }

    public async Task<Guid> RegisterUserAsync(
        string email,
        IReadOnlyCollection<string> accountTypes,
        bool smsConsentAccepted = true)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/account/register",
            new RegisterUserRequest
            {
                Email = email,
                Password = "Tryout2026",
                FirstName = "Taylor",
                LastName = "Morgan",
                PhoneNumber = "555-555-1234",
                SmsConsentAccepted = smsConsentAccepted,
                ZipCode = "73102",
                City = "Oklahoma City",
                State = "OK",
                AccountTypes = accountTypes
            });

        response.EnsureSuccessStatusCode();

        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);

        return user?.Id ?? throw new InvalidOperationException($"Test user {email} was not created.");
    }

    public async Task<User> CreateUserAsync(
        string email,
        IReadOnlyCollection<string> accountTypes,
        bool emailConfirmed = true,
        string password = "Tryout2026")
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = "Taylor",
            LastName = "Morgan",
            PhoneNumber = "555-555-1234",
            SmsConsentAccepted = true,
            SmsConsentAcceptedAt = now,
            SmsConsentText = TryOutSpotSmsConsent.CheckboxText,
            SmsConsentSource = TryOutSpotSmsConsent.ApiAccountRegistrationSource,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        var roleResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }

        return user;
    }

    public async Task<AuthTokenResponse> LoginAsPlatformAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await CreateUserAsync(email, [TryOutSpotRoles.PlatformAdmin]);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AuthTokenResponse>()
            ?? throw new InvalidOperationException("Platform admin login did not return tokens.");
    }

    public async Task ConfirmEmailAsync(string email)
    {
        var emailSender = Services.GetRequiredService<TestAccountEmailSender>();
        if (!emailSender.TryGetEmailConfirmationToken(email, out var token))
        {
            throw new InvalidOperationException($"No email confirmation token was captured for {email}.");
        }

        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/account/verify-email",
            new VerifyEmailRequest
            {
                Email = email,
                Token = token
            });

        response.EnsureSuccessStatusCode();
    }
}
