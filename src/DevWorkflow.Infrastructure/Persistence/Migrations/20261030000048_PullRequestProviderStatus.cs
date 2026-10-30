using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestProviderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "head_commit",
                table: "pull_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "pull_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                // Pull requests reported before this migration are treated as open.
                defaultValue: "Open");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                table: "pull_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_state_updated_at",
                table: "pull_requests",
                columns: new[] { "state", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pull_requests_state_updated_at",
                table: "pull_requests");

            migrationBuilder.DropColumn(
                name: "head_commit",
                table: "pull_requests");

            migrationBuilder.DropColumn(
                name: "state",
                table: "pull_requests");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "pull_requests");
        }
    }
}
