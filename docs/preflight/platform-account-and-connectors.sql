-- Pré-voo da change platform-account-and-connectors (design D13).
--
-- QUANDO: contra o banco do cliente, ANTES de começar o deploy — antes de parar o host antigo. As migrações rodam na
-- subida do host novo, cada uma na própria transação: se uma trava da M2 disparar ali, a M1 já terá convertido o ambiente,
-- sem volta, e o host antigo não roda mais sobre o banco. Este script traz a conferência para antes disso, quando o host
-- antigo ainda está no ar e a tela dele ainda serve para acertar o dado.
--
-- O QUE FAZ: só lê. Nenhum INSERT, UPDATE ou DELETE. Lê a forma de ANTES da change (com a coluna Environment e as seções
-- por ambiente) e devolve uma linha por perfil e por trava que dispararia: a da M1, as cinco do Up da M2 e as duas do Up
-- da M3 que a forma de antes já mostra. Nenhuma linha quer dizer que nenhuma trava dispara. O resultado não traz valor
-- nenhum das settings: só o tenant e a trava. As outras duas travas da M3 (tenant com mais de um conector, conector de
-- adapter fora da grafia) não têm como existir antes: os conectores nascem na M2, um por tenant, e sempre Avalara.
--
-- As chaves conferidas são as que as settings de saída terão depois da M1: as da seção do ambiente gravado e as da raiz
-- que não são seção. Uma chave da raiz que a seção sobrescreve também é conferida, e pode dar um alarme a mais. O script
-- não decide nada: se ele divergir das migrações para menos, a trava da migração ainda segura.
--
-- O QUE FAZER COM CADA LINHA (com o host antigo no ar):
--   M1  ambiente sem seção         grave no perfil o ambiente que a instância atende, ou a seção dele.
--   M2  settings sem adapter       escolha o adapter de saída (Avalara), ou esvazie as settings de saída.
--   M2  adapter fora da grafia     grave o adapter de saída exatamente como Avalara.
--   M2  clientSecret em texto      é segredo vazado: rotacione-o na plataforma, só depois tire o valor das settings, e,
--                                  depois da migração, grave o segredo novo na conta de plataforma pela tela.
--   M2  chave em outra caixa       renomeie a chave para a grafia que a conversão lê (baseUrl, clientId, clientSecretRef,
--                                  clientSecret, tokenUrl).
--   M2  baseUrls nas settings      tire o baseUrls das settings de saída: a conversão o escreve a partir da baseUrl.
--   M3  lista de módulos ilegível  grave a lista pela tela, ou acerte-a para Fiscal, Contabil e Inventario, nessa grafia.
--   M3  módulos sem conector       sem adapter de saída, o tenant não terá conector onde os módulos fiquem: escolha o
--                                  adapter de saída (Avalara), ou deixe só o Fiscal na lista.

SET NOCOUNT ON;

WITH Profiles AS (
    SELECT p.TenantId,
           p.Environment,
           p.OutboundAdapter,
           p.OutboundSettings,
           p.Modules,
           CASE WHEN ISJSON(p.OutboundSettings) = 1 AND LEFT(LTRIM(p.OutboundSettings), 1) = N'{'
                THEN p.OutboundSettings END AS Json
    FROM ConnectorProfiles AS p
),
FlatKeys AS (
    -- As chaves da seção do ambiente gravado (achada sem distinguir maiúscula, como a M1 a acha).
    SELECT pr.TenantId, s.[key], s.[value]
    FROM Profiles AS pr
    CROSS APPLY OPENJSON(pr.Json) AS e
    CROSS APPLY OPENJSON(CASE WHEN e.[type] = 5
                               AND LOWER(e.[key]) = LOWER(LTRIM(RTRIM(COALESCE(pr.Environment, N'')))) COLLATE Latin1_General_BIN2
                              THEN e.[value] END) AS s
    UNION ALL
    -- As chaves da raiz que não são seção.
    SELECT pr.TenantId, r.[key], r.[value]
    FROM Profiles AS pr
    CROSS APPLY OPENJSON(pr.Json) AS r
    WHERE LOWER(r.[key]) NOT IN (N'sandbox', N'production')
)
SELECT N'M1: ambiente sem seção' AS Trava, pr.TenantId
FROM Profiles AS pr
WHERE EXISTS (SELECT 1 FROM OPENJSON(pr.Json) AS e WHERE LOWER(e.[key]) IN (N'sandbox', N'production'))
  AND NOT EXISTS (SELECT 1 FROM OPENJSON(pr.Json) AS e
                  WHERE e.[type] = 5
                    AND LOWER(e.[key]) = LOWER(LTRIM(RTRIM(COALESCE(pr.Environment, N'')))) COLLATE Latin1_General_BIN2)

UNION ALL
SELECT N'M2: settings sem adapter', pr.TenantId
FROM Profiles AS pr
WHERE LTRIM(RTRIM(pr.OutboundAdapter)) = N''
  AND ((pr.Json IS NULL AND LTRIM(RTRIM(pr.OutboundSettings)) NOT IN (N'', N'{}'))
       OR EXISTS (SELECT 1 FROM FlatKeys AS k WHERE k.TenantId = pr.TenantId))

UNION ALL
SELECT N'M2: adapter fora da grafia', pr.TenantId
FROM Profiles AS pr
WHERE LTRIM(RTRIM(pr.OutboundAdapter)) <> N''
  AND LTRIM(RTRIM(pr.OutboundAdapter)) COLLATE Latin1_General_BIN2 <> N'Avalara'

UNION ALL
SELECT DISTINCT N'M2: clientSecret em texto', k.TenantId
FROM FlatKeys AS k
WHERE LOWER(k.[key]) = N'clientsecret'
  AND NULLIF(LTRIM(RTRIM(k.[value])), N'') IS NOT NULL

UNION ALL
SELECT DISTINCT N'M2: chave em outra caixa', k.TenantId
FROM FlatKeys AS k
WHERE LOWER(k.[key]) IN (N'baseurl', N'clientid', N'clientsecretref', N'clientsecret', N'tokenurl')
  AND k.[key] COLLATE Latin1_General_BIN2 NOT IN (N'baseUrl', N'clientId', N'clientSecretRef', N'clientSecret', N'tokenUrl')

UNION ALL
SELECT DISTINCT N'M2: baseUrls nas settings', k.TenantId
FROM FlatKeys AS k
WHERE LOWER(k.[key]) = N'baseurls'

UNION ALL
SELECT N'M3: lista de módulos ilegível', pr.TenantId
FROM Profiles AS pr
WHERE LTRIM(RTRIM(COALESCE(pr.Modules, N''))) <> N''
  AND (ISJSON(pr.Modules) = 0
       OR LEFT(LTRIM(pr.Modules), 1) <> N'['
       OR EXISTS (SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(pr.Modules) = 1 THEN pr.Modules END) AS m
                  WHERE m.[type] <> 1
                     OR m.[value] COLLATE Latin1_General_BIN2 NOT IN (N'Fiscal', N'Contabil', N'Inventario')))

UNION ALL
SELECT N'M3: módulos sem conector', pr.TenantId
FROM Profiles AS pr
WHERE LTRIM(RTRIM(pr.OutboundAdapter)) = N''
  AND EXISTS (SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(pr.Modules) = 1 THEN pr.Modules END) AS m
              WHERE m.[value] COLLATE Latin1_General_BIN2 <> N'Fiscal')

ORDER BY TenantId, Trava;
