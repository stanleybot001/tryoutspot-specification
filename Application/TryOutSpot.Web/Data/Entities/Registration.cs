using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Registration
{
    public Guid Id { get; set; }

    public Guid OpportunityId { get; set; }

    public Guid PlayerId { get; set; }

    public Guid RegisteredByUserId { get; set; }

    public string Status { get; set; } = null!;

    public string? RegistrationData { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public string? PaymentIntentId { get; set; }

    public decimal? Amount { get; set; }

    public string? MedicalInfo { get; set; }

    public string? EmergencyContactName { get; set; }

    public string? EmergencyContactPhone { get; set; }

    public bool WaiverSigned { get; set; }

    public DateTime? WaiverSignedAt { get; set; }

    public string? WaiverSignerName { get; set; }

    public string? AttendanceStatus { get; set; }

    public DateTime? CheckInTime { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Opportunity Opportunity { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;

    public virtual User RegisteredByUser { get; set; } = null!;
}
