## Why

A integração manual e o agendamento oferecem empresas que não existem. O `companies.json` é um mock (`12345678`,
"Empresa Emitente LTDA"), e a execução descobre pelo catálogo fixo dos XMLs de exemplo:

- **Para um tenant do D365:** nenhuma empresa do dropdown casa com um grupo da tabela. Só a integração automática lê o
  ERP.
- **Em qualquer ambiente:** o mock é servido igual para todos os tenants (ADR-0028), inclusive fora de Development.

Os cards só contam o dia de hoje. Quem quer ver a semana, ou separar a NFS-e ignorada da NF-e, só tem a tabela.

O CNPJ alfanumérico, válido para inscrições novas desde julho de 2026, perde as letras em silêncio na descoberta, na
montagem e no adapter da Avalara. `12ABC34501DE35` vira `123450135`, e a nota cai no card de uma empresa que não existe.

## What Changes

- **O diretório vem do ERP.**
  - **A fonte:** o `D365CompanyDirectory` lê a `FiscalEstablishments`, a entidade padrão da ApplicationSuite, entre
    empresas e filtrada pelas `companies` do perfil, com o token e as settings que o coletor já usa. Não há entidade
    nova no D365: a razão das nossas `FS*` (ADR-0022) não vale aqui, porque a Microsoft publica esta.
  - **A chave:** a empresa é o CNPJ completo do estabelecimento, normalizado, como string. A filial é o
    `FiscalEstablishmentId` (`Matriz`, `SP-01`, `SAL-01`, `RJ-01`). É a mesma chave dos cards e do modal, sem tradução.
  - **O cadastro, e não os documentos:** um estabelecimento sem nota aparece, como o `RJ-01`.
  - **O tenant:** a porta `ICompanyDirectory` passa a receber o tenant de quem está logado. A implementação é a do
    adapter de entrada do perfil. Um ERP sem implementação recebe a resposta "este ERP não tem diretório", e não uma
    lista inventada.
  - **O mock só em Development:** o `companies.json` e o catálogo local ficam como fallback explícito de desenvolvimento,
    para o tenant cujo ERP não tem implementação. É o mesmo desenho do Azure CLI do D365. Fora de Development, nunca.
  - **A role:** a `FSFiscalHubIntegration` ganha o privilégio padrão `FiscalEstablishmentEntityView`. A mudança já está
    no disco, sem commit, e vai junto com a fatia. Ela precisa de build e deploy, e não precisa de sync de banco.
- **A descoberta por período do D365.** Não estava no pedido, mas a prova do agendamento depende dela: hoje a execução
  manual ou agendada de um tenant do D365 descobre pelo catálogo dos XMLs e acha 0 notas.
  - **O que lê:** o `D365DocumentDiscovery` lê o dia fiscal (`FiscalDocumentDate`) e os estabelecimentos da empresa e da
    filial, resolvidos pelo diretório. Filtra pelo número, quando houver.
  - **A referência:** é a mesma que o coletor publica: chave natural, locator, origem e grupo. A nota cai no mesmo card,
    sem linha duplicada.
  - **A escolha:** o runner escolhe a descoberta pelo adapter de entrada do perfil, pela mesma regra do diretório.
  - **A fila:** a descoberta por período e o reprocesso publicam na fila de descoberta, a do coletor. As duas filas
    consomem uma mensagem por vez, mas cada uma por conta própria. Em filas diferentes, duas cópias da mesma nota
    passariam juntas pela idempotência.
  - **O reprocesso:** ele pergunta primeiro à descoberta do ERP do tenant e, em Development, ao catálogo local. A nota do
    D365 passa a ser reprocessável, e a do XML de exemplo continua.
- **O CNPJ sem pontuação, com as letras.**
  - **A função:** uma função pura no Domain tira só `.`, `/`, `-` e espaço, e preserva as letras e a caixa.
  - **Onde vale:** na descoberta, na montagem, no diretório, na tradução de estabelecimentos da Avalara, no parceiro do
    payload e na derivação do caminho de XML.
  - **O CNPJ numérico:** o resultado é o mesmo de hoje, e não há nada a migrar.
  - **A tela:** a máscara passa a ser pelo tamanho (14 caracteres), e não pelo tipo do caractere.
