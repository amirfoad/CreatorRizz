using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreatorRizz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimOutboxWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "job_outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "job_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "claimed_by",
                table: "job_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "job_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                table: "job_outbox",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_outbox_kind_completed_at_created_at",
                table: "job_outbox",
                columns: new[] { "kind", "completed_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_outbox_kind_completed_at_created_at",
                table: "job_outbox");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "job_outbox");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "job_outbox");

            migrationBuilder.DropColumn(
                name: "claimed_by",
                table: "job_outbox");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "job_outbox");

            migrationBuilder.DropColumn(
                name: "last_error",
                table: "job_outbox");
        }
    }
}
