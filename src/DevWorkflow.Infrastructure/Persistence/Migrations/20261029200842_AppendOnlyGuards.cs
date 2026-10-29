using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevWorkflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enforces append-only rules in the database so no code path, including a bug or a
    /// direct SQL session, can rewrite history:
    /// audit events cannot be updated or deleted, and a signal's raw data cannot be edited
    /// or the signal deleted. Only a signal's processing status may change.
    /// </summary>
    public partial class AppendOnlyGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_audit_event_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_events is append-only; % is not allowed', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER audit_events_append_only
                    BEFORE UPDATE OR DELETE ON audit_events
                    FOR EACH ROW EXECUTE FUNCTION reject_audit_event_change();

                CREATE TRIGGER audit_events_no_truncate
                    BEFORE TRUNCATE ON audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_audit_event_change();

                CREATE FUNCTION protect_signal_payload() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'signals are append-only; DELETE is not allowed'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.source_type       IS DISTINCT FROM OLD.source_type
                    OR NEW.source_reference  IS DISTINCT FROM OLD.source_reference
                    OR NEW.idempotency_key   IS DISTINCT FROM OLD.idempotency_key
                    OR NEW.payload           IS DISTINCT FROM OLD.payload
                    OR NEW.payload_reference IS DISTINCT FROM OLD.payload_reference
                    OR NEW.observed_at       IS DISTINCT FROM OLD.observed_at
                    OR NEW.received_at       IS DISTINCT FROM OLD.received_at
                    OR NEW.redaction_status  IS DISTINCT FROM OLD.redaction_status THEN
                        RAISE EXCEPTION 'signal raw data is immutable; only status may change'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER signals_immutable_payload
                    BEFORE UPDATE OR DELETE ON signals
                    FOR EACH ROW EXECUTE FUNCTION protect_signal_payload();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS signals_immutable_payload ON signals;
                DROP FUNCTION IF EXISTS protect_signal_payload();
                DROP TRIGGER IF EXISTS audit_events_no_truncate ON audit_events;
                DROP TRIGGER IF EXISTS audit_events_append_only ON audit_events;
                DROP FUNCTION IF EXISTS reject_audit_event_change();
                """);
        }
    }
}
