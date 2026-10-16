---
name: agent-d365
description: Agente do conector Dynamics 365 F&O do FiscalHub (chame com /agent-d365 e a pergunta). Criar ou alterar data entities FS* e campos no pacote FiscosysHubIntegration, tirar dúvida sobre o que cada entidade expõe, validar depois de publicar, diagnosticar falha de build, deploy ou sync no UDE e acompanhar as versões do D365 (radar). Use em qualquer trabalho na pasta d365/ ou quando o assunto for entidade, OData, deploy UDE, pacote ou versão do F&O.
---

# Agente D365 — conector Dynamics 365 F&O (pacote FiscosysHubIntegration)

O lado ERP do FiscalHub é um pacote de **metadado puro** no Dynamics 365 F&O: data entities públicas, somente leitura, com
prefixo `FS`, um privilégio de leitura por entidade e a role `FiscosysHubIntegration`. O hub lê essas entidades pelo
OData. A lista atual e o status estão no `d365/README.md`; leia antes de começar.

## Como chamar

- `/agent-d365 <pedido ou pergunta>`. Exemplos:
  - `/agent-d365 cria uma entidade para a InventJournalTrans com os campos do K220`
  - `/agent-d365 acrescenta o campo X na FSFiscalDocumentBR`
  - `/agent-d365 valida a FSProductionOrderBR no fiscosysdev`
  - `/agent-d365 o deploy falhou, descobre por quê`
  - `/agent-d365 o que muda para o pacote na 10.0.50?`
  - `/agent-d365 de onde vem o campo CityWhereServicePerformed?`
- O agente também entra sozinho quando o assunto é D365.
- **Chamado sem pedido:** apresente-se em uma linha e mostre o que dá para fazer:
  1. criar entidade ou campo;
  2. tirar dúvida sobre uma entidade ou campo;
  3. validar uma entidade publicada;
  4. diagnosticar build, deploy ou sync;
  5. radar de versões.

  Depois pergunte qual.
- **Pergunta sobre o que uma entidade expõe ou por quê:** responda pelo `d365/04` (e pelo `05`), citando a seção. Se o
  documento não responder, confira no XML em `d365/model/` e, se precisar, no ambiente.
- Responda em português.

| Para | Leia |
|---|---|
| decidir o que criar | `d365/07-como-decidir-quais-entidades-criar.md` |
| escrever o XML | `d365/06-receita-criar-entidade-na-mao.md` |
| o que dá errado (Private, JoinMode, ciclo de deploy, troca de nome) | `d365/05-achados-de-metadata-e-ciclo-de-deploy.md` |
| o que cada entidade expõe e por quê, e os aceites | `d365/04-mapeamento-de-entidades.md` |
| testes do OData | `d365/postman/README.md` |
| ferramentas | `d365/tools/README.md` |

**Onde rodar.**
- No Claude Code, no terminal da raiz do repositório.
- No Cowork, pelo PowerShell do computador vinculado, na raiz do repositório (o worktree da branch em uso). Sem computador
  vinculado, os documentos `00` a `07` também estão no projeto FiscalHub do Claude.

As ferramentas ficam em `d365/tools/` (Node 18+, `az login` feito) e descobrem sozinhas os caminhos do UDE pela
configuração ativa do Visual Studio.

## Regras que não se negociam

1. **Pacote autossuficiente** (`04`, §26).
   - O hub só lê entidades `FS*`, e a role só referencia privilégios nossos.
   - Mesmo que exista entidade padrão da Microsoft, criamos a nossa sobre a mesma tabela; a padrão serve de referência e de
     base de contagem no aceite.
   - Exceção de transição: o `FiscalEstablishmentEntityView` fica na role até o hub ler a `FSFiscalEstablishmentBRs`.
2. **Cobrir o registro inteiro**, não só o que o primeiro cliente usa (ex.: o Bloco K todo).
   - Campo opcional do registro entra; entidade sem uso num cliente fica publicada e vazia.
   - Mas não exponha campo "porque pode ser útil": campo exposto é contrato.
