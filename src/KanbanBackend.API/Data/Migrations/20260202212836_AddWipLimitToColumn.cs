using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBackend.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWipLimitToColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WipLimit",
                table: "Columns",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WipLimit",
                table: "Columns");
        }
    }
}
