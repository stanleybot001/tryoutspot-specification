using TryOutSpot.Web.Billing;

namespace TryOutSpot.Web.Services;

public interface IEntitlementService
{
    Task<UserEntitlementSet?> GetEntitlementsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken);
}
