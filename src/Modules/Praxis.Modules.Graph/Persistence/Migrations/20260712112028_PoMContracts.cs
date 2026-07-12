using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Graph.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoMContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pom_contracts",
                schema: "graph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldThreshold = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pom_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pom_contracts_concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "graph",
                        principalTable: "concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accepted_evidence",
                schema: "graph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoMContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    MinScore = table.Column<double>(type: "double precision", nullable: false),
                    MaxContribution = table.Column<double>(type: "double precision", nullable: false),
                    RequiredCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accepted_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accepted_evidence_pom_contracts_PoMContractId",
                        column: x => x.PoMContractId,
                        principalSchema: "graph",
                        principalTable: "pom_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accepted_evidence_PoMContractId_Type",
                schema: "graph",
                table: "accepted_evidence",
                columns: new[] { "PoMContractId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pom_contracts_ConceptId",
                schema: "graph",
                table: "pom_contracts",
                column: "ConceptId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accepted_evidence",
                schema: "graph");

            migrationBuilder.DropTable(
                name: "pom_contracts",
                schema: "graph");
        }
    }
}
