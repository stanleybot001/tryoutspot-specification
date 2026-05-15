using System;

namespace TryOutSpot.Web.Data.Entities;

public partial class StripeWebhookEvent
{
    public Guid Id { get; set; }

    public string StripeEventId { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string? StripeObjectId { get; set; }

    public DateTime ProcessedAt { get; set; }
}
