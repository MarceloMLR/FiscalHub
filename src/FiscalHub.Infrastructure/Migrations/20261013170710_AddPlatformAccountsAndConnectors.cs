using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <summary>
    /// A conta de plataforma e o conector viram linhas, juntos (change <c>platform-account-and-connectors</c>, M2 do design
    /// D13). Do perfil já achatado pela M1:
    /// <list type="bullet">
    ///   <item>a <b>conta</b> nasce do <c>OutboundAdapter</c>, com o <c>clientId</c> e o <c>tokenUrl</c> das settings, quando
    ///   elas os têm. A referência do segredo <b>não</b> é copiada (D8): copiada, ela apontaria para o nome antigo, que continua
    ///   no prefixo do tenant, e o segredo apareceria configurado sob um nome que nenhuma gravação nova produz;</item>
    ///   <item>o <b>conector</b> nasce com o mesmo adapter. A <c>baseUrl</c> vira a base da versão <c>v2</c> — chave fixa, porque
    ///   a migração não consulta a declaração do adapter (D8) —, e o resto das settings vai junto, sem a credencial, que é da
    ///   conta;</item>
    ///   <item>as colunas <c>OutboundAdapter</c> e <c>OutboundSettings</c> saem.</item>
    /// </list>
    /// A regra das travas, nos dois sentidos: nada é removido que não tenha sido lido, e nada é adivinhado. O <c>Up</c>
    /// falha, antes de qualquer escrita, com: um perfil sem adapter de saída mas com settings de saída (não têm para que
    /// conta ir); um adapter de saída que não é exatamente <c>Avalara</c> (o adapter acha a conta pelo nome exato, e
    /// normalizar seria adivinhar — na data desta migração, só existe esse despachante); um <c>clientSecret</c> em texto (é
    /// segredo vazado, a rotacionar, e apagá-lo em silêncio destruiria a evidência); uma chave da credencial ou da base
    /// grafada em outra caixa (<c>"BaseUrl"</c>: o caminho JSON distingue caixa, e a chave não seria lida); e um
    /// <c>baseUrls</c> já nas settings (seria sobrescrito).
    /// <para><b>Tem volta, com condição.</b> O <c>Down</c> remonta o adapter de saída e as settings achatadas a partir da conta
    /// e do conector dela — inclusive a referência de segredo que a conta tiver ganhado depois da M2, que é do prefixo do
    /// tenant e a leitura achatada aceita. A referência que a M2 não copiou não volta. A condição: a forma nova não foi usada
    /// além do que o perfil achatado comporta. Mais de uma conta no tenant, mais de um conector na conta, um conector de
    /// adapter diferente da plataforma, uma base de outra versão que não a <c>v2</c>, ou uma chave da forma achatada nas
    /// settings do conector fazem o <c>Down</c> falhar antes de qualquer escrita: desfazer perderia dado.</para>
    /// </summary>
    public partial class AddPlatformAccountsAndConnectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientSecretRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TokenUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Connectors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Adapter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connectors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Connectors_PlatformAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connectors_AccountId_Adapter",
                table: "Connectors",
                columns: new[] { "AccountId", "Adapter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAccounts_TenantId_Platform",
                table: "PlatformAccounts",
                columns: new[] { "TenantId", "Platform" },
                unique: true);

            // Todo JSON_VALUE e OPENJSON lê só o que passou pelo ISJSON: o CASE devolve nulo para o resto, e nada lança no meio
            // da conversão. A extração usa caminho JSON, que distingue caixa; a exclusão do resto compara em binário, igual a
            // ela. Uma chave em outra caixa ("BaseUrl") não seria lida, e a trava recusa o perfil em vez de deixá-la calada
            // nas settings do conector.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM ConnectorProfiles AS p
                    WHERE LTRIM(RTRIM(p.OutboundAdapter)) = N''
                      AND LTRIM(RTRIM(p.OutboundSettings)) NOT IN (N'', N'{}')
                )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: existe perfil com settings de saída e sem adapter de saída, e elas não têm para que conta ir. Acerte o dado antes de migrar.', 1;

                IF EXISTS (
                    SELECT 1 FROM ConnectorProfiles AS p
                    WHERE LTRIM(RTRIM(p.OutboundAdapter)) <> N''
                      AND LTRIM(RTRIM(p.OutboundAdapter)) COLLATE Latin1_General_BIN2 <> N'Avalara'
                )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: existe perfil cujo OutboundAdapter não é exatamente ''Avalara''. Antes desta change o envio não lia esse campo; depois dela a leitura o compara byte a byte, e o conector ficaria invisível. Acerte o dado antes de migrar.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM ConnectorProfiles AS p
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(p.OutboundSettings) = 1
                                               AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                                              THEN p.OutboundSettings END) AS e
                    WHERE LOWER(e.[key]) = N'clientsecret'
                      AND NULLIF(LTRIM(RTRIM(e.[value])), N'') IS NOT NULL
                )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: existe perfil com clientSecret em texto nas settings de saída. A conversão o descartaria sem lê-lo, e um segredo em texto precisa ser rotacionado, não apagado em silêncio. Trate o dado antes de migrar: rotacione o segredo na plataforma; só depois tire o clientSecret das settings de saída, cujo valor a rotação invalidou; migre; e grave o segredo novo na conta de plataforma, pela tela de conectores, que o manda para o cofre.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM ConnectorProfiles AS p
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(p.OutboundSettings) = 1
                                               AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                                              THEN p.OutboundSettings END) AS e
                    WHERE LOWER(e.[key]) IN (N'baseurl', N'clientid', N'clientsecretref', N'clientsecret', N'tokenurl')
                      AND e.[key] COLLATE Latin1_General_BIN2
                          NOT IN (N'baseUrl', N'clientId', N'clientSecretRef', N'clientSecret', N'tokenUrl')
                )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: existe perfil com chave de saída grafada em caixa diferente da esperada (ex.: "BaseUrl"). A conversão não a reconheceria. Acerte o dado antes de migrar.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM ConnectorProfiles AS p
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(p.OutboundSettings) = 1
                                               AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                                              THEN p.OutboundSettings END) AS e
                    WHERE LOWER(e.[key]) = N'baseurls'
                )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: existe perfil com baseUrls nas settings de saída. A conversão escreve baseUrls, e a sobrescreveria sem a ler. Acerte o dado antes de migrar.', 1;

                INSERT INTO PlatformAccounts (TenantId, Platform, ClientId, ClientSecretRef, TokenUrl)
                SELECT p.TenantId,
                       LTRIM(RTRIM(p.OutboundAdapter)),
                       NULLIF(LTRIM(RTRIM(JSON_VALUE(json.Settings, N'$.clientId'))), N''),
                       NULL,
                       NULLIF(LTRIM(RTRIM(JSON_VALUE(json.Settings, N'$.tokenUrl'))), N'')
                FROM ConnectorProfiles AS p
                CROSS APPLY (SELECT CASE WHEN ISJSON(p.OutboundSettings) = 1 THEN p.OutboundSettings END AS Settings) AS json
                WHERE LTRIM(RTRIM(p.OutboundAdapter)) <> N'';

                INSERT INTO Connectors (AccountId, Adapter, Settings)
                SELECT a.Id,
                       a.Platform,
                       CASE WHEN json.Settings IS NULL THEN p.OutboundSettings
                            ELSE JSON_MODIFY(COALESCE(rest.Json, N'{}'), N'$.baseUrls',
                                     JSON_QUERY(CASE WHEN base.Url IS NULL THEN N'{}'
                                                     ELSE N'{"v2":"' + STRING_ESCAPE(base.Url, 'json') + N'"}' END)) END
                FROM ConnectorProfiles AS p
                JOIN PlatformAccounts AS a
                  ON a.TenantId = p.TenantId
                 AND a.Platform = LTRIM(RTRIM(p.OutboundAdapter))
                CROSS APPLY (SELECT CASE WHEN ISJSON(p.OutboundSettings) = 1 AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                                         THEN p.OutboundSettings END AS Settings) AS json
                CROSS APPLY (SELECT NULLIF(LTRIM(RTRIM(JSON_VALUE(json.Settings, N'$.baseUrl'))), N'') AS Url) AS base
                OUTER APPLY (
                    SELECT N'{' + STRING_AGG(
                               CAST(N'"' + STRING_ESCAPE(r.[key], 'json') + N'":'
                                    + CASE r.[type] WHEN 0 THEN N'null'
                                                    WHEN 1 THEN N'"' + STRING_ESCAPE(r.[value], 'json') + N'"'
                                                    ELSE r.[value] END AS nvarchar(max)),
                               N',') WITHIN GROUP (ORDER BY r.[key]) + N'}' AS Json
                    FROM OPENJSON(json.Settings) AS r
                    WHERE r.[key] COLLATE Latin1_General_BIN2
                          NOT IN (N'baseUrl', N'clientId', N'clientSecretRef', N'clientSecret', N'tokenUrl')
                ) AS rest;
                """);

            migrationBuilder.DropColumn(
                name: "OutboundAdapter",
                table: "ConnectorProfiles");

            migrationBuilder.DropColumn(
                name: "OutboundSettings",
                table: "ConnectorProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A condição da volta, antes de qualquer escrita: o perfil achatado tem um adapter de saída, uma base e uma
            // credencial. O que a forma nova tiver além disso não teria para onde ir. A base vazia de outra versão não é dado:
            // a tela a grava assim quando o campo é apagado. JSON_QUERY devolve nulo para o baseUrls que não é objeto nem
            // lista, e o OPENJSON de nulo não tem linha.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM PlatformAccounts GROUP BY TenantId HAVING COUNT(*) > 1)
                   OR EXISTS (SELECT 1 FROM Connectors GROUP BY AccountId HAVING COUNT(*) > 1)
                   OR EXISTS (
                       SELECT 1 FROM Connectors AS c
                       JOIN PlatformAccounts AS a ON a.Id = c.AccountId
                       WHERE c.Adapter COLLATE Latin1_General_BIN2 <> a.Platform COLLATE Latin1_General_BIN2
                   )
                   OR EXISTS (
                       SELECT 1 FROM Connectors AS c
                       CROSS APPLY OPENJSON(CASE WHEN ISJSON(c.Settings) = 1 AND LEFT(LTRIM(c.Settings), 1) = N'{'
                                                 THEN JSON_QUERY(c.Settings, N'$.baseUrls') END) AS b
                       WHERE b.[key] COLLATE Latin1_General_BIN2 <> N'v2'
                         AND NULLIF(LTRIM(RTRIM(b.[value])), N'') IS NOT NULL
                   )
                   OR EXISTS (
                       SELECT 1 FROM Connectors AS c
                       CROSS APPLY OPENJSON(CASE WHEN ISJSON(c.Settings) = 1 AND LEFT(LTRIM(c.Settings), 1) = N'{'
                                                 THEN c.Settings END) AS e
                       WHERE e.[key] COLLATE Latin1_General_BIN2 IN (N'baseUrl', N'clientId', N'clientSecretRef', N'tokenUrl')
                   )
                    THROW 50000, N'AddPlatformAccountsAndConnectors: o banco já usa a forma nova além do que o perfil achatado comporta (mais de uma conta por tenant, mais de um conector por conta, conector de adapter diferente da plataforma, base de versão diferente da v2 gravada, ou chave da forma achatada nas settings do conector). Desfazer perderia dado; restaure um backup.', 1;
                """);

            migrationBuilder.AddColumn<string>(
                name: "OutboundAdapter",
                table: "ConnectorProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OutboundSettings",
                table: "ConnectorProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            // A conta do tenant e o conector dela — a trava acima garante que há no máximo um de cada —; sem conector, só a
            // credencial da conta. O JSON_MODIFY com valor nulo tira a chave: o campo que a conta não tem não volta como null.
            migrationBuilder.Sql(
                """
                UPDATE p
                SET OutboundAdapter = chosen.Adapter,
                    OutboundSettings = chosen.Json
                FROM ConnectorProfiles AS p
                CROSS APPLY (
                    SELECT TOP (1)
                        COALESCE(c.Adapter, a.Platform) AS Adapter,
                        JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(
                            COALESCE(json.Settings, N'{}'),
                            N'$.baseUrls', NULL),
                            N'$.baseUrl', JSON_VALUE(json.Settings, N'$.baseUrls.v2')),
                            N'$.clientId', a.ClientId),
                            N'$.clientSecretRef', a.ClientSecretRef),
                            N'$.tokenUrl', a.TokenUrl) AS Json
                    FROM PlatformAccounts AS a
                    LEFT JOIN Connectors AS c ON c.AccountId = a.Id
                    CROSS APPLY (SELECT CASE WHEN ISJSON(c.Settings) = 1 AND LEFT(LTRIM(c.Settings), 1) = N'{'
                                             THEN c.Settings END AS Settings) AS json
                    WHERE a.TenantId = p.TenantId
                    ORDER BY a.Id, c.Id
                ) AS chosen;
                """);

            migrationBuilder.DropTable(
                name: "Connectors");

            migrationBuilder.DropTable(
                name: "PlatformAccounts");
        }
    }
}
