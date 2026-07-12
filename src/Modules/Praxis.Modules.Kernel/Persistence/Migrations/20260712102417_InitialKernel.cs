using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Praxis.Modules.Kernel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kernel");

            migrationBuilder.CreateTable(
                name: "learning_events",
                schema: "kernel",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_events", x => x.Sequence);
                    table.UniqueConstraint("AK_learning_events_Id", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_events_LearnerId_ConceptId_OccurredAt",
                schema: "kernel",
                table: "learning_events",
                columns: new[] { "LearnerId", "ConceptId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_events_LearnerId_Sequence",
                schema: "kernel",
                table: "learning_events",
                columns: new[] { "LearnerId", "Sequence" });

            // The guarantee.
            //
            // The C# side also refuses to update or delete an event, but that is a courtesy to
            // whoever writes the bug. THIS is what makes the log trustworthy: the database itself
            // will not permit history to change, no matter who asks or what tool they use.
            //
            // Everything PRAXIS claims about a learner is a projection of this table. A ledger
            // built on editable history is not a ledger, it is a rumour.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION kernel.reject_event_mutation()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION
                        'The event log is append-only. % on kernel.learning_events is forbidden.',
                        TG_OP
                        USING ERRCODE = 'check_violation';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER learning_events_are_immutable
                BEFORE UPDATE OR DELETE ON kernel.learning_events
                FOR EACH ROW EXECUTE FUNCTION kernel.reject_event_mutation();

                CREATE TRIGGER learning_events_resist_truncation
                BEFORE TRUNCATE ON kernel.learning_events
                FOR EACH STATEMENT EXECUTE FUNCTION kernel.reject_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS learning_events_resist_truncation ON kernel.learning_events;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS learning_events_are_immutable ON kernel.learning_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS kernel.reject_event_mutation();");

            migrationBuilder.DropTable(
                name: "learning_events",
                schema: "kernel");
        }
    }
}
