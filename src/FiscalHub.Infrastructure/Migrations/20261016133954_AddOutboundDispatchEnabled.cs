using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundDispatchEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DEFAULT 1, e não o 0 que o EF gera do default do CLR: o tenant que já existe sobe enviando, que é o
            // comportamento de sempre. Sem backfill — o default cobre as linhas existentes, como o Active dos Users
            // (AddTenantsAndUserActive). O padrão de uma linha nova fica no CLR (ConnectorProfileRow), para o EF não
            // omitir a coluna justamente num INSERT com a chave desligada.
            migrationBuilder.AddColumn<bool>(
                name: "OutboundDispatchEnabled",
                table: "ConnectorProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutboundDispatchEnabled",
                table: "ConnectorProfiles");
        }
    }
}
