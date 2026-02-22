using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DayTrace.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWisdomDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "wisdom_duration",
                table: "users_settings",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "wisdom_duration",
                table: "users_settings");
        }
    }
}
