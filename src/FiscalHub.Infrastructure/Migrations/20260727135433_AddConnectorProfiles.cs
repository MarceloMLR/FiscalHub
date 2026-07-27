using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Realtime = table.Column<bool>(type: "bit", nullable: false),
                    InboundAdapter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InboundSettings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutboundAdapter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutboundSettings = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorProfiles_TenantId",
                table: "ConnectorProfiles",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorProfiles");
        }
    }
}
