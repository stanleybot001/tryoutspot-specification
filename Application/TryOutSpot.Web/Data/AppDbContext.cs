using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

namespace TryOutSpot.Web.Data;

public partial class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Comment> Comments { get; set; }

    public virtual DbSet<Medium> Media { get; set; }

    public virtual DbSet<Opportunity> Opportunities { get; set; }

    public virtual DbSet<OpportunityGeographicTarget> OpportunityGeographicTargets { get; set; }

    public virtual DbSet<Organization> Organizations { get; set; }

    public virtual DbSet<Player> Players { get; set; }

    public virtual DbSet<PlayerListing> PlayerListings { get; set; }

    public virtual DbSet<PlayerSport> PlayerSports { get; set; }

    public virtual DbSet<PendingAccountTypeChange> PendingAccountTypeChanges { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<Registration> Registrations { get; set; }

    public virtual DbSet<Sport> Sports { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<StripeWebhookEvent> StripeWebhookEvents { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<TeamSport> TeamSports { get; set; }

    public virtual DbSet<UserAdFrequency> UserAdFrequencies { get; set; }

    public virtual DbSet<UserOauthProvider> UserOauthProviders { get; set; }

    public virtual DbSet<UserPlayerRelationship> UserPlayerRelationships { get; set; }

    public virtual DbSet<UserTeamRole> UserTeamRoles { get; set; }

    public virtual DbSet<ZipCodeGeography> ZipCodeGeographies { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasIndex(e => e.ParentCommentId, "IX_Comments_ParentCommentId");

            entity.HasIndex(e => e.PostId, "IX_Comments_PostId");

            entity.HasIndex(e => e.UserId, "IX_Comments_UserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.ParentComment).WithMany(p => p.InverseParentComment)
                .HasForeignKey(d => d.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Post).WithMany(p => p.Comments).HasForeignKey(d => d.PostId);

            entity.HasOne(d => d.User).WithMany(p => p.Comments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Medium>(entity =>
        {
            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "IX_Media_EntityType_EntityId");

            entity.HasIndex(e => e.IsActive, "IX_Media_IsActive");

            entity.HasIndex(e => e.IsFeatured, "IX_Media_IsFeatured");

            entity.HasIndex(e => e.MediaType, "IX_Media_MediaType");

            entity.HasIndex(e => e.UploadedByUserId, "IX_Media_UploadedByUserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.EntityType).HasMaxLength(50);
            entity.Property(e => e.MediaType).HasMaxLength(50);
            entity.Property(e => e.MimeType).HasMaxLength(100);
            entity.Property(e => e.ThumbnailUrl).HasMaxLength(500);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Url).HasMaxLength(500);

            entity.HasOne(d => d.UploadedByUser).WithMany(p => p.Media)
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Opportunity>(entity =>
        {
            entity.HasIndex(e => e.SportId, "IX_Opportunities_SportId");

            entity.HasIndex(e => e.TeamId, "IX_Opportunities_TeamId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.AgeGroup).HasMaxLength(50);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CompetitionLevel).HasMaxLength(100);
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.ContactPhone).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.GenderRequirement).HasMaxLength(20);
            entity.Property(e => e.Location).HasMaxLength(500);
            entity.Property(e => e.RegistrationRequiredFieldCodes).HasMaxLength(2000);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.PdfUrl).HasMaxLength(500);
            entity.Property(e => e.UploadedPdfObjectKey).HasMaxLength(500);
            entity.Property(e => e.UploadedPdfFileName).HasMaxLength(260);
            entity.Property(e => e.WaiverMethod).HasMaxLength(40);
            entity.Property(e => e.WaiverUploadedPdfObjectKey).HasMaxLength(500);
            entity.Property(e => e.WaiverUploadedPdfFileName).HasMaxLength(260);
            entity.Property(e => e.WebsiteUrl).HasMaxLength(500);
            entity.Property(e => e.ZipCode).HasMaxLength(10);

            entity.HasOne(d => d.Sport).WithMany(p => p.Opportunities)
                .HasForeignKey(d => d.SportId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Team).WithMany(p => p.Opportunities)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OpportunityGeographicTarget>(entity =>
        {
            entity.HasIndex(e => e.OpportunityId, "IX_OpportunityGeographicTargets_OpportunityId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TargetZipCode).HasMaxLength(10);

            entity.HasOne(d => d.Opportunity).WithMany(p => p.OpportunityGeographicTargets).HasForeignKey(d => d.OpportunityId);
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.IsContactInfoVisible).HasDefaultValue(true);
            entity.Property(e => e.IsSearchable).HasDefaultValue(true);
            entity.Property(e => e.LogoImageUrl).HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.PrimaryColor).HasMaxLength(7);
            entity.Property(e => e.SecondaryColor).HasMaxLength(7);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.WebsiteUrl).HasMaxLength(500);
            entity.Property(e => e.ZipCode).HasMaxLength(10);
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.BatsHand).HasMaxLength(10);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.ContactVisibility).HasMaxLength(40).HasDefaultValue("VerifiedCoachesOnly");
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.ContactPhone).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CurrentTeamName).HasMaxLength(200);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.Gender).HasMaxLength(10);
            entity.Property(e => e.Height).HasMaxLength(20);
            entity.Property(e => e.IsSearchable).HasDefaultValue(true);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.ProfileImageUrl).HasMaxLength(500);
            entity.Property(e => e.SchoolName).HasMaxLength(200);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.ThrowsHand).HasMaxLength(10);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Weight).HasMaxLength(20);
            entity.Property(e => e.ZipCode).HasMaxLength(10);
        });

        modelBuilder.Entity<PlayerListing>(entity =>
        {
            entity.HasIndex(e => new { e.IsPublished, e.IsSearchable, e.IsActive, e.ListingType }, "IX_PlayerListings_Discovery");

            entity.HasIndex(e => e.PlayerId, "IX_PlayerListings_PlayerId");

            entity.HasIndex(e => e.SportId, "IX_PlayerListings_SportId");

            entity.HasIndex(e => e.UpdatedAt, "IX_PlayerListings_UpdatedAt");

            entity.HasIndex(e => e.UserId, "IX_PlayerListings_UserId");

            entity.HasIndex(e => e.ZipCode, "IX_PlayerListings_ZipCode");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Condition).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.IsPublished).HasDefaultValue(false);
            entity.Property(e => e.IsSearchable).HasDefaultValue(true);
            entity.Property(e => e.ListingType).HasMaxLength(50);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.VisibleSocialLinkKeys).HasMaxLength(2000);
            entity.Property(e => e.UploadedPdfObjectKey).HasMaxLength(500);
            entity.Property(e => e.UploadedPdfFileName).HasMaxLength(260);
            entity.Property(e => e.ZipCode).HasMaxLength(10);
            entity.Property(e => e.City).HasMaxLength(100);

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerListings)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Sport).WithMany(p => p.PlayerListings)
                .HasForeignKey(d => d.SportId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.User).WithMany(p => p.PlayerListings)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlayerSport>(entity =>
        {
            entity.HasIndex(e => e.PlayerId, "IX_PlayerSports_PlayerId");

            entity.HasIndex(e => e.SportId, "IX_PlayerSports_SportId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ExperienceLevel).HasMaxLength(50);
            entity.Property(e => e.PrimaryPosition).HasMaxLength(100);
            entity.Property(e => e.SecondaryPositions).HasMaxLength(200);
            entity.Property(e => e.SkillLevel).HasMaxLength(50);

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerSports).HasForeignKey(d => d.PlayerId);

            entity.HasOne(d => d.Sport).WithMany(p => p.PlayerSports).HasForeignKey(d => d.SportId);
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasIndex(e => e.OpportunityId, "IX_Posts_OpportunityId");

            entity.HasIndex(e => e.UserId, "IX_Posts_UserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.PostType).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.Opportunity).WithMany(p => p.Posts).HasForeignKey(d => d.OpportunityId);

            entity.HasOne(d => d.User).WithMany(p => p.Posts)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Registration>(entity =>
        {
            entity.HasIndex(e => e.OpportunityId, "IX_Registrations_OpportunityId");

            entity.HasIndex(e => e.PlayerId, "IX_Registrations_PlayerId");

            entity.HasIndex(e => e.RegisteredByUserId, "IX_Registrations_RegisteredByUserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AttendanceStatus).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.EmergencyContactName).HasMaxLength(200);
            entity.Property(e => e.EmergencyContactPhone).HasMaxLength(20);
            entity.Property(e => e.PaymentIntentId).HasMaxLength(255);
            entity.Property(e => e.PaymentStatus).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.WaiverSignerName).HasMaxLength(200);

            entity.HasOne(d => d.Opportunity).WithMany(p => p.Registrations).HasForeignKey(d => d.OpportunityId);

            entity.HasOne(d => d.Player).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.RegisteredByUser).WithMany(p => p.Registrations)
                .HasForeignKey(d => d.RegisteredByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Sport>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<PendingAccountTypeChange>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_PendingAccountTypeChanges_UserId");
            entity.HasIndex(e => new { e.UserId, e.Status }, "IX_PendingAccountTypeChanges_UserId_Status");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TargetRolesJson).HasMaxLength(4000);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.RequestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        modelBuilder.Entity<Sport>().HasData(TryOutSpotSportsCatalog.SeedSports);

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_Subscriptions_UserId");
            entity.HasIndex(e => new { e.UserId, e.PlanType, e.ScopeType, e.ScopeId }, "IX_Subscriptions_UserId_PlanType_Scope");
            entity.HasIndex(e => e.StripeCustomerId, "IX_Subscriptions_StripeCustomerId");
            entity.HasIndex(e => e.StripeSubscriptionId, "IX_Subscriptions_StripeSubscriptionId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.BillingInterval).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.IsElite).HasDefaultValue(false);
            entity.Property(e => e.PlanType).HasMaxLength(50);
            entity.Property(e => e.ScopeType).HasMaxLength(50).HasDefaultValue("account");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.StripeCustomerId).HasMaxLength(255);
            entity.Property(e => e.StripePriceId).HasMaxLength(255);
            entity.Property(e => e.StripeSubscriptionId).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.User).WithMany(p => p.Subscriptions).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<StripeWebhookEvent>(entity =>
        {
            entity.HasIndex(e => e.StripeEventId, "IX_StripeWebhookEvents_StripeEventId").IsUnique();

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.ProcessedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.StripeEventId).HasMaxLength(255);
            entity.Property(e => e.StripeObjectId).HasMaxLength(255);
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasIndex(e => e.OrganizationId, "IX_Teams_OrganizationId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.IsContactInfoVisible).HasDefaultValue(true);
            entity.Property(e => e.IsSearchable).HasDefaultValue(true);
            entity.Property(e => e.LogoImageUrl).HasMaxLength(500);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.PrimaryColor).HasMaxLength(7);
            entity.Property(e => e.SecondaryColor).HasMaxLength(7);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.GeographicScope).HasMaxLength(20);
            entity.Property(e => e.TeamLevel).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.WebsiteUrl).HasMaxLength(500);
            entity.Property(e => e.ZipCode).HasMaxLength(10);

            entity.HasOne(d => d.Organization).WithMany(p => p.Teams)
                .HasForeignKey(d => d.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TeamSport>(entity =>
        {
            entity.HasIndex(e => e.SportId, "IX_TeamSports_SportId");

            entity.HasIndex(e => e.TeamId, "IX_TeamSports_TeamId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AgeGroup).HasMaxLength(50);
            entity.Property(e => e.CompetitionLevel).HasMaxLength(100);
            entity.Property(e => e.TravelLevel).HasMaxLength(50);

            entity.HasOne(d => d.Sport).WithMany(p => p.TeamSports).HasForeignKey(d => d.SportId);

            entity.HasOne(d => d.Team).WithMany(p => p.TeamSports).HasForeignKey(d => d.TeamId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");

            entity.HasIndex(e => e.Email, "IX_Users_Email").IsUnique();

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.ConcurrencyStamp).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EmailConfirmed).HasColumnName("IsEmailVerified");
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.NormalizedEmail).HasMaxLength(255);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(255);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.PhoneNumberConfirmed).HasColumnName("IsPhoneVerified");
            entity.Property(e => e.ProfileImageUrl).HasMaxLength(500);
            entity.Property(e => e.SecurityStamp).HasMaxLength(255);
            entity.Property(e => e.SmsConsentSource).HasMaxLength(100);
            entity.Property(e => e.SmsConsentText).HasMaxLength(1000);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UserName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ZipCode).HasMaxLength(10);
        });

        modelBuilder.Entity<UserAdFrequency>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_UserAdFrequencies_UserId").IsUnique();

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasOne(d => d.User).WithOne(p => p.UserAdFrequency).HasForeignKey<UserAdFrequency>(d => d.UserId);
        });

        modelBuilder.Entity<UserOauthProvider>(entity =>
        {
            entity.ToTable("UserOAuthProviders");

            entity.HasIndex(e => e.UserId, "IX_UserOAuthProviders_UserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Provider).HasMaxLength(50);
            entity.Property(e => e.ProviderUserId).HasMaxLength(255);

            entity.HasOne(d => d.User).WithMany(p => p.UserOauthProviders).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<UserPlayerRelationship>(entity =>
        {
            entity.HasIndex(e => e.PlayerId, "IX_UserPlayerRelationships_PlayerId");

            entity.HasIndex(e => e.UserId, "IX_UserPlayerRelationships_UserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Relationship).HasMaxLength(50);

            entity.HasOne(d => d.Player).WithMany(p => p.UserPlayerRelationships).HasForeignKey(d => d.PlayerId);

            entity.HasOne(d => d.User).WithMany(p => p.UserPlayerRelationships).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<UserTeamRole>(entity =>
        {
            entity.HasIndex(e => e.TeamId, "IX_UserTeamRoles_TeamId");

            entity.HasIndex(e => e.UserId, "IX_UserTeamRoles_UserId");

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Role).HasMaxLength(50);

            entity.HasOne(d => d.Team).WithMany(p => p.UserTeamRoles).HasForeignKey(d => d.TeamId);

            entity.HasOne(d => d.User).WithMany(p => p.UserTeamRoles).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<ZipCodeGeography>(entity =>
        {
            entity.HasKey(e => e.ZipCode);

            entity.Property(e => e.ZipCode).HasMaxLength(10);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.Latitude).HasColumnType("numeric(9,6)");
            entity.Property(e => e.Longitude).HasColumnType("numeric(9,6)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasIndex(e => e.IsActive, "IX_ZipCodeGeographies_IsActive");
            entity.HasIndex(e => new { e.State, e.City }, "IX_ZipCodeGeographies_State_City");
        });

        modelBuilder.Entity<IdentityRole<Guid>>().HasData(TryOutSpotRoles.SeedRoles);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
