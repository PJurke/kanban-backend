using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBackend.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyAndRankStability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Cards",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Cards",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // SQLite-specific triggers to emulate RowVersion behavior
            migrationBuilder.Sql(
                @"CREATE TRIGGER SetCardRowVersionOnInsert
                  AFTER INSERT ON Cards
                  BEGIN
                      UPDATE Cards
                      SET RowVersion = randomblob(8)
                      WHERE Id = NEW.Id;
                  END;");

            migrationBuilder.Sql(
                @"CREATE TRIGGER SetCardRowVersionOnUpdate
                  AFTER UPDATE ON Cards
                  BEGIN
                      UPDATE Cards
                      SET RowVersion = randomblob(8)
                      WHERE Id = NEW.Id;
                  END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Cards");
            
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS SetCardRowVersionOnInsert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS SetCardRowVersionOnUpdate;");
        }
    }
}
