using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class UserAdFrequency
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime? LastAdShown { get; set; }

    public int AdsShownToday { get; set; }

    public int AdsShownThisHour { get; set; }

    public DateTime LastResetDate { get; set; }

    public DateTime LastHourReset { get; set; }

    public bool IsAdFree { get; set; }

    public DateTime? AdFreeExpiresAt { get; set; }

    public virtual User User { get; set; } = null!;
}
