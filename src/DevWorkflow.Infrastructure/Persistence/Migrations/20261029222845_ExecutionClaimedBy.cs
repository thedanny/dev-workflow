using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionClaimedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claimed_by",
                table: "executions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_by",
                table: "executions");
        }
    }
}
