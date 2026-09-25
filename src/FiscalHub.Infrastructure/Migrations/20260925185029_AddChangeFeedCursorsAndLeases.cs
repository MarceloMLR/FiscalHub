using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeFeedCursorsAndLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeFeedCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WatermarkTicks = table.Column<long>(type: "bigint", nullable: true),
                    LastPolledTicks = table.Column<long>(type: "bigint", nullable: true),
                    NotBeforeTicks = table.Column<long>(type: "bigint", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeFeedCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    Resource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpiresTicks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.Resource);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeFeedCursors_TenantId_Origin",
                table: "ChangeFeedCursors",
                columns: new[] { "TenantId", "Origin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeFeedCursors");

            migrationBuilder.DropTable(
                name: "Leases");
        }
    }
}
