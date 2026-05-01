using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Account registration, password recovery, and account lifecycle endpoints.
/// </summary>
[ApiController]
[Tags("Account")]
[Produces("application/json")]
[Route("api/account")]
public sealed class AccountApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IAccountEmailSender accountEmailSender) : ControllerBase
{
    /// <summary>
    /// Creates a new active user account with one or more account types.
    /// </summary>
    /// <remarks>
    /// Account types are additive. For example, the same user can register as both Parent and Coach.
    /// </remarks>
    /// <response code="201">Returns the created user account summary.</response>
    /// <response code="400">The request failed validation or Identity password rules.</response>
    [HttpPost("register")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserAccountResponse>> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var accountTypes = GetValidatedAccountTypes(request.AccountTypes);
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
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            DateOfBirth = request.DateOfBirth,
            ZipCode = string.IsNullOrWhiteSpace(request.ZipCode) ? null : request.ZipCode.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim().ToUpperInvariant(),
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

        return StatusCode(StatusCodes.Status201Created, ToResponse(user, accountTypes));
    }

    /// <summary>
    /// Starts the forgot-password flow for an active account.
    /// </summary>
    /// <remarks>
    /// The response is intentionally generic to avoid revealing whether an email address exists.
    /// </remarks>
    /// <response code="200">Always returns a generic password reset message.</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountActionResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is { IsActive: true })
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            await accountEmailSender.SendPasswordResetTokenAsync(user, resetToken, cancellationToken);
        }

        return Ok(new AccountActionResponse(
            "If an active account exists for that email address, password reset instructions will be sent."));
    }

    /// <summary>
    /// Resets an active account password using an ASP.NET Core Identity reset token.
    /// </summary>
    /// <response code="200">The password was reset.</response>
    /// <response code="400">The token, account, or new password is invalid.</response>
    [HttpPost("reset-password")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountActionResponse>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return BadRequest(new AccountActionResponse("The password reset request is invalid or expired."));
        }

        var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            AddIdentityErrors(resetResult);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return Ok(new AccountActionResponse("The password has been reset."));
    }

    /// <summary>
    /// Soft deletes an active account after password verification.
    /// </summary>
    /// <remarks>
    /// The user row is retained, marked inactive, locked, and receives a new security stamp.
    /// </remarks>
    /// <response code="200">The account was soft deleted.</response>
    /// <response code="400">The account could not be deleted.</response>
    [HttpPost("delete-account")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountActionResponse>> DeleteAccount(DeleteAccountRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return BadRequest(new AccountActionResponse("The account could not be deleted."));
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return BadRequest(new AccountActionResponse("The account could not be deleted."));
        }

        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return ValidationProblem(ModelState);
        }

        return Ok(new AccountActionResponse("The account has been deleted."));
    }

    /// <summary>
    /// Lists the public account types that can be selected during registration.
    /// </summary>
    /// <response code="200">Returns public registration account types.</response>
    [HttpGet("account-types")]
    [ProducesResponseType<AccountRoleResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<AccountRoleResponse>> GetAccountTypes()
    {
        var roles = TryOutSpotRoles.PublicRegistrationRoles
            .Select(role => new AccountRoleResponse(role))
            .ToArray();

        return Ok(roles);
    }

    private List<string> GetValidatedAccountTypes(IReadOnlyCollection<string>? requestedAccountTypes)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
            ModelState.AddModelError(
                nameof(RegisterUserRequest.AccountTypes),
                "At least one account type is required.");

            return [];
        }

        var accountTypes = new List<string>();
        foreach (var requestedAccountType in requestedAccountTypes)
        {
            var accountType = TryOutSpotRoles.NormalizePublicRegistrationRole(requestedAccountType);
            if (accountType is null)
            {
                ModelState.AddModelError(
                    nameof(RegisterUserRequest.AccountTypes),
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

    private static UserAccountResponse ToResponse(User user, IReadOnlyCollection<string> accountTypes)
    {
        return new UserAccountResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            accountTypes,
            user.IsActive);
    }
}
