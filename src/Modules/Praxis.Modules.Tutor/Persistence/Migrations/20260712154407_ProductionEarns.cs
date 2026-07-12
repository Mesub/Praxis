using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Tutor.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionEarns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Earned",
                schema: "tutor",
                table: "answer_budgets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductionsRecorded",
                schema: "tutor",
                table: "answer_budgets",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Earned",
                schema: "tutor",
                table: "answer_budgets");

            migrationBuilder.DropColumn(
                name: "ProductionsRecorded",
                schema: "tutor",
                table: "answer_budgets");
        }
    }
}
