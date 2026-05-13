using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();
builder.Services.Configure<JwtTokenOptions>(builder.Configuration.GetSection(JwtTokenOptions.SectionName));
builder.Services.Configure<SocialLoginOptions>(builder.Configuration.GetSection(SocialLoginOptions.SectionName));
builder.Services.Configure<GoogleAuthenticationOptions>(builder.Configuration.GetSection(GoogleAuthenticationOptions.SectionName));
builder.Services.Configure<AccountEmailOptions>(builder.Configuration.GetSection(AccountEmailOptions.SectionName));
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection(ResendEmailOptions.SectionName));
builder.Services.Configure<AccountSmsOptions>(builder.Configuration.GetSection(AccountSmsOptions.SectionName));
builder.Services.Configure<TwilioSmsOptions>(builder.Configuration.GetSection(TwilioSmsOptions.SectionName));
builder.Services.Configure<StripeBillingOptions>(builder.Configuration.GetSection(StripeBillingOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
        builder.Environment.ContentRootPath,
        "App_Data",
        "DataProtection-Keys")));
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("TryOutSpotDatabase")));
}
builder.Services.AddIdentityCore<User>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
var jwtOptions = builder.Configuration.GetSection(JwtTokenOptions.SectionName).Get<JwtTokenOptions>() ?? new JwtTokenOptions();
var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddCookie(IdentityConstants.ExternalScheme, options =>
    {
        options.Cookie.Name = "TryOutSpot.ExternalLogin";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    })
    .AddCookie(TryOutSpotAuthenticationSchemes.WebCookie, options =>
    {
        options.Cookie.Name = "TryOutSpot.Web";
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userIdClaim, out var userId))
                {
                    context.Fail("The bearer token is missing a valid user id.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
                var user = await userManager.FindByIdAsync(userId.ToString());
                var tokenSecurityStamp = context.Principal?.FindFirstValue("security_stamp");
                if (user is not { IsActive: true }
                    || !string.Equals(user.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
                {
                    context.Fail("The bearer token is no longer valid.");
                }
            }
        };
    });

var googleAuthentication = builder.Configuration.GetSection(GoogleAuthenticationOptions.SectionName).Get<GoogleAuthenticationOptions>()
    ?? new GoogleAuthenticationOptions();
if (googleAuthentication.IsConfigured)
{
    authenticationBuilder.AddGoogle(TryOutSpotSocialLoginProviders.Google, options =>
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.ClientId = googleAuthentication.ClientId;
        options.ClientSecret = googleAuthentication.ClientSecret;
        options.CallbackPath = string.IsNullOrWhiteSpace(googleAuthentication.CallbackPath)
            ? "/signin-google"
            : googleAuthentication.CallbackPath;
        options.Events.OnCreatingTicket = context =>
        {
            if (context.User.TryGetProperty("email_verified", out var emailVerified)
                || context.User.TryGetProperty("verified_email", out emailVerified))
            {
                context.Identity?.AddClaim(new Claim("urn:google:email_verified", emailVerified.GetRawText().Trim('"')));
            }

            if (context.User.TryGetProperty("picture", out var picture))
            {
                context.Identity?.AddClaim(new Claim("urn:google:picture", picture.GetString() ?? string.Empty));
            }

            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/account/login?externalLoginStatus=failed");
            return Task.CompletedTask;
        };
    });
}

var facebookAuthentication = builder.Configuration.GetSection("Authentication:Facebook");
if (HasConfiguredValue(facebookAuthentication["AppId"]) && HasConfiguredValue(facebookAuthentication["AppSecret"]))
{
    authenticationBuilder.AddFacebook(TryOutSpotSocialLoginProviders.Facebook, options =>
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.AppId = facebookAuthentication["AppId"]!;
        options.AppSecret = facebookAuthentication["AppSecret"]!;
        options.CallbackPath = facebookAuthentication["CallbackPath"] ?? "/signin-facebook";
        options.Fields.Add("first_name");
        options.Fields.Add("last_name");
        options.Fields.Add("verified");
        options.Fields.Add("is_verified");
        options.Events.OnCreatingTicket = context =>
        {
            if (context.User.TryGetProperty("first_name", out var firstName))
            {
                context.Identity?.AddClaim(new Claim(ClaimTypes.GivenName, firstName.GetString() ?? string.Empty));
            }

            if (context.User.TryGetProperty("last_name", out var lastName))
            {
                context.Identity?.AddClaim(new Claim(ClaimTypes.Surname, lastName.GetString() ?? string.Empty));
            }

            if (context.User.TryGetProperty("verified", out var verified))
            {
                context.Identity?.AddClaim(new Claim("urn:facebook:email_verified", verified.GetRawText().Trim('"')));
            }
            else if (context.User.TryGetProperty("is_verified", out var isVerified))
            {
                context.Identity?.AddClaim(new Claim("urn:facebook:email_verified", isVerified.GetRawText().Trim('"')));
            }

            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/account/login?externalLoginStatus=failed");
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(TryOutSpotRateLimitPolicies.AccountSecurity, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            TryOutSpotRateLimitPolicies.GetPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
var emailProvider = builder.Configuration.GetValue<string>($"{AccountEmailOptions.SectionName}:Provider");
if (string.Equals(emailProvider, AccountCommunicationProviders.Resend, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IAccountEmailSender, ResendAccountEmailSender>();
}
else
{
    builder.Services.AddScoped<IAccountEmailSender, LoggingAccountEmailSender>();
}

var smsProvider = builder.Configuration.GetValue<string>($"{AccountSmsOptions.SectionName}:Provider");
if (string.Equals(smsProvider, AccountCommunicationProviders.Twilio, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IAccountSmsSender, TwilioAccountSmsSender>();
}
else
{
    builder.Services.AddScoped<IAccountSmsSender, LoggingAccountSmsSender>();
}

builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddSingleton<IExternalLoginTicketService, ExternalLoginTicketService>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<IStripeBillingService, StripeBillingService>();
builder.Services.AddScoped<IStripeSubscriptionSyncService, StripeSubscriptionSyncService>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        TryOutSpotAuthorizationPolicies.ActiveUser,
        TryOutSpotAuthorizationPolicyProvider.BuildAuthenticatedPolicy()
            .AddRequirements(new ActiveUserRequirement())
            .Build());
    options.AddPolicy(
        TryOutSpotAuthorizationPolicies.ConfirmedEmail,
        TryOutSpotAuthorizationPolicyProvider.BuildAuthenticatedPolicy()
            .AddRequirements(new ConfirmedEmailRequirement())
            .Build());
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, TryOutSpotAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, ActiveUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ConfirmedEmailAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, FeatureAccessAuthorizationHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TryOutSpot API v1");
    });
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static bool HasConfiguredValue(string? value)
{
    return !string.IsNullOrWhiteSpace(value)
        && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase);
}

public partial class Program;
