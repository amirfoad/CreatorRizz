using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.Persistence;

public sealed class CreatorRizzDbContext(DbContextOptions<CreatorRizzDbContext> options) : DbContext(options)
{
    public DbSet<TopicCandidate> TopicCandidates => Set<TopicCandidate>();
    public DbSet<SourceItem> SourceItems => Set<SourceItem>();
    public DbSet<ResearchPack> ResearchPacks => Set<ResearchPack>();
    public DbSet<Production> Productions => Set<Production>();
    public DbSet<ScriptVersion> ScriptVersions => Set<ScriptVersion>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TopicCandidate>(entity =>
        {
            entity.ToTable("topic_candidates");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2000);
            entity.Property(item => item.Title).HasMaxLength(500);
            entity.Property(item => item.PublishedAt).HasColumnName("published_at");
            entity.Property(item => item.ViralScore).HasColumnName("viral_score").HasPrecision(5, 2);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(item => item.CanonicalUrl).IsUnique();
        });
        modelBuilder.Entity<SourceItem>(entity => { entity.ToTable("source_items"); entity.HasKey(item => item.Id); entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id"); entity.Property(item => item.ReliabilityScore).HasColumnName("reliability_score"); entity.Property(item => item.CapturedAt).HasColumnName("captured_at"); });
        modelBuilder.Entity<ResearchPack>(entity => { entity.ToTable("research_packs"); entity.HasKey(item => item.Id); entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id"); entity.Property(item => item.FactsJson).HasColumnName("facts_json").HasColumnType("jsonb"); entity.Property(item => item.UncertaintyJson).HasColumnName("uncertainty_json").HasColumnType("jsonb"); entity.Property(item => item.CreatedAt).HasColumnName("created_at"); entity.HasIndex(item => item.TopicCandidateId).IsUnique(); });
        modelBuilder.Entity<Production>(entity => { entity.ToTable("productions"); entity.HasKey(item => item.Id); entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id"); entity.Property(item => item.State).HasConversion<string>().HasMaxLength(50); entity.Property(item => item.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<ScriptVersion>(entity => { entity.ToTable("script_versions"); entity.HasKey(item => item.Id); entity.Property(item => item.ProductionId).HasColumnName("production_id"); entity.Property(item => item.ClaimMapJson).HasColumnName("claim_map_json").HasColumnType("jsonb"); entity.Property(item => item.CreatedAt).HasColumnName("created_at"); entity.HasIndex(item => new { item.ProductionId, item.Version }).IsUnique(); });
        modelBuilder.Entity<Asset>(entity => { entity.ToTable("assets"); entity.HasKey(item => item.Id); entity.Property(item => item.ObjectKey).HasColumnName("object_key"); entity.Property(item => item.SourceUrl).HasColumnName("source_url"); entity.Property(item => item.RightsStatus).HasColumnName("rights_status").HasConversion<string>().HasMaxLength(50); entity.Property(item => item.LicenseEvidence).HasColumnName("license_evidence"); });
        modelBuilder.Entity<ReviewDecision>(entity => { entity.ToTable("review_decisions"); entity.HasKey(item => item.Id); entity.Property(item => item.ProductionId).HasColumnName("production_id"); entity.Property(item => item.ReviewerId).HasColumnName("reviewer_id"); entity.Property(item => item.DecidedAt).HasColumnName("decided_at"); });
        modelBuilder.Entity<AuditEvent>(entity => { entity.ToTable("audit_events"); entity.HasKey(item => item.Id); entity.Property(item => item.EntityType).HasColumnName("entity_type"); entity.Property(item => item.EntityId).HasColumnName("entity_id"); entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb"); entity.Property(item => item.OccurredAt).HasColumnName("occurred_at"); });
    }
}
