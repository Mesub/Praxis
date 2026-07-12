using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Tutor.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTutor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tutor");

            migrationBuilder.CreateTable(
                name: "answer_budgets",
                schema: "tutor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Allowance = table.Column<int>(type: "integer", nullable: false),
                    Spent = table.Column<int>(type: "integer", nullable: false),
                    DeepestRung = table.Column<int>(type: "integer", nullable: true),
                    HintsTaken = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer_budgets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answer_budgets_LearnerId_ConceptId",
                schema: "tutor",
                table: "answer_budgets",
                columns: new[] { "LearnerId", "ConceptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_budgets",
                schema: "tutor");
        }
    }
}
