using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class OpportunityGeographicTarget
{
    public Guid Id { get; set; }

    public Guid OpportunityId { get; set; }

    public string TargetZipCode { get; set; } = null!;

    public int RadiusMiles { get; set; }

    public decimal Cost { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Opportunity Opportunity { get; set; } = null!;
}
