using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <summary>
    /// Só dado, sem esquema (change establishment-and-readable-dashboard, design D13): o modo da nota que entrou sem ação
    /// humana passa de <c>RealTime</c> a <c>Automatic</c>, o mesmo nome do rótulo "Automática". O snapshot não muda.
    /// </summary>
    public partial class RenameRealTimeTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [ProcessedDocuments] SET [Trigger] = 'Automatic' WHERE [Trigger] = 'RealTime'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [ProcessedDocuments] SET [Trigger] = 'RealTime' WHERE [Trigger] = 'Automatic'");
        }
    }
}
