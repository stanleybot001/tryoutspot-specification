using System;

namespace TryOutSpot.Web.Data.Entities;

public partial class ActivationAssistanceEvent
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? TeamId { get; set; }

    public string PromptKey { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Team? Team { get; set; }

    public virtual User User { get; set; } = null!;
}
