-- Antes de desfazer a M3 da change platform-account-and-connectors (AddConnectorModulesAndIntegrations, design D13).
--
-- QUANDO: contra o banco, ANTES de desfazer a M3 — e guarde a saída num arquivo (por exemplo, sqlcmd ... -o caminhos.txt).
-- O Down da M3 devolve ao tenant a lista de módulos, mas perde o caminho das integrações, inclusive o da de notas fiscais:
-- antes da M3 o envio lê o Avalara:DocumentsPath, e a migração não lê configuração para saber se o caminho gravado é o que
-- o host preencheu ou um que o Admin editou pela tela. Com esta saída guardada, "a gente perdeu o caminho" vira "a gente
-- tem o caminho num arquivo".
--
-- O QUE FAZ: só lê. Nenhum INSERT, UPDATE ou DELETE. Lê a forma da M3 (as tabelas ConnectorModules e
-- ConnectorIntegrations) e devolve uma linha por integração ligada: o tenant, a chave e o caminho gravado (nulo é o que o
-- host ainda não preencheu; vazio é a integração inativa).

SET NOCOUNT ON;

SELECT a.TenantId, i.[Key], i.Endpoint
FROM ConnectorIntegrations AS i
JOIN ConnectorModules AS m ON m.Id = i.ModuleId
JOIN Connectors AS c ON c.Id = m.ConnectorId
JOIN PlatformAccounts AS a ON a.Id = c.AccountId
ORDER BY a.TenantId, i.[Key];
