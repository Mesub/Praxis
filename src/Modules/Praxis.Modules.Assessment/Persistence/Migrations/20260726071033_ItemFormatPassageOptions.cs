using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemFormatPassageOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Format",
                schema: "assessment",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Options",
                schema: "assessment",
                table: "items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Passage",
                schema: "assessment",
                table: "items",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Format",
                schema: "assessment",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Options",
                schema: "assessment",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Passage",
                schema: "assessment",
                table: "items");
        }
    }
}
