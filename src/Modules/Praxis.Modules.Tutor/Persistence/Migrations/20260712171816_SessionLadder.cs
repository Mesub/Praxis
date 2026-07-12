using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Tutor.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionLadder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeepestRungServed",
                schema: "tutor",
                table: "sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RungsServed",
                schema: "tutor",
                table: "sessions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeepestRungServed",
                schema: "tutor",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "RungsServed",
                schema: "tutor",
                table: "sessions");
        }
    }
}
