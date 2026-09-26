using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreatorRizz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordQueuedWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    production_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_outbox", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_outbox_productions_production_id",
                        column: x => x.production_id,
                        principalTable: "productions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_outbox_idempotency_key",
                table: "job_outbox",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_outbox_kind_production_id",
                table: "job_outbox",
                columns: new[] { "kind", "production_id" });

            migrationBuilder.CreateIndex(
                name: "IX_job_outbox_production_id",
                table: "job_outbox",
                column: "production_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_outbox");
        }
    }
}
