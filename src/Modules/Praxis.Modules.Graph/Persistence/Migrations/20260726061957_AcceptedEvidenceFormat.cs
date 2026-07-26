using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Graph.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptedEvidenceFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Format",
                schema: "graph",
                table: "accepted_evidence",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Format",
                schema: "graph",
                table: "accepted_evidence");
        }
    }
}
