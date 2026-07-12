using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Graph.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "graph");

            migrationBuilder.CreateTable(
                name: "concepts",
                schema: "graph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Domain = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DifficultyPrior = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_concepts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "graph_versions",
                schema: "graph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ConceptCount = table.Column<int>(type: "integer", nullable: false),
                    EdgeCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "concept_edges",
                schema: "graph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToConceptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concept_edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_concept_edges_concepts_FromConceptId",
                        column: x => x.FromConceptId,
                        principalSchema: "graph",
                        principalTable: "concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_concept_edges_concepts_ToConceptId",
                        column: x => x.ToConceptId,
                        principalSchema: "graph",
                        principalTable: "concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_concept_edges_FromConceptId_Kind",
                schema: "graph",
                table: "concept_edges",
                columns: new[] { "FromConceptId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_concept_edges_FromConceptId_ToConceptId_Kind",
                schema: "graph",
                table: "concept_edges",
                columns: new[] { "FromConceptId", "ToConceptId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_concept_edges_ToConceptId_Kind",
                schema: "graph",
                table: "concept_edges",
                columns: new[] { "ToConceptId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_concepts_Domain",
                schema: "graph",
                table: "concepts",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_concepts_Slug",
                schema: "graph",
                table: "concepts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_graph_versions_Domain_Number",
                schema: "graph",
                table: "graph_versions",
                columns: new[] { "Domain", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concept_edges",
                schema: "graph");

            migrationBuilder.DropTable(
                name: "graph_versions",
                schema: "graph");

            migrationBuilder.DropTable(
                name: "concepts",
                schema: "graph");
        }
    }
}
