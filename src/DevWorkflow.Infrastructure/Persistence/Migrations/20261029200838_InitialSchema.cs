using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DevWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    remote_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repositories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: true),
                    payload_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redaction_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acceptance_criteria = table.Column<string[]>(type: "text[]", nullable: false),
                    source_references = table.Column<string[]>(type: "text[]", nullable: false),
                    priority = table.Column<int>(type: "integer", maxLength: 32, nullable: false),
                    risk = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_items_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "authorizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_authorizations_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    worktree_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_executions_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_signals",
                columns: table => new
                {
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_item_signals", x => new { x.work_item_id, x.signal_id });
                    table.ForeignKey(
                        name: "fk_work_item_signals_signals_signal_id",
                        column: x => x.signal_id,
                        principalTable: "signals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_item_signals_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pull_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    head_branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    base_branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    checks_summary = table.Column<string>(type: "text", nullable: true),
                    review_summary = table.Column<string>(type: "text", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pull_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_pull_requests_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pull_requests_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pull_requests_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_type_entity_id_id",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_authorizations_work_item_id_decided_at",
                table: "authorizations",
                columns: new[] { "work_item_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "ix_executions_one_active_per_work_item",
                table: "executions",
                column: "work_item_id",
                unique: true,
                filter: "state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_executions_state_lease_expires_at",
                table: "executions",
                columns: new[] { "state", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_execution_id",
                table: "pull_requests",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_repository_id_external_id",
                table: "pull_requests",
                columns: new[] { "repository_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_work_item_id",
                table: "pull_requests",
                column: "work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_repositories_key",
                table: "repositories",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repositories_remote_url",
                table: "repositories",
                column: "remote_url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signals_source_type_idempotency_key",
                table: "signals",
                columns: new[] { "source_type", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signals_status_received_at",
                table: "signals",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_item_signals_signal_id",
                table: "work_item_signals",
                column: "signal_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_repository_id_status_priority",
                table: "work_items",
                columns: new[] { "repository_id", "status", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "authorizations");

            migrationBuilder.DropTable(
                name: "pull_requests");

            migrationBuilder.DropTable(
                name: "work_item_signals");

            migrationBuilder.DropTable(
                name: "executions");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropTable(
                name: "work_items");

            migrationBuilder.DropTable(
                name: "repositories");
        }
    }
}
