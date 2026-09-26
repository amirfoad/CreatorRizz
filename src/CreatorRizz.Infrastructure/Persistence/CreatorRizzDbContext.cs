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
    public DbSet<ScriptGeneration> ScriptGenerations => Set<ScriptGeneration>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetUsage> AssetUsages => Set<AssetUsage>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<JobOutboxEntry> JobOutbox => Set<JobOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TopicCandidate>(entity =>
        {
            entity.ToTable("topic_candidates");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2000);
            entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(500);
            entity.Property(item => item.Creator).HasColumnName("creator").HasMaxLength(250);
            entity.Property(item => item.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
            entity.Property(item => item.PublishedAt).HasColumnName("published_at");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.ViralScore).HasColumnName("viral_score").HasPrecision(5, 2);
            entity.Property(item => item.State).HasColumnName("state").HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(item => item.CanonicalUrl).IsUnique();
            entity.HasIndex(item => item.Fingerprint).IsUnique();
        });

        modelBuilder.Entity<SourceItem>(entity =>
        {
            entity.ToTable("source_items", table => table.HasCheckConstraint(
                "ck_source_items_reliability_score",
                "reliability_score BETWEEN 0 AND 100"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id");
            entity.Property(item => item.Url).HasColumnName("url");
            entity.Property(item => item.Publisher).HasColumnName("publisher").HasMaxLength(250);
            entity.Property(item => item.Excerpt).HasColumnName("excerpt");
            entity.Property(item => item.ReliabilityScore).HasColumnName("reliability_score");
            entity.Property(item => item.CapturedAt).HasColumnName("captured_at");
            entity.HasIndex(item => item.TopicCandidateId);
            entity.HasOne<TopicCandidate>().WithMany().HasForeignKey(item => item.TopicCandidateId);
        });

        modelBuilder.Entity<ResearchPack>(entity =>
        {
            entity.ToTable("research_packs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id");
            entity.Property(item => item.Summary).HasColumnName("summary");
            entity.Property(item => item.FactsJson).HasColumnName("facts_json").HasColumnType("jsonb");
            entity.Property(item => item.UncertaintyJson).HasColumnName("uncertainty_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => item.TopicCandidateId).IsUnique();
            entity.HasOne<TopicCandidate>().WithMany().HasForeignKey(item => item.TopicCandidateId);
        });

        modelBuilder.Entity<Production>(entity =>
        {
            entity.ToTable("productions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.TopicCandidateId).HasColumnName("topic_candidate_id");
            entity.Property(item => item.State).HasColumnName("state").HasConversion<string>().HasMaxLength(50);
            entity.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => item.TopicCandidateId);
            entity.HasOne<TopicCandidate>().WithMany().HasForeignKey(item => item.TopicCandidateId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScriptVersion>(entity =>
        {
            entity.ToTable("script_versions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ProductionId).HasColumnName("production_id");
            entity.Property(item => item.Version).HasColumnName("version");
            entity.Property(item => item.Body).HasColumnName("body");
            entity.Property(item => item.ClaimMapJson).HasColumnName("claim_map_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => new { item.ProductionId, item.Version }).IsUnique();
            entity.HasOne<Production>().WithMany().HasForeignKey(item => item.ProductionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScriptGeneration>(entity =>
        {
            entity.ToTable("script_generations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ProductionId).HasColumnName("production_id");
            entity.Property(item => item.ModelId).HasColumnName("model_id").HasMaxLength(200);
            entity.Property(item => item.PromptVersion).HasColumnName("prompt_version").HasMaxLength(100);
            entity.Property(item => item.InputReferencesJson).HasColumnName("input_references_json").HasColumnType("jsonb");
            entity.Property(item => item.Body).HasColumnName("body");
            entity.Property(item => item.ClaimMapJson).HasColumnName("claim_map_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(item => new { item.ProductionId, item.CreatedAt });
            entity.HasOne<Production>().WithMany().HasForeignKey(item => item.ProductionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("assets");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ObjectKey).HasColumnName("object_key");
            entity.Property(item => item.Type).HasColumnName("type").HasMaxLength(50);
            entity.Property(item => item.SourceUrl).HasColumnName("source_url");
            entity.Property(item => item.RightsStatus).HasColumnName("rights_status").HasConversion<string>().HasMaxLength(50);
            entity.Property(item => item.LicenseEvidence).HasColumnName("license_evidence");
            entity.Property(item => item.Checksum).HasColumnName("checksum").HasMaxLength(128);
            entity.HasIndex(item => item.Checksum).IsUnique();
        });

        modelBuilder.Entity<AssetUsage>(entity =>
        {
            entity.ToTable("asset_usages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ProductionId).HasColumnName("production_id");
            entity.Property(item => item.AssetId).HasColumnName("asset_id");
            entity.Property(item => item.InMilliseconds).HasColumnName("in_milliseconds");
            entity.Property(item => item.OutMilliseconds).HasColumnName("out_milliseconds");
            entity.Property(item => item.NarrativePurpose).HasColumnName("narrative_purpose");
            entity.HasIndex(item => item.ProductionId);
            entity.HasOne<Production>().WithMany().HasForeignKey(item => item.ProductionId);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.AssetId);
        });

        modelBuilder.Entity<ReviewDecision>(entity =>
        {
            entity.ToTable("review_decisions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ProductionId).HasColumnName("production_id");
            entity.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(50);
            entity.Property(item => item.Decision).HasColumnName("decision").HasMaxLength(50);
            entity.Property(item => item.ReviewerId).HasColumnName("reviewer_id").HasMaxLength(250);
            entity.Property(item => item.Notes).HasColumnName("notes");
            entity.Property(item => item.DecidedAt).HasColumnName("decided_at");
            entity.HasIndex(item => item.ProductionId);
            entity.HasOne<Production>().WithMany().HasForeignKey(item => item.ProductionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id").HasColumnType("bigint").UseIdentityByDefaultColumn();
            entity.Property(item => item.Actor).HasColumnName("actor").HasMaxLength(250);
            entity.Property(item => item.Action).HasColumnName("action").HasMaxLength(100);
            entity.Property(item => item.EntityType).HasColumnName("entity_type").HasMaxLength(100);
            entity.Property(item => item.EntityId).HasColumnName("entity_id").HasMaxLength(100);
            entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(item => item.OccurredAt).HasColumnName("occurred_at");
            entity.HasIndex(item => new { item.EntityType, item.EntityId, item.OccurredAt });
        });

        modelBuilder.Entity<JobOutboxEntry>(entity =>
        {
            entity.ToTable("job_outbox");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(50);
            entity.Property(item => item.ProductionId).HasColumnName("production_id");
            entity.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            // The database, not the application, decides that one request is one unit of work. An
            // enqueue replayed inside a retried transaction then cannot become a second render.
            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasIndex(item => new { item.Kind, item.ProductionId });
            entity.HasOne<Production>().WithMany().HasForeignKey(item => item.ProductionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
