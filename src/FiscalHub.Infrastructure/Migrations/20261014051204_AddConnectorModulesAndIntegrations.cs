using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <summary>
    /// Os módulos e as integrações viram linhas sob o conector (change <c>platform-account-and-connectors</c>, M3 do design
    /// D13). Do perfil convertido pela M2:
    /// <list type="bullet">
    ///   <item>o <b>conector</b> do tenant ganha o módulo <b>Fiscal, sempre</b>, com a integração de notas fiscais ligada e o
    ///   caminho <b>nulo</b>, à espera do host, que o preenche uma vez (D10) — e não vazio, que é a inativa da tela (D5). É a
    ///   integração de notas que mantém o envio, e ela só fica ligada com o módulo dela;</item>
    ///   <item>os <b>módulos gravados no tenant</b> vão para esse conector, sem integrações;</item>
    ///   <item>a coluna <c>Modules</c> sai, depois de lida.</item>
    /// </list>
    /// As travas seguem a regra da M2: nada é removido que não tenha sido lido, e nada é adivinhado. O <c>Up</c> falha, antes
    /// de qualquer escrita, com: uma lista de módulos que não é uma lista de <c>Fiscal</c>, <c>Contabil</c> e
    /// <c>Inventario</c>, nessa grafia (a conversão não adivinha o módulo que o valor quis dizer); um tenant com mais de um
    /// conector (a conversão liga os módulos num conector só, e não escolhe qual); um conector cujo adapter não é exatamente
    /// <c>Avalara</c> (a integração de notas que ela liga é a que esse adapter declara, e na data desta migração só existe
    /// ele); e um tenant sem conector com módulos além do Fiscal na lista (sem conector, eles não têm onde ficar).
    /// <para><b>Tem volta, com condição.</b> O <c>Down</c> devolve ao tenant a lista dos módulos do conector, na ordem da
    /// barra lateral. O caminho da integração de notas não volta, e não precisa: antes da M3 o envio lê o
    /// <c>Avalara:DocumentsPath</c>. A condição é a forma nova não ter sido usada além do que a lista do tenant comporta:
    /// uma integração ligada além da de notas, um tamanho de lote gravado, ou mais de um conector no tenant fazem o
    /// <c>Down</c> falhar antes de qualquer escrita, porque desfazer perderia dado.</para>
    /// </summary>
    public partial class AddConnectorModulesAndIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorModules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectorId = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorModules_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorIntegrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BatchSize = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorIntegrations_ConnectorModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "ConnectorModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorIntegrations_ModuleId",
                table: "ConnectorIntegrations",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorIntegrations_TenantId_Key",
                table: "ConnectorIntegrations",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorModules_ConnectorId_Module",
                table: "ConnectorModules",
                columns: new[] { "ConnectorId", "Module" },
                unique: true);

            // A lista do tenant é lida pelo OPENJSON só depois do ISJSON, e o valor é comparado em binário, como a leitura do
            // perfil o compara: "fiscal" não é "Fiscal".
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM ConnectorProfiles AS p
                    WHERE LTRIM(RTRIM(COALESCE(p.Modules, N''))) <> N''
                      AND (ISJSON(p.Modules) = 0
                           OR LEFT(LTRIM(p.Modules), 1) <> N'['
                           OR EXISTS (SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(p.Modules) = 1 THEN p.Modules END) AS m
                                      WHERE m.[type] <> 1
                                         OR m.[value] COLLATE Latin1_General_BIN2 NOT IN (N'Fiscal', N'Contabil', N'Inventario')))
                )
                    THROW 50000, N'AddConnectorModulesAndIntegrations: existe perfil cuja lista de módulos não é uma lista de Fiscal, Contabil e Inventario, nessa grafia. A conversão não adivinha o módulo que o valor quis dizer. Acerte o dado antes de migrar.', 1;

                IF EXISTS (
                    SELECT 1 FROM Connectors AS c
                    JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                    GROUP BY a.TenantId
                    HAVING COUNT(*) > 1
                )
                    THROW 50000, N'AddConnectorModulesAndIntegrations: existe tenant com mais de um conector. A conversão liga os módulos do tenant e a integração de notas num conector só, e não escolhe qual. Acerte o dado antes de migrar.', 1;

                IF EXISTS (SELECT 1 FROM Connectors AS c WHERE c.Adapter COLLATE Latin1_General_BIN2 <> N'Avalara')
                    THROW 50000, N'AddConnectorModulesAndIntegrations: existe conector cujo adapter não é exatamente ''Avalara''. A integração de notas que a conversão liga é a que esse adapter declara. Acerte o dado antes de migrar.', 1;

                IF EXISTS (
                    SELECT 1 FROM ConnectorProfiles AS p
                    WHERE NOT EXISTS (SELECT 1 FROM PlatformAccounts AS a JOIN Connectors AS c ON c.AccountId = a.Id
                                      WHERE a.TenantId = p.TenantId)
                      AND EXISTS (SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(p.Modules) = 1 THEN p.Modules END) AS m
                                  WHERE m.[value] COLLATE Latin1_General_BIN2 <> N'Fiscal')
                )
                    THROW 50000, N'AddConnectorModulesAndIntegrations: existe tenant sem conector com módulos além do Fiscal na lista. Sem conector, eles não têm onde ficar, e o tenant passaria a ter só o Fiscal. Acerte o dado antes de migrar.', 1;

                INSERT INTO ConnectorModules (ConnectorId, Module)
                SELECT c.Id, chosen.Module
                FROM Connectors AS c
                JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                OUTER APPLY (SELECT TOP (1) p.Modules FROM ConnectorProfiles AS p WHERE p.TenantId = a.TenantId) AS profile
                CROSS APPLY (
                    SELECT N'Fiscal' AS Module
                    UNION
                    SELECT CAST(m.[value] AS nvarchar(50)) COLLATE DATABASE_DEFAULT
                    FROM OPENJSON(CASE WHEN ISJSON(profile.Modules) = 1 THEN profile.Modules END) AS m
                ) AS chosen;

                INSERT INTO ConnectorIntegrations (ModuleId, TenantId, [Key], Endpoint, BatchSize)
                SELECT m.Id, a.TenantId, N'notas-fiscais', NULL, NULL
                FROM ConnectorModules AS m
                JOIN Connectors AS c ON c.Id = m.ConnectorId
                JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                WHERE m.Module = N'Fiscal';
                """);

            migrationBuilder.DropColumn(
                name: "Modules",
                table: "ConnectorProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A condição da volta, antes de qualquer escrita: a lista do tenant guarda só quais módulos ele tem. O caminho da
            // integração de notas não precisa voltar (antes da M3, o envio lê o Avalara:DocumentsPath); o resto da forma nova
            // não teria para onde ir.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM ConnectorIntegrations
                           WHERE [Key] COLLATE Latin1_General_BIN2 <> N'notas-fiscais' OR BatchSize IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM Connectors AS c JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                              GROUP BY a.TenantId HAVING COUNT(*) > 1)
                    THROW 50000, N'AddConnectorModulesAndIntegrations: o banco já usa a forma nova além do que a lista de módulos do tenant comporta (integração ligada além da de notas, tamanho de lote gravado, ou mais de um conector no tenant). Desfazer perderia dado; restaure um backup.', 1;
                """);

            migrationBuilder.AddColumn<string>(
                name: "Modules",
                table: "ConnectorProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // A lista na ordem da barra lateral; sem módulo ligado, nula, que é o padrão (só o Fiscal).
            migrationBuilder.Sql(
                """
                UPDATE p
                SET Modules = (
                    SELECT N'[' + STRING_AGG(N'"' + STRING_ESCAPE(m.Module, 'json') + N'"', N',')
                                 WITHIN GROUP (ORDER BY CASE m.Module WHEN N'Fiscal' THEN 0 WHEN N'Contabil' THEN 1
                                                                      WHEN N'Inventario' THEN 2 ELSE 3 END, m.Module) + N']'
                    FROM ConnectorModules AS m
                    JOIN Connectors AS c ON c.Id = m.ConnectorId
                    JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                    WHERE a.TenantId = p.TenantId)
                FROM ConnectorProfiles AS p;
                """);

            migrationBuilder.DropTable(
                name: "ConnectorIntegrations");

            migrationBuilder.DropTable(
                name: "ConnectorModules");
        }
    }
}