- **Os filtros dos cards.**
  - **O período:** o dia, que é o padrão, ou 7, 15 e 30 dias, pelo relógio do navegador. N dias são hoje e os N−1
    anteriores. Há também um período personalizado, de uma data a outra (pedido da conferência na tela, 2026-10-02).
  - **O modelo:** todos, ou um modelo. As opções trazem sempre os modelos que o hub conhece (`55`, `57` e `SE`), mais os
    que as notas do período trazem.
  - **A contagem vai para o servidor:** `GET /groups/totals?from&to` devolve as contagens por modelo. Os cards deixam de
    somar no navegador os 200 grupos mais recentes, que numa janela de 30 dias truncariam.
  - **O grupo ganha o modelo**, com a coluna "Modelo" na tabela.
  - **O modal lista exatamente a linha:** empresa, filial, dia, tipo, modelo e modo. Isso fecha o item do STATUS "O modal
    do grupo não filtra pelo tipo e pelo modo", e é o que faz o filtro por modelo bater com o modal.
  - **A tabela continua o histórico completo.** Os filtros são dos cards.
- **O mapa `establishments` continua configuração digitada, e não é semeado pelo diretório.**
  - **Onde ele está hoje:** não está na tela. Ele existe só no seed e por SQL, e a tela o preserva ao salvar.
  - **Por que não semear:** os códigos são da plataforma e nunca vêm do ERP (ADR-0026 §3). O diretório só daria a chave,
    e uma entrada sem os códigos é rejeitada do mesmo jeito.
  - **O próximo passo:** a tela da tradução, com as linhas vindas do diretório, fica registrada no STATUS.
- **ADR-0032.** Registra:
  - o diretório pela entidade padrão;
  - a porta com o tenant e o fallback só em Development;
  - a descoberta por período e a fila dela;
  - o CNPJ normalizado com as letras;
  - os filtros e a contagem no servidor;
  - a decisão do `establishments`.
- **BREAKING (contrato interno e do front):**
  - a `ICompanyDirectory` recebe o tenant;
  - o `/groups` ganha o `model`, e a chave do grupo passa a incluir o modelo;
  - a consulta do modal recebe tipo, modelo e modo.

  Front e back sobem juntos, e não há cliente em produção.

## Capabilities

### New Capabilities

- `company-directory`: o que ela cobre:
  - o diretório de empresas e filiais do tenant logado;
  - a escolha da implementação pelo adapter de entrada;
  - o fallback só em Development;
  - a leitura do D365 pela `FiscalEstablishments`;
  - as respostas de "sem diretório" e de falha da origem;
  - o que os dropdowns mostram.
- `period-discovery`: o que ela cobre:
  - a descoberta por período escolhida pelo adapter de entrada;
  - a implementação do D365, pelo dia fiscal e pelo estabelecimento, com a mesma referência do coletor;
  - a fila de descoberta como destino da execução manual, da agendada e do reprocesso;
  - a busca por chave do reprocesso.
- `tax-identifier-normalization`: a regra do CNPJ e do CPF sem pontuação, com letras e caixa preservadas, num lugar só,
  e onde ela vale.

### Modified Capabilities

- `document-grouping`: quatro requisitos mudam:
  - a empresa passa a ser o CNPJ normalizado, e não "só dígitos";
  - os cards ganham os filtros de período e de modelo, contados no servidor;
  - o grupo ganha o modelo, e o modal lista exatamente a linha;
  - a máscara passa a ser pelo tamanho.
- `d365-change-feed`: a empresa do grupo passa a ser o `FiscalEstablishmentCNPJCPF` normalizado, com as letras.
- `d365-document-assembly`: o CNPJ e o CPF das partes e do estabelecimento passam a ser normalizados, com as letras. O
  NCM e o CFOP continuam só com dígitos.
- `avalara-document-contract`: duas regras mudam:
  - a chave da tradução de estabelecimentos é comparada pelo CNPJ normalizado;
  - o parceiro leva o `cnpj` quando o documento tem 14 caracteres.