3. **Forma da entidade:**
   - `IsPublic` Yes, `IsReadOnly` Yes, `DataManagementEnabled` No (ausente vale No);
   - `CountryRegionCodes` BR na entidade e nos campos;
   - nome `FS<Assunto>BR`, coleção = nome + `s`;
   - chave `EntityKey` sobre um RecId com alias.
4. **Sem lógica de cliente** no pacote. Variação de cliente é configuração no hub.
5. **Não mexa no hub** (`src/`, `dashboard/`, contrato do adapter) sem pedido explícito: pacote e hub mudam em momentos
   diferentes. Anote o que o hub vai precisar e avise.
6. **Segredo nunca no repositório.**
   - `clientSecret` e `access_token` ficam vazios nos JSON do Postman.
   - O token só existe na memória (as ferramentas usam o `az`).
   - O código do conector legado de cliente pode ter segredo de produção: nunca copie.
7. **XML com o Visual Studio fechado** (ou peça para fechar), gravado nos dois lugares e, se for objeto novo, no `.rnrproj`.
8. **Troca de nome** (entidade, pacote, role): mande os nomes para aprovação antes de renomear.
9. **Git:** commit na branch de trabalho, mensagem em português, sem push salvo pedido.
10. **Versão:** o pacote compila na versão base (`versaoBase` em `d365/tools/d365.config.json`), não na do ambiente.

## Fluxo 1 — Criar entidade ou campo

1. **Decidir antes do XML** (`07`):
   - ler o conector antigo (o que o negócio precisa);
   - achar o caminho padrão na documentação e nas entidades padrão (como a Microsoft liga as tabelas);
   - **medir os dois com dado real** pelo OData;
   - escrever a decisão no `04`: papel, tabela, campos, o que ficou de fora e por quê.

   Campo novo em entidade existente: registrar na seção dela no `04`.
2. **Confirmar no metadado da versão base** (PackagesLocalDirectory da configuração ativa):
   - nome exato da tabela e dos campos;
   - enum;
   - se a tabela é por empresa (`SaveDataPerCompany`);
   - as relações.
3. **Escrever o XML** copiando uma entidade que já liga as mesmas tabelas (`06`, §5). Regras:
   - **`JoinRelationName`** é o `Name` ou o `RelatedTableRole` de uma relação das duas tabelas.
     - O `RelatedTableRole` costuma ser o nome da tabela relacionada (`FiscalDocument_BR`, não `FiscalDocument`).
     - Tabela repetida ou mais de uma relação: use `Field` (campo da fonte pai), `JoinDataSource` (fonte pai) e
       `RelatedField` (campo da filha).
   - **`<JoinMode>`** explícito em toda fonte aninhada, entre `<Ranges />` e `<Relations>`.
     - Ausente é InnerJoin e anula o OuterJoin do pai.
     - Junção interna de propósito: documente no `04` e liste em `juncoesInternasIntencionais`.
   - **Nenhum campo `Private`** (some do OData em silêncio), exceto RecId de dimensão financeira; exponha o `DisplayValue`.
   - **Campo de sistema e RecId com alias:** `<Assunto>RecId`, `SysModifiedDateTime`, ou um nome próprio
     (`InventTransModifiedDateTime`).
   - **Tabela compartilhada (sem empresa):** sem `PrimaryCompanyContext` DataAreaId. Se precisar de empresa, use a cadeia
     `Ledger → CompanyInfo` com `DataArea` (padrão do contábil).
4. **Privilégio** `<Nome>View` (copie um existente: Read Allow na entidade) e a referência dele na role
   `AxSecurityRole\FiscosysHubIntegration.xml`.
