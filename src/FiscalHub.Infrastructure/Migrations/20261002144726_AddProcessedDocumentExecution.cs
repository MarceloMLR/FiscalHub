using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedDocumentExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutedOn",
                table: "ProcessedDocuments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodEnd",
                table: "ProcessedDocuments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodStart",
                table: "ProcessedDocuments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            // O registro antigo ganha, como dia da execução, o dia em que foi gravado pela primeira vez, em Brasília, e fica
            // sem período (change erp-company-directory-and-card-filters, D15). O estilo 23 é aaaa-mm-dd.
            migrationBuilder.Sql(
                "UPDATE [ProcessedDocuments] SET [ExecutedOn] = CONVERT(char(10), SWITCHOFFSET([CreatedAt], '-03:00'), 23) WHERE [ExecutedOn] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutedOn",
                table: "ProcessedDocuments");

            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "ProcessedDocuments");

            migrationBuilder.DropColumn(
                name: "PeriodStart",
                table: "ProcessedDocuments");
        }
    }
}
