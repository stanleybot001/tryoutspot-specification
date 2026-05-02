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

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            services.AddSingleton<TestAccountEmailSender>();
            services.AddScoped<IAccountEmailSender>(serviceProvider =>
                serviceProvider.GetRequiredService<TestAccountEmailSender>());

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();
        });
    }

    public async Task<Guid> RegisterUserAsync(string email, IReadOnlyCollection<string> accountTypes)
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
}
