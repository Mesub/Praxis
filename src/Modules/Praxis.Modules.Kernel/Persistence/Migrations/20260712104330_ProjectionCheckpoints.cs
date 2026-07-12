using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Praxis.Modules.Kernel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectionCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projection_checkpoints",
                schema: "kernel",
                columns: table => new
                {
                    ProjectionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_checkpoints", x => x.ProjectionName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projection_checkpoints",
                schema: "kernel");
        }
    }
}
