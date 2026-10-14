using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalHub.Infrastructure.Migrations
{
    /// <summary>
    /// O ambiente sai do perfil e vira da instância (change <c>platform-account-and-connectors</c>, M1 do design D13): a
    /// seção do ambiente gravado vira as settings de saída, a seção do outro ambiente é descartada (D14), e a coluna
    /// <c>Environment</c> sai. As chaves da raiz que não são seção de ambiente ficam; numa chave que existe nos dois lugares,
    /// vale a da seção.
    /// <para>Settings que não são um objeto JSON ficam como estão: a leitura do adapter já as recusa, com o motivo. Settings já
    /// achatadas, sem seção nenhuma, passam inteiras. Um perfil com seção por ambiente cujo <c>Environment</c> não nomeia
    /// nenhuma delas faz a migração falhar antes de qualquer escrita: é dado ambíguo, e a decisão é de quem conhece o
    /// cliente.</para>
    /// <para><b>Irreversível.</b> Depois do <c>Up</c>, nenhuma linha guarda o ambiente, e a seção descartada não existe mais:
    /// não há de onde reconstruí-las (ADR-0036). O <c>Down</c> lança, antes de qualquer SQL.</para>
    /// </summary>
    public partial class MoveEnvironmentToInstance : Migration
    {
        internal const string IrreversibleMessage =
            "A migração MoveEnvironmentToInstance é de ida só: ela descartou a seção do outro ambiente e a coluna Environment, e "
            + "não há de onde reconstruí-las (ADR-0036). Para voltar a antes dela, recrie a base de desenvolvimento.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A trava vem antes de qualquer escrita: um perfil com seção por ambiente cujo Environment não nomeia nenhuma
            // delas (em branco, ou um valor legado) é dado ambíguo, e a migração para em vez de escolher uma seção ou esvaziar
            // o perfil no escuro — e para antes do DropColumn, enquanto ainda dá para acertar o dado.
            // A seção é achada por OPENJSON com comparação explícita, sem distinguir maiúscula ("Sandbox" e "sandbox"), e
            // não por caminho JSON, que distingue. O OUTER APPLY é de propósito: com a trava, a única linha que chega sem seção
            // é a já achatada, e para ela a raiz passa inteira. O TOP (1) protege da chave repetida. O resultado é remontado
            // com STRING_AGG: a string volta escapada, e o resto (número, booleano, objeto, lista) volta como o JSON que já
            // era. A comparação das chaves é binária: baseUrl e BaseUrl são chaves distintas em JSON. O Environment ganha o
            // COLLATE explícito porque a chave do OPENJSON é Latin1_General_BIN2 e a coluna tem a collation do banco: sem
            // ele, o SQL Server recusa o batch inteiro por conflito de collation, até num banco vazio (visto no harness).
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM ConnectorProfiles AS p
                    WHERE ISJSON(p.OutboundSettings) = 1
                      AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                      AND EXISTS (SELECT 1 FROM OPENJSON(p.OutboundSettings) AS e
                                  WHERE LOWER(e.[key]) IN (N'sandbox', N'production'))
                      AND NOT EXISTS (SELECT 1 FROM OPENJSON(p.OutboundSettings) AS e
                                      WHERE e.[type] = 5
                                        AND LOWER(e.[key]) = LOWER(LTRIM(RTRIM(COALESCE(p.Environment, N'')))) COLLATE Latin1_General_BIN2)
                )
                    THROW 50000, N'MoveEnvironmentToInstance: existe perfil com seção por ambiente cujo Environment não nomeia nenhuma seção. Acerte o dado antes de migrar.', 1;

                UPDATE p
                SET OutboundSettings = COALESCE(merged.Json, N'{}')
                FROM ConnectorProfiles AS p
                OUTER APPLY (
                    SELECT TOP (1) CAST(e.[value] AS nvarchar(max)) AS Section
                    FROM OPENJSON(p.OutboundSettings) AS e
                    WHERE e.[type] = 5
                      AND LOWER(e.[key]) = LOWER(LTRIM(RTRIM(COALESCE(p.Environment, N'')))) COLLATE Latin1_General_BIN2
                ) AS chosen
                CROSS APPLY (
                    SELECT N'{' + STRING_AGG(
                               CAST(N'"' + STRING_ESCAPE(k.[key], 'json') + N'":'
                                    + CASE k.[type] WHEN 0 THEN N'null'
                                                    WHEN 1 THEN N'"' + STRING_ESCAPE(k.[value], 'json') + N'"'
                                                    ELSE k.[value] END AS nvarchar(max)),
                               N',') WITHIN GROUP (ORDER BY k.[origin], k.[key]) + N'}' AS Json
                    FROM (
                        SELECT s.[key], s.[value], s.[type], 0 AS [origin]
                        FROM OPENJSON(chosen.Section) AS s
                        UNION ALL
                        SELECT r.[key], r.[value], r.[type], 1 AS [origin]
                        FROM OPENJSON(p.OutboundSettings) AS r
                        WHERE LOWER(r.[key]) NOT IN (N'sandbox', N'production')
                          AND NOT EXISTS (SELECT 1 FROM OPENJSON(chosen.Section) AS s2
                                          WHERE s2.[key] COLLATE Latin1_General_BIN2
                                              = r.[key] COLLATE Latin1_General_BIN2)
                    ) AS k
                ) AS merged
                WHERE ISJSON(p.OutboundSettings) = 1 AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{';
                """);

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "ConnectorProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException(IrreversibleMessage);
    }
}
