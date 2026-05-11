using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Subscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PlanType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? StripeCustomerId { get; set; }

    public string? StripeSubscriptionId { get; set; }

    public string? StripePriceId { get; set; }

    public DateTime? CurrentPeriodStart { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public DateTime? TrialEnd { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = null!;

    public string BillingInterval { get; set; } = null!;

    public bool CancelAtPeriodEnd { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsElite { get; set; }

    public virtual User User { get; set; } = null!;
}
