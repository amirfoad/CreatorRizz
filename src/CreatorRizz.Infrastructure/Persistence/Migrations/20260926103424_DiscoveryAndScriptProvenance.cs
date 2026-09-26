using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreatorRizz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryAndScriptProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added as nullable so the rows that already exist can be given a real fingerprint before
            // the unique index is created. A non-null default of "" would put every existing row in
            // the same bucket and fail the index build.
            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                table: "topic_candidates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Mirrors CandidateFingerprint.From: lowercase, keep letters and digits, turn every other
            // run into a single space, then hash "<title>|<creator>".
            migrationBuilder.Sql(
                """
                UPDATE "topic_candidates"
                SET "fingerprint" = encode(sha256(convert_to(
                    btrim(regexp_replace(lower(btrim(coalesce("title", ''))), '[^[:alnum:]]+', ' ', 'g')) || '|' ||
                    btrim(regexp_replace(lower(btrim(coalesce("creator", ''))), '[^[:alnum:]]+', ' ', 'g')),
                    'UTF8')), 'hex')
                """);

            // Two pre-existing rows can share a headline, which the unique index will not allow. Dropping
            // one would destroy a production that points at it, so instead the earliest row keeps the
            // clean fingerprint and later rows are salted with their own id. Those rows then simply do
            // not take part in headline dedupe: discovery sees them as new stories, which is the safe
            // direction to be wrong in.
            migrationBuilder.Sql(
                """
                UPDATE "topic_candidates" "row"
                SET "fingerprint" = encode(sha256(convert_to("row"."fingerprint" || '|' || "row"."id"::text, 'UTF8')), 'hex')
                FROM (
                    SELECT "id", "fingerprint",
                           row_number() OVER (PARTITION BY "fingerprint" ORDER BY "created_at", "id") AS "rank"
                    FROM "topic_candidates"
                ) "ranked"
                WHERE "row"."id" = "ranked"."id" AND "ranked"."rank" > 1
                """);

            migrationBuilder.Sql(
                """ALTER TABLE "topic_candidates" ALTER COLUMN "fingerprint" SET NOT NULL;""");

            migrationBuilder.CreateTable(
                name: "script_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_references_json = table.Column<string>(type: "jsonb", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    claim_map_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_generations", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_generations_productions_production_id",
                        column: x => x.production_id,
                        principalTable: "productions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_topic_candidates_fingerprint",
                table: "topic_candidates",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_script_generations_production_id_created_at",
                table: "script_generations",
                columns: new[] { "production_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "script_generations");

            migrationBuilder.DropIndex(
                name: "IX_topic_candidates_fingerprint",
                table: "topic_candidates");

            migrationBuilder.DropColumn(
                name: "fingerprint",
                table: "topic_candidates");
        }
    }
}
