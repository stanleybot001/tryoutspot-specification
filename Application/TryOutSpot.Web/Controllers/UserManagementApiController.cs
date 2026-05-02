using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.UserManagement;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Platform administrator endpoints for managing user accounts.
/// </summary>
[ApiController]
[Authorize(Roles = TryOutSpotRoles.PlatformAdmin)]
[Tags("User Management")]
[Produces("application/json")]
[Route("api/user-management")]
public sealed class UserManagementApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IAccountEmailSender accountEmailSender) : ControllerBase
{
    /// <summary>
    /// Lists users with optional search, account type, and active-state filters.
    /// </summary>
    /// <response code="200">Returns a paginated user list.</response>
    /// <response code="400">The account type filter is invalid.</response>
    /// <response code="401">The bearer token is missing or invalid.</response>
    /// <response code="403">The current user is not a platform administrator.</response>
    [HttpGet("users")]
    [ProducesResponseType<ManagedUserListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ManagedUserListResponse>> ListUsers(
        [FromQuery] string? search,
        [FromQuery] string? accountType,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(user =>
                (user.Email != null && user.Email.ToLower().Contains(normalizedSearch)) ||
                user.FirstName.ToLower().Contains(normalizedSearch) ||
                user.LastName.ToLower().Contains(normalizedSearch));
        }

        if (isActive.HasValue)
        {
            query = query.Where(user => user.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(accountType))
        {
            var normalizedRole = TryOutSpotRoles.NormalizeRole(accountType);
            if (normalizedRole is null)
            {
                ModelState.AddModelError(nameof(accountType), $"'{accountType}' is not a supported account type.");
                return ValidationProblem(ModelState);
            }

            var role = await dbContext.Roles
                .AsNoTracking()
                .SingleOrDefaultAsync(currentRole => currentRole.Name == normalizedRole, cancellationToken);

            if (role is null)
            {
                ModelState.AddModelError(nameof(accountType), $"'{accountType}' is not configured in the role store.");
                return ValidationProblem(ModelState);
            }

            var userIdsForRole = dbContext.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.RoleId == role.Id)
                .Select(userRole => userRole.UserId);

            query = query.Where(user => userIdsForRole.Contains(user.Id));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserListProjection(
                user.Id,
                user.Email ?? string.Empty,
                user.FirstName,
                user.LastName,
                user.IsActive,
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                user.CreatedAt,
                user.UpdatedAt))
            .ToListAsync(cancellationToken);

        var roleLookup = await GetRoleLookupAsync(users.Select(user => user.UserId), cancellationToken);
        var responseUsers = users
            .Select(user => new ManagedUserSummaryResponse(
                user.UserId,
                user.Email,
                user.FirstName,
                user.LastName,
                GetRolesForUser(roleLookup, user.UserId),
                user.IsActive,
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                user.CreatedAt,
                user.UpdatedAt))
            .ToArray();

        return Ok(new ManagedUserListResponse(
            responseUsers,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>
    /// Returns a single user account with administrative state.
    /// </summary>
    [HttpGet("users/{userId:guid}")]
    [ProducesResponseType<ManagedUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDetailResponse>> GetUser(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToDetailResponse(user, roles));
    }

    /// <summary>
    /// Creates an account as a platform administrator.
    /// </summary>
    [HttpPost("users")]
    [ProducesResponseType<ManagedUserDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ManagedUserDetailResponse>> CreateUser(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var accountTypes = GetValidatedAccountTypes(request.AccountTypes, nameof(request.AccountTypes));
        if (accountTypes.Count == 0)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var email = request.Email.Trim();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = request.EmailConfirmed,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = NormalizeOptional(request.PhoneNumber),
            PhoneNumberConfirmed = request.PhoneNumberConfirmed,
            DateOfBirth = request.DateOfBirth,
            ZipCode = NormalizeOptional(request.ZipCode),
            City = NormalizeOptional(request.City),
            State = NormalizeState(request.State),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return ValidationProblem(ModelState);
        }

        var roleResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            return ValidationProblem(ModelState);
        }

        await transaction.CommitAsync(cancellationToken);

        if (!user.EmailConfirmed)
        {
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);
        }

        var roles = await userManager.GetRolesAsync(user);
        return StatusCode(StatusCodes.Status201Created, ToDetailResponse(user, roles));
    }

    /// <summary>
    /// Updates editable profile fields for a user.
    /// </summary>
    [HttpPost("users/{userId:guid}/profile")]
    [ProducesResponseType<ManagedUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDetailResponse>> UpdateProfile(
        Guid userId,
        UpdateManagedUserProfileRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = NormalizeOptional(request.PhoneNumber);
        user.DateOfBirth = request.DateOfBirth;
        user.ZipCode = NormalizeOptional(request.ZipCode);
        user.City = NormalizeOptional(request.City);
        user.State = NormalizeState(request.State);
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToDetailResponse(user, roles));
    }

    /// <summary>
    /// Replaces a user's account type roles.
    /// </summary>
    [HttpPost("users/{userId:guid}/account-types")]
    [ProducesResponseType<ManagedUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDetailResponse>> UpdateAccountTypes(
        Guid userId,
        UpdateManagedUserAccountTypesRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var accountTypes = GetValidatedAccountTypes(request.AccountTypes, nameof(request.AccountTypes));
        if (accountTypes.Count == 0)
        {
            return ValidationProblem(ModelState);
        }

        if (IsCurrentUser(user.Id) && !accountTypes.Contains(TryOutSpotRoles.PlatformAdmin))
        {
            ModelState.AddModelError(
                nameof(request.AccountTypes),
                "Platform administrators cannot remove their own PlatformAdmin account type.");
            return ValidationProblem(ModelState);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var currentRoles = await userManager.GetRolesAsync(user);
        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded)
        {
            AddIdentityErrors(removeResult);
            return ValidationProblem(ModelState);
        }

        var addResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!addResult.Succeeded)
        {
            AddIdentityErrors(addResult);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return ValidationProblem(ModelState);
        }

        await transaction.CommitAsync();

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToDetailResponse(user, roles));
    }

