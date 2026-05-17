using System;

namespace TryOutSpot.Web.Data.Entities;

public partial class UserFavorite
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? PlayerListingId { get; set; }

    public Guid? OpportunityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Opportunity? Opportunity { get; set; }

    public virtual PlayerListing? PlayerListing { get; set; }

    public virtual User User { get; set; } = null!;
}