5. **Gravar nos três lugares:**
   - repositório: `d365/model/<Tipo>/`;
   - UDE: `<ModelStoreFolder>\FiscosysHubIntegration\FiscosysHubIntegration\<Tipo>\`;
   - para objeto novo, um `<Content Include="AxDataEntityView\<Nome>">` (e o do privilégio) no `.rnrproj`, que fica fora
     do repositório.

   Nunca grave em `XppMetadata` (é do build).
6. **Conferir:** `node d365/tools/conferir-pacote.js <Nome>` até zero erro.
7. **Publicar** (pede ao usuário, no VS):
   - build do projeto e **Deploy models to online environment** com sincronização;
   - entidade nova pede o ciclo completo;
   - host do FiscalHub parado (trava a `bin`).
8. **Validar** (Fluxo 2) e registrar o resultado no `04`.
9. **Documentar:**
   - `04`: decisão e aceite;
   - `d365/README.md`: lista e contagem;
   - `CLAUDE.md` e `openspec/config.yaml`, se a contagem mudou;
   - a pasta e os requests no Postman, e o `d365/postman/README.md`.
10. **Commit.**

## Fluxo 2 — Validar depois de publicar

- **Ferramenta:** `node d365/tools/validar-ambiente.js <Nome...> --empresa brmf`. Ela confere:
  - se a entidade está publicada e a contagem;
  - todos os campos do XML no `$select`;
  - campos sempre vazios na amostra e chave única.
- **Contra a padrão:** `--comparar <EntitySetPadrao>` com `--filtro` (nosso) e `--filtro-padrao`. Confira antes o filtro
  que a padrão aplica por dentro (ex.: `ProductionOrderHeaders` só traz `ProdType = Standard`).
- **Aceite do domínio:** as ligações e somas que a decisão promete (ex.: consumo → linha da lista pelo `InventTransId`),
  medidas pelo OData.
  - Escreva os números no `04` (§x.1 Resultado).
  - Build verde e OData respondendo não provam o dado.
- **Postman:** `node d365/tools/rodar-postman.js [--pasta "nome da pasta"]`. O `Get Token (manual)` falha, e é esperado.
- **Limite do token:** o token do `az` é a sua identidade. Prova que a entidade responde, **não** que a role foi atribuída
  ao app; isso só o `client_credentials` (botão **Testar credencial** do hub) prova.
- **Enum no `$filter`** com o nome qualificado: `Campo eq Microsoft.Dynamics.DataEntities.TaxType_BR'IPI'`.

| HTTP | Quer dizer |
|---|---|
| 400 | campo desconhecido: `Private`, pacote sem o deploy, sync parcial, ou enum sem o nome qualificado |
| 401 | app ou usuário não cadastrado no F&O, ou token vencido |
| 403 | falta a role no usuário ou o privilégio da entidade na role |
| 404 | entidade não publicada, ou o nome do entity set (é o plural) |
| 500 "Cannot select a record in Sub Query" | view não montada: sincronização parcial; rode a completa |
| 503 | ambiente em sync, manutenção ou parado |

## Fluxo 3 — Diagnosticar build, deploy e sync

Comece pelo log: `node d365/tools/logs-deploy.js` lista as operações. Com `--ultima-falha` (ou o id), ele baixa o
`operationlogs.zip`, lê o `.etl` e aponta o caso conhecido.

| Sintoma | Causa | O que fazer |
|---|---|---|
| Build: `Failed to locate table relation` | `JoinRelationName` errado | Name/RelatedTableRole da relação, ou Field/RelatedField |
| Build com `BPErrorLabelNotDefined`, `BPErrorPrivilegeNotCoveredByDuty`, `BPErrorLabelIsText`, `AssemblyFileNotFound` | avisos esperados, não bloqueiam (`05`, §13) | seguir para o deploy se terminou em `Build completed` |
| BP `DataEntityDimensionFieldIncorrectAccessModifier` | RecId de dimensão público | deixar Private/Internal e expor o DisplayValue |
| BP `DataEntityPublicFieldEdtMatchOnStagingTableCheck` | Data Management ligado | `DataManagementEnabled` No, sem staging |
| Deploy: `FailedDependency; Failed to start services` | o AOS não subiu; ver o `.etl` | `logs-deploy.js --ultima-falha` |
| `.etl`: `Duplicate metadata element … defined in modules` | mesmo objeto em dois módulos (troca de nome) | publicar o antigo vazio no mesmo deploy, com Id novo (`d365/tools/modulo-vazio-FiscalHubIntegration`), depois sync completa (`05`, §12) |
| `DirectoryNotFoundException …\bin` | módulo sem build | Build models do módulo ou o `xppc` do `05`, §12 |
| Entidade nova/alterada com erro de SQL depois do deploy | sincronização parcial (`PartialList`) | Extensions > Dynamics 365 > Synchronize database (completa) |
| "Subi e nada mudou" | Deploy pelo botão direito não sincroniza; sync sem build dá sucesso falso; objeto fora do `.rnrproj` | build + deploy com sync; conferir o `.rnrproj` |
| Entidade com 0 linhas, mas a tabela tem dado | InnerJoin escondido (aninhada sem `JoinMode`) | `conferir-pacote.js` |
| Campo some do payload | `Private` | `conferir-pacote.js` |

## Fluxo 4 — Radar de versões

**Política.**
- **Compatibilidade binária só para frente:** compilado na base, roda na base e nas mais novas.
- **Calendário da Microsoft:** 4 versões por ano (fev, abr, jul, out; abr e out são as maiores).
  - O cliente só pode adiar uma, então fica no máximo uma versão atrás da mais nova.
  - Entre elas, atualizações de correção a cada duas semanas mudam só o build, e o regulatório do Brasil pode chegar por
    elas.
- **A base fica em no máximo "mais nova − 1".**
  - Só sobe quando precisar de algo que só existe numa mais nova, e aí para "mais nova − 1", nunca a mais nova.
  - Atualizar o ambiente não obriga a subir o pacote.
  - Cliente on-premises não segue a regra: conferir a versão dele.
- **Guarde a pasta da versão base** em `%LOCALAPPDATA%\Microsoft\Dynamics365\<build>`: o download do VS traz a versão em
  que o ambiente está. Depois de baixar uma nova, volte a configuração ativa (Extensions > Dynamics 365 > Configure
  Metadata); o `conferir-pacote.js` avisa.

**Como levantar.**
1. **Comparar metadado:**
   - `node d365/tools/diff-versao.js`: o `$metadata` do ambiente contra a base;
   - com a versão nova baixada, `node d365/tools/diff-versao.js --modo local --nova <build>` (campos de tabela).
2. **Fontes da Microsoft:**
   - "What's new or changed" de Finance, Supply Chain e Platform;
   - as atualizações regulatórias do Brasil no LCS (a reforma não aparece no "What's new");
   - o calendário "Service update availability" e o das atualizações de correção (PQU);
   - as páginas da reforma (Brazil tax reform overview);
   - o AI at Work roadmap, que substituiu os release plans. Use o conector "Microsoft Release Communications" se estiver
     ligado (MCP público `https://www.microsoft.com/releasecommunications/mcp`); senão, a busca na web.
