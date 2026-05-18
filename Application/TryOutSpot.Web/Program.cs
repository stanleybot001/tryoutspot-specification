using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
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
const long ListingPdfMaxUploadBytes = 10L * 1024L * 1024L;
const long MultipartRequestLimitBytes = ListingPdfMaxUploadBytes + (2L * 1024L * 1024L);
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
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MultipartRequestLimitBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = MultipartRequestLimitBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MultipartRequestLimitBytes;
});
builder.Services.Configure<JwtTokenOptions>(builder.Configuration.GetSection(JwtTokenOptions.SectionName));
builder.Services.Configure<SocialLoginOptions>(builder.Configuration.GetSection(SocialLoginOptions.SectionName));
builder.Services.Configure<GoogleAuthenticationOptions>(builder.Configuration.GetSection(GoogleAuthenticationOptions.SectionName));
builder.Services.Configure<AccountEmailOptions>(builder.Configuration.GetSection(AccountEmailOptions.SectionName));
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection(ResendEmailOptions.SectionName));
builder.Services.Configure<AccountSmsOptions>(builder.Configuration.GetSection(AccountSmsOptions.SectionName));
builder.Services.Configure<TwilioSmsOptions>(builder.Configuration.GetSection(TwilioSmsOptions.SectionName));
builder.Services.Configure<StripeBillingOptions>(builder.Configuration.GetSection(StripeBillingOptions.SectionName));
builder.Services.Configure<R2StorageOptions>(builder.Configuration.GetSection(R2StorageOptions.SectionName));
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
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("TryOutSpotDatabase"),
            npgsqlOptions => npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
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
var webCookieDomain = builder.Configuration["Authentication:CookieDomain"];
var testFriendlyCookieSecurePolicy = builder.Environment.IsEnvironment("Testing")
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
var authenticationBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = TryOutSpotAuthenticationSchemes.BrowserOrApi;
        options.DefaultAuthenticateScheme = TryOutSpotAuthenticationSchemes.BrowserOrApi;
        options.DefaultChallengeScheme = TryOutSpotAuthenticationSchemes.BrowserOrApi;
    })
    .AddPolicyScheme(TryOutSpotAuthenticationSchemes.BrowserOrApi, displayName: null, options =>
    {
        options.ForwardDefaultSelector = context => SelectAuthenticationScheme(context);
    })
    .AddCookie(IdentityConstants.ExternalScheme, options =>
    {
        options.Cookie.Name = "TryOutSpot.ExternalLogin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = testFriendlyCookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    })
    .AddCookie(TryOutSpotAuthenticationSchemes.WebCookie, options =>
    {
        options.Cookie.Name = "TryOutSpot.Web";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = testFriendlyCookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        if (!string.IsNullOrWhiteSpace(webCookieDomain))
        {
            options.Cookie.Domain = webCookieDomain.Trim();
        }
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.Cookie.MaxAge = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (IsApiRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
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
builder.Services.AddScoped<IDashboardActivityService, DashboardActivityService>();
builder.Services.AddScoped<IZipRadiusSearchService, ZipRadiusSearchService>();
builder.Services.AddScoped<IStripeBillingService, StripeBillingService>();
builder.Services.AddScoped<IStripeSubscriptionSyncService, StripeSubscriptionSyncService>();
builder.Services.AddScoped<IAccountTypeChangeWorkflowService, AccountTypeChangeWorkflowService>();
builder.Services.AddScoped<IPdfStorageService, R2PdfStorageService>();
builder.Services.AddScoped<IImageStorageService, R2ImageStorageService>();
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
    options.AddPolicy(
        TryOutSpotAuthorizationPolicies.ManagePlayerProfile,
        TryOutSpotAuthorizationPolicyProvider.BuildAuthenticatedPolicy()
            .AddRequirements(new FeatureAccessRequirement(TryOutSpotFeatureCodes.CreateBasicPlayerProfiles))
            .Build());
    options.AddPolicy(
        TryOutSpotAuthorizationPolicies.ManageTeamProfile,
        TryOutSpotAuthorizationPolicyProvider.BuildAuthenticatedPolicy()
            .AddRequirements(new AnyFeatureAccessRequirement(
                TryOutSpotFeatureCodes.PostLimitedOpportunities,
                TryOutSpotFeatureCodes.UnlimitedOpportunityPostings))
            .Build());
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, TryOutSpotAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, ActiveUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ConfirmedEmailAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, FeatureAccessAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AnyFeatureAccessAuthorizationHandler>();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TryOutSpot.Startup");
await BootstrapAdminSeeder.SeedAsync(app.Services, app.Configuration, startupLogger);
if (args.Contains("--seed-admin", StringComparer.OrdinalIgnoreCase))
{
    return;
}

var r2Options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<R2StorageOptions>>().Value;
startupLogger.LogInformation(
    "R2 configuration loaded. Endpoint={Endpoint} Bucket={BucketName} AccountIdConfigured={HasAccountId} AccessKeyConfigured={HasAccessKey} SecretConfigured={HasSecret}",
    string.IsNullOrWhiteSpace(r2Options.Endpoint) ? "(missing)" : r2Options.Endpoint,
    string.IsNullOrWhiteSpace(r2Options.BucketName) ? "(missing)" : r2Options.BucketName,
    !string.IsNullOrWhiteSpace(r2Options.AccountId),
    !string.IsNullOrWhiteSpace(r2Options.AccessKeyId),
    !string.IsNullOrWhiteSpace(r2Options.SecretAccessKey));

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
    app.UseStatusCodePages(async context =>
    {
        var httpContext = context.HttpContext;
        if (httpContext.Response.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(
                "File upload is too large. Upload PDF files up to 10 MB.");
            return;
        }

        var isOpportunityRegistrationPost =
            httpContext.Response.StatusCode == StatusCodes.Status400BadRequest
            && HttpMethods.IsPost(httpContext.Request.Method)
            && httpContext.Request.Path.StartsWithSegments("/opportunities", StringComparison.OrdinalIgnoreCase)
            && httpContext.Request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true;
        if (isOpportunityRegistrationPost)
        {
            var statusLogger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("TryOutSpot.StatusCodes");
            statusLogger.LogWarning(
                "Team opportunity registration request returned 400. Path={Path} TraceIdentifier={TraceIdentifier} Authenticated={IsAuthenticated} Referer={Referer}",
                httpContext.Request.Path,
                httpContext.TraceIdentifier,
                httpContext.User?.Identity?.IsAuthenticated ?? false,
                httpContext.Request.Headers.Referer.ToString());

            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(
                "Registration could not be submitted. Refresh the opportunity page and try again. " +
                "If the issue continues, sign out and sign back in before submitting.");
        }
    });
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

static bool IsApiRequest(HttpRequest request)
{
    return request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}

static string SelectAuthenticationScheme(HttpContext context)
{
    if (IsApiRequest(context.Request))
    {
        return JwtBearerDefaults.AuthenticationScheme;
    }

    var authorizationHeader = context.Request.Headers.Authorization.ToString();
    return authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? JwtBearerDefaults.AuthenticationScheme
        : TryOutSpotAuthenticationSchemes.WebCookie;
}

public partial class Program;
