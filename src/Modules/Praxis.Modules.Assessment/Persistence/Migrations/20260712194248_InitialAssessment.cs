using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assessment");

            migrationBuilder.CreateTable(
                name: "abilities",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Theta = table.Column<double>(type: "double precision", nullable: false),
                    StandardError = table.Column<double>(type: "double precision", nullable: false),
                    Responses = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<int>(type: "integer", nullable: false),
                    Stem = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Discrimination = table.Column<double>(type: "double precision", nullable: false),
                    Difficulty = table.Column<double>(type: "double precision", nullable: false),
                    Guessing = table.Column<double>(type: "double precision", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetsMisconception = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimesServed = table.Column<int>(type: "integer", nullable: false),
                    TimesCorrect = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "learner_misconceptions",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevealedBy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IndependentHits = table.Column<int>(type: "integer", nullable: false),
                    CleanItemsSince = table.Column<int>(type: "integer", nullable: false),
                    FailedRemediations = table.Column<int>(type: "integer", nullable: false),
                    TargetedItemsPassed = table.Column<int>(type: "integer", nullable: false),
                    DistinctSessions = table.Column<int>(type: "integer", nullable: false),
                    LastReceiptSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CritiquedItInSomeoneElsesWork = table.Column<bool>(type: "boolean", nullable: false),
                    NamedAndRepairedIt = table.Column<bool>(type: "boolean", nullable: false),
                    SurvivedADifferentModality = table.Column<bool>(type: "boolean", nullable: false),
                    RepairBeganAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastProgressAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learner_misconceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "responses",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Correct = table.Column<bool>(type: "boolean", nullable: false),
                    StatedConfidence = table.Column<double>(type: "double precision", nullable: false),
                    Seconds = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_responses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_abilities_LearnerId_ConceptId",
                schema: "assessment",
                table: "abilities",
                columns: new[] { "LearnerId", "ConceptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_ConceptId_EvidenceType",
                schema: "assessment",
                table: "items",
                columns: new[] { "ConceptId", "EvidenceType" });

            migrationBuilder.CreateIndex(
                name: "IX_items_ConceptId_TargetsMisconception",
                schema: "assessment",
                table: "items",
                columns: new[] { "ConceptId", "TargetsMisconception" });

            migrationBuilder.CreateIndex(
                name: "IX_items_Fingerprint",
                schema: "assessment",
                table: "items",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learner_misconceptions_LearnerId_ConceptId",
                schema: "assessment",
                table: "learner_misconceptions",
                columns: new[] { "LearnerId", "ConceptId" });

            migrationBuilder.CreateIndex(
                name: "IX_responses_EventId",
                schema: "assessment",
                table: "responses",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_responses_LearnerId_ConceptId",
                schema: "assessment",
                table: "responses",
                columns: new[] { "LearnerId", "ConceptId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abilities",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "items",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "learner_misconceptions",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "responses",
                schema: "assessment");
        }
    }
}