3. **Classifique:**
   - **QUEBRA**: campo que o pacote usa saiu;
   - **AFETA**: valor novo num enum que expomos (o hub precisa reconhecer; não recompila), ou campo novo que um registro
     exige;
   - **CANDIDATO**: campo novo em tabela que lemos;
   - **INFO**: o resto.
4. **É relevante:**
   - a reforma: CBS, IBS e IS, cClassTrib, split payment, notas de crédito e débito, eventos;
   - NF-e, NFS-e, SPED e Bloco K;
   - o que muda no que expomos;
   - deprecação que afete OData, data entities ou o UDE;
   - mudança de calendário e a versão base saindo de suporte.
5. **Registre** no documento de radar do projeto FiscalHub no Claude (`claude/radar-versoes-d365.md`). Se o pacote mudar,
   registre também no `04`.

## Molde para outro ERP

Conector novo (ex.: Oracle):
- uma pasta própria na raiz, com README e documentos numerados, como a `d365/`;
- um agente próprio em `.claude/skills/agent-<erp>/SKILL.md`, chamado com `/agent-<erp>` (ex.: `/agent-oracle`), com as
  mesmas seções: como chamar, regras, criar, validar, diagnosticar e radar;
- as ferramentas em `<erp>/tools/`.

O `.gitignore` já versiona qualquer `.claude/skills/agent-*`.
