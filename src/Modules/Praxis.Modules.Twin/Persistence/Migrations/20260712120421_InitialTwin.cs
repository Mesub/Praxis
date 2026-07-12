using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Twin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTwin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "twin");

            migrationBuilder.CreateTable(
                name: "concept_states",
                schema: "twin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PKnows = table.Column<double>(type: "double precision", nullable: false),
                    MasteryCredit = table.Column<double>(type: "double precision", nullable: false),
                    IsHeld = table.Column<bool>(type: "boolean", nullable: false),
                    Stability = table.Column<double>(type: "double precision", nullable: false),
                    Difficulty = table.Column<double>(type: "double precision", nullable: false),
                    LastEvidenceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MeanStatedConfidence = table.Column<double>(type: "double precision", nullable: false),
                    MeanCorrectness = table.Column<double>(type: "double precision", nullable: false),
                    BrierScore = table.Column<double>(type: "double precision", nullable: false),
                    CalibrationSamples = table.Column<int>(type: "integer", nullable: false),
                    Assurance = table.Column<double>(type: "double precision", nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concept_states", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "misconceptions",
                schema: "twin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Statement = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TimesObserved = table.Column<int>(type: "integer", nullable: false),
                    TimesContradicted = table.Column<int>(type: "integer", nullable: false),
                    FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_misconceptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_concept_states_LearnerId_ConceptId",
                schema: "twin",
                table: "concept_states",
                columns: new[] { "LearnerId", "ConceptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_concept_states_LearnerId_IsHeld",
                schema: "twin",
                table: "concept_states",
                columns: new[] { "LearnerId", "IsHeld" });

            migrationBuilder.CreateIndex(
                name: "IX_concept_states_LearnerId_NextDueAt",
                schema: "twin",
                table: "concept_states",
                columns: new[] { "LearnerId", "NextDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_misconceptions_LearnerId_ConceptId_Key",
                schema: "twin",
                table: "misconceptions",
                columns: new[] { "LearnerId", "ConceptId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_misconceptions_LearnerId_IsActive",
                schema: "twin",
                table: "misconceptions",
                columns: new[] { "LearnerId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concept_states",
                schema: "twin");

            migrationBuilder.DropTable(
                name: "misconceptions",
                schema: "twin");
        }
    }
}