- `inbound-source-resolution`: a descoberta local por período deixa de atender a integração manual e a agendada de
  qualquer tenant. Ela vira o fallback de desenvolvimento. Ela continua publicando com a origem `Xml`, e a nota de exemplo
  continua reprocessável.

## Non-goals

- **A tela da tradução `establishments`.** Ela fica para o próximo passo, com as linhas vindas do diretório. Esta fatia só
  decide e documenta.
- **Filtrar a tabela de grupos pelo período ou pelo modelo.** A tabela continua o histórico completo.
- **Um diretório ou uma descoberta por período para a Avalara ou para o iScala.** A porta permite, mas nenhum entra
  aqui.
- **O teste de credencial lendo a `FiscalEstablishments`.** A prova da role é o próprio dropdown carregando com a
  credencial do conector.
- **Cache do diretório no servidor.** O front já guarda por 5 minutos.
- **Persistir a origem no registro do documento.** O reprocesso resolve pela ordem das descobertas (design D5).
- **O agendamento de um tenant sem descoberta.** Hoje ele retenta a cada passada, como qualquer falha de agendamento, e
  continua assim.
- **Validar o dígito verificador do CNPJ, ou forçar maiúsculas.** Isso é conteúdo fiscal (ADR-0026). O hub tira a
  pontuação e mais nada.
- **Mudar o NCM, o CFOP ou o CEP.** Continuam só com dígitos.
- **Entidade nova no D365, ou sync de banco.** Só a role muda.

## Impact

- **Domain:** a função de normalização do CNPJ e do CPF.
- **Application:**
  - `Directory`:
    - a `ICompanyDirectory` com o tenant e a origem;
    - a escolha pelo adapter de entrada, com o fallback de desenvolvimento;
    - a consulta que o host chama, com as respostas de "sem diretório" e de falha.
  - `Inbound` e `Integrations`:
    - a escolha da `IDocumentDiscovery` pelo adapter de entrada;
    - o runner publicando na fila de descoberta;
    - o reprocesso pela ordem das descobertas.
  - `Metadata`: a derivação do caminho de XML com a normalização.
  - `Queries`: o grupo com o modelo, o modal pela linha inteira e as contagens por modelo num período.
- **Infrastructure:** o `SqlDocumentQueries`, sem migração (o `DocumentModel` já é gravado).
- **Adapters:**
  - `Ingress.D365Poll`: o diretório, a descoberta por período, a leitura comum da `FiscalEstablishments`, o mapeamento
    comum da referência (coletor e descoberta) e a normalização no grupo e na montagem;
  - `Outbound.Avalara`: a normalização na tradução e no parceiro;
  - `Directory.Json` e `Discovery.Local`: a assinatura nova e o registro como fallback de desenvolvimento.
- **Host:**
  - o diretório pelo tenant, com as respostas de erro;
  - o `GET /groups/totals`;
  - o `/groups` com o modelo, e o modal com tipo, modelo e modo;
  - o runner e o reprocesso na fila de descoberta;
  - o fallback só em Development.
- **Dashboard:**
  - os filtros e os cards da `GroupsPage`;
  - a coluna "Modelo" e o modal pela linha;
  - a máscara pelo tamanho;
  - os dropdowns e as tabelas da `IntegrationsPage` com a máscara e as mensagens;
  - `types.ts` e `client.ts`;
  - os testes do `vitest`.
- **D365:**
  - o `FSFiscalHubIntegration.xml` e a nota da receita `06`, que já estão no disco;
  - o `d365/README.md` e o `04` §7: a `FiscalEstablishments` passa a ser lida pelo hub;
  - a coleção do Postman.
- **Docs:**
  - o ADR-0032, e as linhas de revisão nos ADRs 0013, 0014 e 0028;
  - o RUNNING;
  - o STATUS;
  - o CLAUDE.md §7 e o contexto do `openspec/config.yaml`: o privilégio padrão na role.
- **Testes:**
  - a normalização;
  - o diretório e a descoberta do D365, com HTTP falso;
  - a escolha pelo adapter e o fallback;
  - a fila do runner;
  - o reprocesso;
  - as contagens e o modal no SQLite;
  - a janela do período e a máscara no front.
