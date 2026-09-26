using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CreatorRizz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    rights_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    license_evidence = table.Column<string>(type: "text", nullable: true),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    creator = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    viral_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "productions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productions", x => x.id);
                    table.ForeignKey(
                        name: "FK_productions_topic_candidates_topic_candidate_id",
                        column: x => x.topic_candidate_id,
                        principalTable: "topic_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    facts_json = table.Column<string>(type: "jsonb", nullable: false),
                    uncertainty_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_packs", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_packs_topic_candidates_topic_candidate_id",
                        column: x => x.topic_candidate_id,
                        principalTable: "topic_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    publisher = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    excerpt = table.Column<string>(type: "text", nullable: true),
                    reliability_score = table.Column<int>(type: "integer", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_items", x => x.id);
                    table.CheckConstraint("ck_source_items_reliability_score", "reliability_score BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_source_items_topic_candidates_topic_candidate_id",
                        column: x => x.topic_candidate_id,
                        principalTable: "topic_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asset_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    in_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    out_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    narrative_purpose = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_usages", x => x.id);
                    table.ForeignKey(
                        name: "FK_asset_usages_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_asset_usages_productions_production_id",
                        column: x => x.production_id,
                        principalTable: "productions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reviewer_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_review_decisions_productions_production_id",
                        column: x => x.production_id,
                        principalTable: "productions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "script_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    claim_map_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_versions_productions_production_id",
                        column: x => x.production_id,
                        principalTable: "productions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_usages_asset_id",
                table: "asset_usages",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_asset_usages_production_id",
                table: "asset_usages",
                column: "production_id");

            migrationBuilder.CreateIndex(
                name: "IX_assets_checksum",
                table: "assets",
                column: "checksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_entity_type_entity_id_occurred_at",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_productions_topic_candidate_id",
                table: "productions",
                column: "topic_candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_packs_topic_candidate_id",
                table: "research_packs",
                column: "topic_candidate_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_decisions_production_id",
                table: "review_decisions",
                column: "production_id");

            migrationBuilder.CreateIndex(
                name: "IX_script_versions_production_id_version",
                table: "script_versions",
                columns: new[] { "production_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_items_topic_candidate_id",
                table: "source_items",
                column: "topic_candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_topic_candidates_canonical_url",
                table: "topic_candidates",
                column: "canonical_url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_usages");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "research_packs");

            migrationBuilder.DropTable(
                name: "review_decisions");

            migrationBuilder.DropTable(
                name: "script_versions");

            migrationBuilder.DropTable(
                name: "source_items");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "productions");

            migrationBuilder.DropTable(
                name: "topic_candidates");
        }
    }
}
