using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorProfileModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Modules",
                table: "ConnectorProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Modules",
                table: "ConnectorProfiles");
        }
    }
}
