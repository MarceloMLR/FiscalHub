using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledIntegrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompanyCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PeriodStart = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PeriodEnd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NextRunTicks = table.Column<long>(type: "bigint", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledIntegrations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledIntegrations");
        }
    }
}