    /// <summary>
    /// Updates email and phone verification flags for a user.
    /// </summary>
    [HttpPost("users/{userId:guid}/verification")]
    [ProducesResponseType<ManagedUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDetailResponse>> SetVerification(
        Guid userId,
        SetManagedUserVerificationRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.EmailConfirmed = request.EmailConfirmed;
        user.PhoneNumberConfirmed = request.PhoneNumberConfirmed;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToDetailResponse(user, roles));
    }

    /// <summary>
    /// Locks a user account until manually unlocked.
    /// </summary>
    [HttpPost("users/{userId:guid}/lock")]
    [ProducesResponseType<ManagedUserActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ManagedUserActionResponse>> LockUser(Guid userId, ManagedUserStateChangeRequest request)
    {
        return SetLockoutAsync(userId, locked: true, "The user account has been locked.");
    }

    /// <summary>
    /// Unlocks a user account.
    /// </summary>
    [HttpPost("users/{userId:guid}/unlock")]
    [ProducesResponseType<ManagedUserActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ManagedUserActionResponse>> UnlockUser(Guid userId, ManagedUserStateChangeRequest request)
    {
        return SetLockoutAsync(userId, locked: false, "The user account has been unlocked.");
    }

    /// <summary>
    /// Soft deactivates a user account.
    /// </summary>
    [HttpPost("users/{userId:guid}/deactivate")]
    [ProducesResponseType<ManagedUserActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserActionResponse>> DeactivateUser(
        Guid userId,
        ManagedUserStateChangeRequest request)
    {
        if (IsCurrentUser(userId))
        {
            ModelState.AddModelError(nameof(userId), "Platform administrators cannot deactivate their own account.");
            return ValidationProblem(ModelState);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        return Ok(new ManagedUserActionResponse("The user account has been deactivated."));
    }

    /// <summary>
    /// Reactivates a soft-deactivated user account.
    /// </summary>
    [HttpPost("users/{userId:guid}/reactivate")]
    [ProducesResponseType<ManagedUserActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserActionResponse>> ReactivateUser(
        Guid userId,
        ManagedUserStateChangeRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = true;
        user.LockoutEnabled = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        return Ok(new ManagedUserActionResponse("The user account has been reactivated."));
    }

    /// <summary>
    /// Sends a password reset email for a user account.
    /// </summary>
    [HttpPost("users/{userId:guid}/send-password-reset")]
    [ProducesResponseType<ManagedUserActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserActionResponse>> SendPasswordReset(
        Guid userId,
        ManagedUserStateChangeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
        {
            return NotFound();
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        await accountEmailSender.SendPasswordResetTokenAsync(user, resetToken, cancellationToken);

        return Ok(new ManagedUserActionResponse("Password reset instructions have been sent."));
    }

    private async Task<ActionResult<ManagedUserActionResponse>> SetLockoutAsync(
        Guid userId,
        bool locked,
        string successMessage)
    {
        if (locked && IsCurrentUser(userId))
        {
            ModelState.AddModelError(nameof(userId), "Platform administrators cannot lock their own account.");
            return ValidationProblem(ModelState);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.LockoutEnabled = true;
        user.LockoutEnd = locked ? DateTimeOffset.UtcNow.AddYears(100) : null;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        return Ok(new ManagedUserActionResponse(successMessage));
    }

    private async Task<IReadOnlyDictionary<Guid, string[]>> GetRoleLookupAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var requestedUserIds = userIds.ToArray();
        if (requestedUserIds.Length == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var roles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => requestedUserIds.Contains(userRole.UserId))
            .Join(
                dbContext.Roles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        return roles
            .GroupBy(role => role.UserId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(role => role.Name ?? string.Empty)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .OrderBy(role => role)
                    .ToArray());
    }

    private static string[] GetRolesForUser(IReadOnlyDictionary<Guid, string[]> roleLookup, Guid userId)
    {
        return roleLookup.TryGetValue(userId, out var roles) ? roles : [];
    }

    private List<string> GetValidatedAccountTypes(
        IReadOnlyCollection<string>? requestedAccountTypes,
        string modelStateKey)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
            ModelState.AddModelError(modelStateKey, "At least one account type is required.");
            return [];
        }

        var accountTypes = new List<string>();
        foreach (var requestedAccountType in requestedAccountTypes)
        {
            var accountType = TryOutSpotRoles.NormalizeRole(requestedAccountType);
            if (accountType is null)
            {
                ModelState.AddModelError(
                    modelStateKey,
                    $"'{requestedAccountType}' is not a supported account type.");

                continue;
            }

            if (!accountTypes.Contains(accountType, StringComparer.OrdinalIgnoreCase))
            {
                accountTypes.Add(accountType);
            }
        }

        return accountTypes;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }
    }

    private bool IsCurrentUser(Guid userId)
    {
        var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(currentUserIdClaim, out var currentUserId) && currentUserId == userId;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeState(string? state)
    {
        return string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
    }

    private static ManagedUserDetailResponse ToDetailResponse(User user, IEnumerable<string> roles)
    {
        return new ManagedUserDetailResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.DateOfBirth,
            user.ZipCode,
            user.City,
            user.State,
            roles.OrderBy(role => role).ToArray(),
            user.IsActive,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            user.LockoutEnabled,
            user.LockoutEnd,
            user.AccessFailedCount,
            user.CreatedAt,
            user.UpdatedAt);
    }

    private sealed record UserListProjection(
        Guid UserId,
        string Email,
        string FirstName,
        string LastName,
        bool IsActive,
        bool EmailConfirmed,
        bool PhoneNumberConfirmed,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
