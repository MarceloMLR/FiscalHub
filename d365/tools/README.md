# Ferramentas do pacote D365

Scripts Node (18 ou mais novo, sem dependências) que automatizam a receita dos documentos `05` e `06`. Rodam na raiz do
repositório, na máquina de desenvolvimento do UDE. O agente `/agent-d365` (`.claude/skills/agent-d365/`) usa os mesmos scripts.

| Script | Quando | O que faz |
|---|---|---|
| `conferir-pacote.js [Entidade...]` | antes do build | Confere os três lugares (repositório, UDE e `.rnrproj`), privilégio e role, as regras de XML (`JoinMode`, `Private`, alias de campo de sistema, propriedades da entidade) e se cada tabela, campo e relação existe no metadado da versão base. Avisa se a configuração ativa do VS não é a versão base. |
| `validar-ambiente.js Entidade... [--empresa brmf]` | depois do build, deploy e sync | Para cada entidade: responde, contagem, todos os campos do XML no `$select`, campos sempre vazios na amostra e chave única. `--comparar <EntitySetPadrao>` conta a entidade padrão com o mesmo filtro (`--filtro`, `--filtro-padrao`). |
| `rodar-postman.js [--pasta "nome"]` | depois do deploy | Roda a collection do `d365/postman` pelo newman com o seu token do `az`. O `Get Token (manual)` falha, e é esperado. |
| `logs-deploy.js [id \| --ultima-falha]` | deploy ou sync falhou | Lista o histórico de operações do UDE (Dataverse) e, para uma operação, baixa o `operationlogs.zip`, lê o `.etl` e aponta o caso conhecido (módulo duplicado, falta da `bin`, sincronização parcial...). |
| `diff-versao.js [--modo local --nova <build>]` | radar de versão | Compara a versão base do pacote com uma mais nova: pelo `$metadata` do ambiente (entidades públicas e enums) ou entre duas versões baixadas no VS (campos de tabela). Destaca o que afeta o pacote e o que é do Brasil. |

`modulo-vazio-FiscalHubIntegration/` é o módulo antigo vazio usado na troca de nome do pacote (`05`, seção 12).

## Pré-requisitos

- `az login` com um usuário que tenha acesso ao ambiente (F&O e Dataverse). O token fica só na memória do processo: nenhum
  script imprime ou grava o token, e a cópia da collection com token é apagada no fim (e no começo da próxima execução, se
  uma anterior foi interrompida).
- Visual Studio com o UDE configurado. Os caminhos da máquina vêm da configuração de metadado ativa do VS (registro do
  Windows); para forçar: `D365_XPPCONFIG` (o `.json` da configuração), `D365_CUSTOM_METADATA` (a pasta
  `C:\CustomXppMetadata*`) e `D365_RNRPROJ` (o projeto, que fica fora do repositório).

## Configuração

`d365.config.json`: nome do pacote e da role, versão base, URL do F&O e do Dataverse, empresa de teste, os privilégios
padrão que a role ainda referencia (transição) e as junções internas de propósito (`04`, seções 15.0 e 22.0). Nada de
segredo.
