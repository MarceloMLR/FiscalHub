## Context

A motivação está no `proposal.md`; o porquê, nos **ADRs 0036 e 0037**. Aqui ficam o estado do código que molda o
desenho e as escolhas que os ADRs não precisavam fixar. O levantamento abaixo foi conferido no código (revisão de
2026-10-13), e não presumido.

**O perfil hoje** (`TenantConnectorProfile`, `src/FiscalHub.Application/Connectors/`):

- campos tipados: `TenantId`, `Environment`, `InboundAdapter`, `OutboundAdapter`, `SupportAdapter`, `Modules`;
- três blobs JSON: `InboundSettings`, `OutboundSettings`, `SupportSettings`;
- a divisão vem do ADR-0019: **campo comum a todos os adapters é tipado; o que muda por adapter é blob.**

**O `OutboundSettings` é o único que aninha por ambiente.** O `AvalaraOutboundSettings.Read` faz
`profile.Environment.ToLowerInvariant()` e entra na seção; a entrada e os chamados nunca tiveram esse nível. No
dashboard, o `splitOutbound` separa `{ sandbox, production, ...rest }`, o `buildConnectorPayload` remonta as duas
seções, e a tela desenha dois blocos, cada um com o próprio botão de teste. As chaves do mapa de segredos que o servidor
devolve (`outbound.sandbox.clientSecret`) também carregam a seção.

**Quem lê o ambiente.** São pelo menos cinco leitores diretos do `TenantConnectorProfile.Environment`:

- o `AvalaraOutboundSettings.Read`, que escolhe a seção;
- o `PlatformEstablishmentResolver`, que o põe na chave da listagem guardada e da recusa lembrada;
- o `ConnectorProfileService`, que o grava a partir da requisição e o devolve na leitura;
- o `AvalaraCredentialTest`, que o lê **e o sobrescreve** com o ambiente que veio na requisição do teste;
- o `/info`, no `Program.cs`, que o devolve com fallback para `Connector:Environment` e, por fim, `"Sandbox"`.

Mais a persistência (`SqlConnectorProfileStore`, `ConnectorProfileRow`, o seed) e cerca de 25 arquivos de teste que
atribuem o campo, que é `required`. Pela propriedade `AvalaraOutboundSettings.Environment`, o ambiente chega ainda à
chave do cache de token (`AvalaraTokenProvider`), aos motivos do despachante, da listagem e do provedor de token, e à
sonda `tools/AvalaraSandboxProbe`, que compila na solução e enxerga os internos do adapter. E há uma cadeia paralela, a
do teste de credencial: o `CredentialTestRequest.Environment` do host vai ao `ConnectorCredentialTestService`, que o
exige na saída, ao `CredentialTestBrake`, que chaveia por ele, ao `CredentialTestLog`, e à porta
`IConnectorCredentialTest.TestAsync(profile, environment)`, implementada pela Avalara **e pelo D365**. No dashboard, o
`CredentialTestButton` e o cliente da API o mandam. Os motivos citam o ambiente em texto
(`OutboundSettings.<ambiente>.…` e o `→ Sandbox` do caminho da tela).

**A chave de configuração já existe.** `Connector:Environment` está no `appsettings.json`, com `"Sandbox"`, e o `/info`
a lê como fallback, com mais um `?? "Sandbox"` no fim. O dashboard tem o próprio padrão (`info?.environment ??
'Sandbox'`). São os padrões silenciosos que o D6 proíbe.

**O nome do segredo** sai do `SecretNames.For(tenantId, kind, path, field)`:
`fh-{tenant}--{kind}--{path…}--{field}`, minúsculas, segmentos sem `--`, no máximo 127 caracteres. O `kind` é o enum
`ConnectorSettingsKind { Inbound, Outbound, Support }`, que também é o `Side` do teste de credencial. Na saída, o
ambiente entra no nome porque o caminho JSON do campo é `["sandbox"]`.

**A regra de nomes de segredo e quem varre o JSON.** O `ConnectorSecretFields` guarda só a regra de nome (qual campo é
de escrita, qual é referência), em qualquer nível. Quem percorre o JSON é o `ConnectorProfileService`, na gravação e na
leitura, e o `AvalaraOutboundSettings`, que recusa segredo em claro na leitura. O `clientId` não é campo de escrita.

**O despacho não escolhe adapter por nome.** A `DocumentPipeline` e o `StatusPoller` recebem **um**
`IComplianceDispatcher<GoodsInvoice>` do DI, e o `OutboundAdapter` nunca é lido na esteira. A comparação de nome que
existe é a do `PlatformEstablishmentResolver` (`l.Adapter == profile.OutboundAdapter`, exata), e ela só decide se a
listagem é usada. O `ConnectorCredentialTestService` faz outra, sem distinguir maiúscula, para achar o teste do lado.

**O caminho do envio mora no host.** É o `Avalara:DocumentsPath` (padrão `documents` no código; o
`appsettings.Development.json` traz `taxcompliance/v2/fiscal/dfe`). Leem-no o envio, a consulta de status
(`{DocumentsPath}/{id}/status`), o motivo do 404, a sonda, o `DispatchToMockTests` e o `RUNNING.md`. O caminho do token
também: sem `tokenUrl` na seção, o endpoint é a base mais o `Avalara:TokenPath`.

**A versão mora no caminho, e não na base.** A base gravada é a raiz do host (`http://localhost:5100/`), e os caminhos
trazem o prefixo (`taxcompliance/v2/fiscal/dfe`, `taxcompliance/v2/empresa`). O mock atende o token na raiz
(`/oauth/token`) e o envio nos dois caminhos.

**Os testes não rodam migração.** Os de Infrastructure usam SQLite em memória com `EnsureCreated`, e as funções JSON do
T-SQL não rodam ali. Mas há um teste que compara o modelo com o snapshot das migrações
(`The_model_has_no_change_pending_against_the_sql_server_migrations`): todo grupo que muda o modelo persistido sem trazer
a migração o deixa vermelho. O precedente de teste contra serviço externo é o `KeyVaultEmulatorFact`: opt-in por
variável de ambiente, pulado sem ela.

## Decisões

### D1. A credencial é campo tipado da conta; o resto do conector continua blob

A conta vira linha própria, com `Platform`, `ClientId`, `ClientSecretRef` e `TokenUrl` tipados.

É o ADR-0019 aplicado: credencial OAuth de client credentials é comum a toda plataforma de compliance. O que muda por
produto — as settings do conector — continua blob.

**O ganho concreto:** a derivação do nome do segredo e a checagem de dono deixam de precisar achar o campo dentro de um
JSON. A regra de nomes (`ConnectorSecretFields`) e a varredura do JSON continuam valendo para a entrada, os chamados e
as settings do conector; para a credencial da conta, deixam de ser necessárias.

**A credencial da plataforma fora do conector é regra nova.** Um `clientSecret` nas settings de um conector seria, pela
regra de nomes, levado ao cofre; um `clientId` nem é campo de segredo. A recusa dos dois nas settings do conector é
regra própria da gravação, e vem antes da conversão de segredos. Os outros segredos do conector (`apiKey`…) seguem a
regra de nomes, com escopo de conector no nome.

### D2. A chave do conector é o nome do adapter

Nada de campo novo de identificação. A chave precisa ser estável (entra no nome do segredo) e segura para o cofre (só
minúsculas, dígitos e hífen, sem `--`); o nome do adapter já é as duas coisas e já é a identidade que o código usa.

**Consequência aceita:** um tenant não pode ter dois conectores do mesmo adapter. Não há caso — duas contas Avalara do
mesmo cliente seriam duas contas.

### D3. Módulo e integração são tabelas, e não um blob

O que o adapter declara é código. O que o perfil liga é **dado consultável**: a esteira precisa perguntar pela
integração sem desserializar o JSON de todos os conectores do tenant.

- **conector** → N módulos ligados;
- **módulo** → N integrações ligadas;
- **integração** → `Endpoint` (texto, anulável) e `BatchSize` (anulável), e o `TenantId` repetido.

**A unicidade.** A regra "um conector por integração, por tenant" é a validação da gravação: é ela que responde o 400
nomeando os dois conectores. O banco guarda a mesma regra como rede de segurança, num índice único em
`(TenantId, chave da integração)`. Um índice não atravessa joins, e por isso a linha da integração repete o `TenantId`.

**A chave da integração é vocabulário do hub.** Ela é definida na Application, como os nomes de módulo, e os adapters a
usam ao declarar. Se a chave fosse do adapter, duas integrações de adapters diferentes nunca colidiriam, e a regra de
unicidade seria vazia — o cenário dos dois conectores na integração de notas fiscais não se sustentaria.

A **declaração** do adapter — quais módulos, quais integrações, qual versão de base, quais aceitam lote — vive em
código e é consultada por uma porta. Ela não é gravada: duplicar em dado o que o código já sabe cria a pergunta "e
quando divergem".

### D4. As URLs base são um mapa por versão, e a chave é rótulo

O conector guarda `{ "<versão>": "<url>" }` — para a Avalara, `v1` e `v2`. **Não** `baseUrlV1` e `baseUrlV2`.

**Por quê.** "v1" e "v2" são o vocabulário de um fornecedor. Dois campos fixos fariam o próximo adapter — que pode ter
uma base só, ou três — carregar nomes que não são dele.

**A chave é rótulo, e nunca pedaço da URL.** Ela escolhe a base, e nada a concatena. A base guardada é a que a
plataforma publica — na Avalara, a da `v2` é a raiz do host —, e o prefixo da API continua no caminho
(`taxcompliance/v2/…`). Sem isso escrito, alguém poria `…/taxcompliance/v2/` na base, e a listagem viraria
`…/v2/taxcompliance/v2/empresa`.

**O mapa vive nas settings do conector**, que já são blob, porque o conjunto de chaves é do adapter. A gravação o confere
contra a declaração: recusa a versão não declarada, exige as que as integrações ligadas usam, e recusa a URL fora da
regra de `https`. Essa última é nova: hoje a regra só existe na leitura (`AvalaraOutboundSettings`), e o `PUT` aceita
qualquer URL.

**Onde a validação é contornável, e o que isso custa.** O seed, a migração e o SQL direto gravam sem passar pela
gravação do perfil. O dano é limitado: uma versão sobrando no mapa é configuração morta, que nada lê; uma versão
faltando é pega no envio ("Conector sem URL base"); e a regra de `https` é refeita na leitura.

**A chave de versão em outra caixa é recusada, e não canonizada — ao contrário do nome da plataforma e do adapter (D17),
de propósito.** As duas situações parecem a mesma ("escreveu com a caixa errada, e a leitura compara exato"), mas não
são. A plataforma e o adapter são a **identidade de um registro**, escolhida de um conjunto fechado que o servidor
conhece: canonizar é o que mantém a gravação e a leitura falando do mesmo registro, e a mesma identidade não fica com
duas grafias no cofre e nos índices. A chave de versão é **conteúdo de um blob** que o adapter define, e a tela nunca a
digita — um `V2` só chega por quem montou o blob à mão. Ali a caixa diferente não é a mesma coisa escrita de outro
jeito: é sinal de que quem gravou não sabia o que estava gravando, e adivinhar esconderia isso. Recusar também impede
`v2` e `V2` de conviverem no mesmo mapa, onde nada as compararia. Quem for igualar as duas regras precisa desfazer um
desses dois motivos primeiro.

### D5. A integração sem endpoint é inativa, e isso é estado derivado

Não existe campo "ativa". Uma integração é inativa quando o endpoint está vazio — derivar evita o terceiro estado
("ligada, com endpoint, mas marcada como inativa") que ninguém sabe explicar.

O endpoint nulo também é inativo. Ele só existe entre a migração e a primeira subida do host, que o preenche (D10): é
marca de migração, e não estado do produto. A tela grava o caminho em branco como texto vazio, e nunca como nulo.

Quem precisa da integração para despachar trata a inativa como configuração do conector, pelo mesmo caminho da
credencial ausente: documento rejeitado, motivo que aponta a tela, nenhuma requisição.

### D6. O ambiente entra pela configuração do host, na chave que já existe

O valor é o `Connector:Environment`, que já está no `appsettings.json`, lido na subida e exposto por uma porta da
Application. Não se cria chave nova: duas chaves para o mesmo valor seriam a pergunta "qual vale".

**Não é o `IHostEnvironment`.** Aquele é o eixo Development/Staging/Production do ASP.NET; este é o eixo
sandbox/produção da plataforma de compliance. Um ambiente de desenvolvimento aponta para o sandbox da Avalara, e os
dois eixos não coincidem.

**O host recusa a subida** sem o valor ou com um valor fora dos dois. **E nenhum leitor tem padrão próprio:** saem o
`?? "Sandbox"` do `/info` e o `?? 'Sandbox'` do selo no dashboard. Antes de o `/info` responder, o selo não afirma
ambiente nenhum, como o selo da integração automática já faz.

**O `PUT /connector` simplesmente não tem o campo.** Um dashboard antigo que ainda mande `environment` tem o campo
ignorado, e não 400: 400 quebraria a tela de quem não recarregou, e o campo ignorado não causa dano. O mesmo vale para a
requisição do teste de credencial.

### D7. O `/info` mantém a forma da resposta, e muda a fonte

`modules` continua uma lista plana de `Fiscal`, `Contabil`, `Inventario`. O que muda é de onde ela sai: a união dos
módulos ligados nos conectores do tenant, em vez de um campo do perfil.

**Isso é de propósito.** A barra lateral agrupada por conector é o ADR-0039, de outra fatia. Mantendo a forma da
resposta, esta change não toca em nenhuma tela de navegação — e a fatia seguinte muda a tela sem mexer no dado de novo.

**O padrão:** um tenant sem conector nenhum tem `["Fiscal"]`, como hoje. Um conector sem módulo ligado não contribui.
É decidido, e não acidente da união vazia: o tenant que a M3 atravessa sem linha nenhuma — sem adapter de saída, e por
isso sem conector — também responde `["Fiscal"]`, e não a lista vazia. A lista vazia deixaria a barra lateral só com o
Agendamento, escondendo o Fiscal e as notas já gravadas; o padrão mantém o que esse tenant via antes desta change.

**A tensão com "módulo é apresentação".** O módulo continua sem comportamento (ADR-0037 §1), mas a integração passa a
decidir o envio, e ela só fica ligada com o módulo. Desligar o Fiscal num conector, portanto, faz o envio de notas ser
rejeitado. O delta de `module-navigation` registra isso: a leitura da API não muda; o envio muda, pela integração.

### D8. A migração não fala com o cofre, não copia a referência, e é de ida só

O nome do segredo muda, e o cofre é endereçado por nome. A migração é EF Core, roda na subida e vive em Infrastructure:
para copiar o segredo ela teria que falar com o Key Vault, pondo uma dependência de rede dentro de uma migração de
schema e fazendo a subida do host depender do cofre estar no ar.

**A referência também não é copiada.** Copiada, ela apontaria para o nome antigo (`fh-{tenant}--outbound--sandbox--…`),
que continua no prefixo do tenant e passa na checagem de dono. Num cofre que persiste, o segredo apareceria
**configurado** sob um nome que nenhuma gravação nova produz. A conta nasce sem `ClientSecretRef`, e o segredo aparece
como não configurado até ser digitado de novo. Em desenvolvimento o cofre é um emulador em memória, que já perde os
segredos a cada reinício; em produção não há cliente.

**O `tokenUrl` é copiado quando a seção o tem**, e fica vazio quando não tem — e então a conta o mostra como não
configurado (D11). O Admin já precisa voltar à conta para digitar o segredo, e preenche o endpoint na mesma visita.

**A chave `v2` vai fixa no SQL.** A URL base da seção vira a base da versão `v2`. A migração não consulta a declaração do
adapter: o Infrastructure não referencia os Adapters, e uma migração não tem DI. É a única coisa da plataforma que a
migração sabe, e está escrita aqui por isso.

**O caminho da integração fiscal nasce nulo**, à espera do host (D10): ele mora na configuração do host, que uma
migração não lê.

**O Fiscal é ligado sempre.** Mesmo o tenant cuja lista de módulos não tinha o Fiscal — o que hoje é aceito — sai da
migração com ele, porque é a integração de notas fiscais que mantém o envio. O `/info` desse tenant passa a trazer o
Fiscal; a spec registra o caso.

**A irreversibilidade é por migração, e não da change (D13).** Só a conversão do ambiente (M1) não tem volta: o `Down`
dela lança, com uma mensagem que diz isso. Uma migração EF não lê `IConfiguration`, e depois do `Up` nenhuma linha
guarda o ambiente — não há de onde reconstruir a seção descartada (ADR-0036). A conta e o conector (M2) e os módulos e
as integrações (M3) têm `Down` de verdade, porque tudo o que eles desmontam continua no banco — o da M2 com condição: a
forma nova não pode ter sido usada além do que o perfil achatado comporta (D13).

### D9. O envio lê o caminho da integração, no próprio conector

O `AvalaraOutboundSettings` passa a achar o **próprio** conector na coleção do tenant, pelo nome do adapter — a mesma
comparação exata que o `PlatformEstablishmentResolver` já faz —, e dentro dele a integração de notas fiscais: a base da
versão que ela usa e o caminho dela. O envio e a consulta de status montam a URL com esses dois.

**Não entra seletor de despachante.** A esteira continua com um despachante do DI. Com um conector por adapter, "resolver
o conector pela integração" é operação nula; a indireção entra na fatia do segundo conector, quando houver dois adapters
para escolher.

**O `Avalara:DocumentsPath` sai do envio.** O envio, a consulta de status e o motivo do 404 deixam de lê-lo; o motivo do
404 passa a apontar as duas partes da URL na tela de conectores (a base da versão e o caminho da integração). O único
leitor que sobra é o preenchimento de uma vez (D10). Os caminhos da listagem (`Avalara:CompaniesPath` e
`Avalara:TaxpayersPath`) continuam opções do host: a listagem não é uma integração.

### D10. O host preenche, uma vez, o caminho que a migração não sabe

Na subida, depois do `MigrateAsync`, o host preenche o endpoint **nulo** da integração de notas fiscais com o valor de
`Avalara:DocumentsPath` — o mesmo que o envio usa antes desta mudança, inclusive o padrão do código. Roda em qualquer
ambiente, e não depende do seed de desenvolvimento, que só semeia tabela vazia.

- **Só o nulo.** Um caminho preenchido não é sobrescrito, e o texto vazio que a tela grava — a integração inativa que
  alguém escolheu — não é preenchido.
- **Uma vez.** Preenchido, o caminho é da tela.

Isso deixa a migração sem ler configuração, e a instância que já enviava continua enviando para o mesmo lugar depois da
migração.

### D11. O endpoint de token é obrigatório na conta

Não existe mais derivação do endpoint de token a partir da URL base de um conector. Isso resolve três coisas de uma vez:

- **a chave do cache fica estável:** o endpoint é da conta, e dois conectores — ou duas integrações em versões de base
  diferentes — não produzem dois endpoints, nem dois tokens;
- **o teste de credencial da conta tem endereço**, sem precisar escolher um conector;
- **o mock** atende o token na raiz (`/oauth/token`), que não é a base de versão nenhuma.

O `Avalara:TokenPath` deixa de ser lido, e sai: configuração que nada lê é configuração em que alguém acredita.

**Obrigatório para enviar e para testar, e não para gravar.** Sem o `tokenUrl`, o envio e o teste são rejeitados como
configuração do conector, nomeando o campo na conta — como já acontece com o `clientId`. A gravação não bloqueia por
ele, pelo mesmo argumento da integração sem caminho; confere só a regra de `https` quando ele vem.

### D12. O harness de migração roda contra SQL Server de verdade

A conversão do perfil é SQL Server (funções JSON do T-SQL), e o SQLite dos testes não a executa. O harness é um teste de
integração contra o SQL Server do `docker compose`, no padrão do `KeyVaultEmulatorFact`: opt-in por variável de
ambiente, pulado sem ela, e sem tocar a rede no CI.

Ele aplica as migrações até a anterior às desta change, grava perfis na forma antiga, aplica o resto e confere a forma
nova. Também desfaz: prova que desfazer a M1 falha e deixa o banco como estava, e que a M2 e a M3 vão e voltam — a
forma depois do `Down` é a de antes do `Up` —, e que a M2 recusa voltar quando a volta perderia dado (D13).

**Pulado não é verde.** Um teste pulado não prova a migração. Por isso as tarefas de migração só recebem `[x]` com a
saída de uma execução do harness com o SQL Server no ar.

### D13. Três migrações, e não cinco

O teste do modelo contra o snapshot obriga cada grupo que muda a forma persistida a trazer a sua migração. O número de
migrações é, portanto, o número de grupos que mudam o schema, e a consolidação se faz juntando o que mudava o schema em
grupos separados sem precisar:

- **M1 (grupo 3), o ambiente sai:** some a coluna `Environment`, e as settings de saída são achatadas. Fica separada
  porque o ambiente é a dimensão que atravessa tudo e sai primeiro; juntá-la à M2 faria o grupo 3 terminar com o domínio
  achatado sobre um banco aninhado, e exigiria um leitor das duas formas, de jogar fora. **É irreversível:** o `Down`
  lança, porque a seção do outro ambiente foi descartada e não há de onde reconstruí-la. **E por isso tem trava:** um
  perfil com seção por ambiente cujo `Environment` não nomeia nenhuma delas (em branco, ou um valor legado) faz o `Up`
  falhar antes de qualquer escrita e antes do `DropColumn`. Numa migração sem volta, converter no escuro — escolher uma
  seção, ou esvaziar o perfil, que era o que a primeira versão fazia — destrói dado em silêncio; parar devolve a decisão a
  quem conhece o cliente, enquanto a coluna ainda está lá. A seção é achada sem distinguir maiúscula, por `OPENJSON`, e não
  por caminho JSON, que distingue.
- **M2 (grupo 4), a conta e o conector viram linhas, juntos:** a conta sem o conector deixaria as settings do produto
  num lugar provisório, que a migração seguinte desmontaria. Era a 3.2 e a 4.2. A regra das travas dela vale nos dois
  sentidos: **nada é removido que não tenha sido lido** (revisão de 2026-10-13).
  - **O `Up` tem trava**, como a M1. Falha antes de qualquer escrita com: um perfil com settings de saída e sem
    `OutboundAdapter`, que não têm para que conta ir; um `OutboundAdapter` que não é exatamente `Avalara`; um
    `clientSecret` em texto; uma chave da credencial ou da base grafada em outra caixa (`"BaseUrl"`), porque a extração
    por caminho JSON distingue caixa e não a leria; e um `baseUrls` já nas settings, que a conversão sobrescreveria. A exclusão do resto das settings compara em binário, igual à extração: o que não foi
    lido não sai. A junção da conta com o perfil é por tenant **e** plataforma; com o índice único de
    `ConnectorProfiles` em `TenantId` (desde a `AddConnectorProfiles`), há um perfil por tenant, e a plataforma na junção
    só fecha o caso de essa cardinalidade mudar.
  - **O nome do adapter: trava, e não normalização.** A conta e o conector recebem o `OutboundAdapter`, e o adapter os
    acha por comparação exata (D9). Antes desta change o envio não lia esse campo, e a gravação não o validava: um
    `avalara` gravado pela API, ou o `Mock` que a tela antiga oferecia, enviava pela Avalara. Copiado como está, o nome
    ficaria invisível; normalizado, a migração decidiria em silêncio o que ele queria dizer, e um acerto desses não deixa
    rastro quando é erro. A M2 recusa o que não é exatamente `Avalara`. O texto fixo está certo numa migração: ela
    registra o mundo como era na data dela, e nesta data só existe um despachante. É a grafia do adapter, a que o seed e a
    tela gravam, e a dos dois tenants do banco de dev; o harness prova que o nome migrado é achado pelas comparações
    exatas do adapter de verdade.
  - **O `clientSecret` em texto não é dado a preservar: é segredo vazado.** A conversão o descartaria sem lê-lo, e
    apagá-lo em silêncio destruiria a evidência de que houve vazamento — ninguém rotacionaria nada. Vazio não conta. Ao
    esbarrar na trava: rotacione o segredo na plataforma; só depois tire o `clientSecret` das settings de saída, cujo
    valor a rotação invalidou; migre; e grave o segredo novo na conta de plataforma pela tela, que o manda para o cofre.
    O segredo novo vai direto para a conta porque a M2 não copia referência nenhuma (D8): gravá-lo antes, pela tela
    antiga, não o levaria para a conta.
  - **Cada migração roda na própria transação.** A trava de uma desfaz só ela: numa subida que aplica a M1 e a M2, a M2
    que trava deixa a M1 aplicada — o ambiente já convertido, sem volta, e o host antigo sem a coluna que lê. O acerto
    que a trava pede é feito por SQL sobre essa forma (settings achatadas, sem ambiente), e a subida seguinte aplica a
    M2. O que isso custa não é dado — as travas já impedem a perda —, é o trabalho de remediação, numa falha que nos
    bancos que vão rodar a change (o de dev, limpo, e a instalação nova de um cliente, vazia) tende a não acontecer.
    - **Alternativa considerada:** repetir as condições da M2 no `Up` da M1, para nada sem volta acontecer antes de
      todas passarem. Descartada porque seria **uma segunda implementação do mesmo predicado sobre outra forma de
      dado**: as travas da M2 leem a raiz achatada, e dentro da M1 teriam que ler a seção escolhida mais as chaves da
      raiz que sobrevivem — o resultado do merge, antes do merge. A cópia que divergisse para o lado de passar devolveria
      o cenário de agora, com código a mais; e a M1 passaria a afirmar coisas sobre `baseUrls`, `clientSecret` e grafia
      de adapter, que não são assunto dela.
    - **Cada trava tem a remediação provada.** Um teste parametrizado do harness passa pelas cinco travas do `Up`: a
      trava dispara, o banco fica na forma da M1, o acerto é feito por SQL sobre a forma achatada, e a subida seguinte
      aplica a M2. Uma trava nova entra nele, com o acerto dela.
    - **O pré-voo:** `docs/preflight/platform-account-and-connectors.sql`, só leitura, roda contra o banco do cliente
      **antes** de começar o deploy — antes de parar o host antigo, quando a tela dele ainda serve para acertar o dado. Ele
      escreve as mesmas condições (as cinco do `Up` da M2, mais a da M1) como `SELECT` sobre a forma de antes da change,
      e devolve uma linha por perfil e por trava, sem valor nenhum das settings. Pode divergir das migrações sem quebrar
      nada, porque não decide nada: se divergir para menos, a trava da migração ainda segura. Um teste do harness o roda
      sobre a forma de antes, com um perfil por trava. Uma trava nova entra nele também.
  - **Tem `Down`, com condição.** Remonta as settings achatadas e o `OutboundAdapter` a partir da conta e do conector —
    inclusive a referência de segredo que a conta tiver ganhado depois da M2, que é do prefixo do tenant e a leitura
    achatada aceita. A condição é a forma nova não ter sido usada além do que o perfil achatado comporta, e ela está no
    `Down`, que falha antes de qualquer escrita com: mais de uma conta no tenant; mais de um conector na conta; um
    conector de adapter diferente da plataforma; uma base de versão que não a `v2` (a `v1`, que o grupo 6 passa a
    gravar, é o caso esperado; a base vazia não conta, porque não é dado); ou uma chave da forma achatada (`baseUrl`,
    `clientId`, `clientSecretRef`, `tokenUrl`) nas settings do conector, que o `Down` sobrescreveria. Um `Down` que
    trunca em silêncio é pior que um que lança: a mensagem manda restaurar um backup.
- **M3 (grupo 6), módulos e integrações viram linhas, e a coluna `Modules` sai:** junto com a derivação do `/info`, que
  é o único leitor da coluna. Eram a 5.3, a 5.8 e a 6.3. As travas seguem a regra da M2 — nada é removido que não tenha
  sido lido, e nada é adivinhado —, e cada uma tem a remediação provada no harness, com o banco ficando na forma da M2.
  - **O `Up` tem trava.** Falha antes de qualquer escrita com: uma lista de módulos que não é uma lista de `Fiscal`,
    `Contabil` e `Inventario`, nessa grafia; um tenant com mais de um conector (a conversão liga os módulos num conector
    só, e não escolhe qual); um conector de adapter que não é exatamente `Avalara` (a integração de notas que ela liga é
    a que esse adapter declara); e um tenant sem conector com módulos além do Fiscal (sem conector, eles não têm onde
    ficar). As duas primeiras que a forma de antes já mostra estão no pré-voo; as outras não têm como existir antes,
    porque os conectores nascem na M2, um por tenant, e sempre `Avalara`.
  - **Tem `Down`, com condição.** Devolve a coluna `Modules` ao tenant a partir dos módulos do conector, na ordem da
    barra lateral; o Fiscal que a M3 ligou volta junto, e o tenant sem módulo ligado volta nulo, que é o padrão. A
    condição é a forma nova não ter sido usada além do que a lista do tenant comporta: uma integração ligada além da de
    notas, um tamanho de lote gravado, ou mais de um conector no tenant fazem o `Down` falhar antes de qualquer escrita.
  - **O que o `Down` perde: o caminho da integração de notas.** Ele devolve a lista de módulos, mas o caminho não volta
    — antes da M3 o envio lê o `Avalara:DocumentsPath`. Isso é aceito porque a alternativa, pôr o caminho na trava, faria
    o `Down` recusar em todo banco real: o host preenche o nulo na subida (D10), e a migração não lê configuração para
    distinguir o caminho que o host preencheu do que o Admin editou. O dano é limitado ao caso em que o Admin editou o
    caminho pela tela. A mitigação: `docs/preflight/platform-account-and-connectors-before-undoing-m3.sql`, só leitura,
    roda sobre a forma da M3 e devolve o tenant, a chave e o caminho de cada integração — antes de desfazer a M3, guarde
    essa saída num arquivo. Um teste do harness o roda sobre a forma da M3.

Desfazer a change inteira roda o `Down` da M3 e o da M2, e para no da M1: o banco fica na forma achatada, sem ambiente,
que é uma forma válida — a da M1 aplicada. Isso vale enquanto a forma nova não tiver sido usada além do que a de antes
comporta; depois disso, o `Down` da M2 recusa, e a volta é por backup.

**Por que três migrações não contrariam o ADR-0037.** O "pagar duas vezes" de lá é sobre partir a mudança de forma em
fatias entregues em momentos diferentes, cada uma convertendo a config viva de novo; não é sobre contar migrações dentro
de uma fatia. As três desta change chegam juntas, na mesma entrega. O número delas é consequência do teste que exige
paridade entre o modelo e as migrações: cada grupo que muda a forma persistida precisa terminar verde, e só termina
verde com a sua migração. Dava para chegar a duas, trazendo módulos e integrações para o grupo 4; não fiz, para o grupo
da conta continuar revisável.

### D14. A seção do ambiente que não é o da instância é descartada

**Alternativa descartada:** guardar a outra seção num campo morto "por segurança". Configuração que nada lê é
configuração em que alguém, meses depois, acredita.

### D15. O que esta change não toca, de propósito

Para o review ficar barato:

- o prefixo `fh-{tenant}--` e a regra de segmento do `SecretNames`;
- a checagem de dono do segredo e a recusa de `*Ref` vindo da requisição;
- a condição de acesso do cofre de produção (`fh-`);
- a máscara da tela e a regra de "salvar sem digitar não toca o segredo";
- a `IntegrationStatus`, a esteira, a DLQ e o mecanismo do poll — do poll, muda só o caminho que a consulta usa (D9);
- o despachante único do DI (D9);
- as settings de entrada e de chamados, que nunca tiveram ambiente;
- o bloco Integrações da barra lateral.

### D16. A tela: o que a forma do dado obriga, e o que veio do canvas

A tela de Configurações muda por força da forma do dado. **O obrigatório** é o que deixa a tela gravar o dado novo:

- sem seletor de ambiente e sem os dois blocos;
- o bloco da conta: `clientId`, Client Secret, `tokenUrl` e o botão de testar. O `clientId` e o `tokenUrl` vazios têm a
  marca de pendência, com o efeito ("sem o endpoint de token, o conector não despacha"), como o segredo não configurado e
  a integração sem caminho: o `tokenUrl` é o campo que a migração deixa vazio em todo tenant (D8), e sem a marca quem
  migra cai na tela com o conector parado e sem saber por quê. Com a credencial gravada incompleta, o botão de testar
  fica desabilitado, dizendo o que falta;
- os campos do conector: as URLs base por versão, com o resto das settings preservado;
- os módulos marcados por conector, no lugar da aba do nível do cliente;
- as integrações ligadas, com o caminho e, onde houver, o lote, e a pendência das que estão sem caminho.

Cada parte obrigatória entra no mesmo grupo que a forma de dado que ela segue: uma tela que manda a forma velha a um
servidor que já lê a nova fica verde e quebrada.

**Largura:** uma URL ou um GUID cortado no meio não se lê, e o Client ID é o que se lê ao diagnosticar por que um tenant
não acha a conta. Os campos assim ocupam a linha inteira, os grids empilham abaixo de 900px, e a linha da integração
também: o rótulo em cima, o caminho e o lote embaixo, na largura inteira. O painel do conector (grupo 7) só põe a lista
ao lado das abas a partir de 1200px, quando a página já está na largura máxima: com a barra lateral de 232px, entre 900
e 1000px as abas e a URL base não cabiam ao lado da lista.

**Os exemplos seguem o ambiente da instância.** O exemplo do endpoint de token e das URLs base é o do ambiente que o
`/info` diz, e não há exemplo antes de ele responder — a mesma regra do selo (D6). Com o exemplo de produção fixo, quem
configurava sandbox via produção como modelo, e um Admin apressado autenticaria contra produção numa instância que se
anuncia Sandbox. As URLs dos dois ambientes ficam escritas por inteiro, e uma não se deriva da outra.

**O que veio do desenho do canvas** ("FiscosysHub — as telas do produto", do ADR-0039) e não é obrigado pela forma do
dado: a lista de conectores, o painel do conector com três abas (Conexão, Módulos e integrações, Endpoints) e o módulo
que o adapter não declara mostrado apagado, com o motivo. Ficam num grupo próprio, no fim. Se ele sair desta fatia, o
cenário "Módulo que o conector não atende", de `module-navigation`, sai junto.

### D17. O nome gravado é o declarado

**O defeito.** O grupo 4 gravava o nome da plataforma e o do adapter como vinham, com `Trim()`, e achava o gravado sem
distinguir caixa; o store casava do mesmo jeito e renomeava a linha no lugar. As leituras — a conta e o conector do
envio, a listagem de estabelecimentos, o teste de credencial — comparam o nome exato (D9). Um `PUT` com `avalara`
renomeava a conta e a deixava invisível, sem erro e sem log. É dívida desta change, do grupo 4, que ainda não foi
entregue: conserta-se dentro dela, e não abre change própria.

**A decisão.** A gravação resolve a plataforma de cada conta e o adapter de cada conector contra os nomes **declarados**
pelas implementações registradas, casando sem distinguir caixa, e grava a forma declarada. As leituras continuam
exatas: canonizar na gravação é o que mantém as duas pontas iguais. As travas da M2 limpam o passado; esta regra fecha a
fonte.

**A declaração carrega a plataforma além do adapter.** É o ADR-0037 ("o adapter declara, o perfil habilita"), e é a
primeira vez que plataforma e adapter deixam de ser a mesma string: hoje coincidem porque a Avalara é uma só, mas com o
AvaTax a plataforma é `Avalara` e os adapters são dois.

**O nome que nenhuma implementação declara é recusado** com 400, nomeando o pedido e os declarados — o padrão das
travas: recusar dizendo o que valeria, e não adivinhar. Por isso o "Mock" sai do seletor da tela: uma opção que o
servidor recusa é um botão que sempre falha. O seed não depende dele (a conta dele é `Avalara`, apontada para o mock
local), e o `DispatchToMockTests` usa dublê, e não adapter registrado.

**Ressalva, registrada e não resolvida:** a gravação passa a depender de quais adapters estão registrados no DI. Hoje o
host registra todos, e não há caso. Se um dia existir build sem um adapter, isso é decisão de formato de deploy, e não
coisa para a gravação disfarçar; a mensagem "nenhuma implementação declara esse nome" continua honesta nesse cenário.

## Riscos

- **A largura.** Nove arquivos de spec e código em quatro camadas, mais a sonda e o dashboard, para quase nenhum
  comportamento novo. A mitigação é a ordem das fatias: o ambiente sai primeiro (é o que mais atravessa), a conta e o
  conector depois, as regras da coleção depois, módulos e integrações por último. Cada fatia termina com o build verde.
- **O seed e os testes.** Cerca de 25 arquivos de teste atribuem `Environment`, inclusive os do D365 e os de integração,
  e vários montam settings com `sandbox`/`production`. É mecânico, e é onde um descuido passa.
- **A conversão do ambiente (M1) é de ida só.** O custo é recriar a base de desenvolvimento para voltar a antes dela. A
  M2 e a M3 têm volta, provada pelo harness com ida e volta — a da M2 com a condição do D13: só enquanto a forma nova não
  tiver sido usada além do que o perfil achatado comporta.
- **O harness é opt-in.** Pulado, ele não prova nada (D12). A regra do `[x]` com a saída da execução é o que segura.
- **O mapa de versões é blob validado na gravação.** O que entra por fora dela (D4) tem o dano limitado pela leitura.
- **A marca nula do caminho (D5, D10).** Um nulo gravado por SQL também seria preenchido pelo host na subida seguinte. É
  aceitável: o efeito é o comportamento de antes desta mudança.
- **O teste de credencial é da conta, mas se acha pelo adapter.** `IConnectorCredentialTest` declara um nome de adapter,
  e o serviço o compara com a plataforma da conta. Funciona porque a Avalara tem um adapter só e os dois nomes coincidem.
  Com o AvaTax, a plataforma `Avalara` terá dois adapters, e a comparação deixa de fazer sentido: o teste passa a
  precisar dizer de que **plataforma** é, e não de que adapter. A porta da declaração (5.1) já carrega os dois nomes, e a
  correção é pequena — mas precisa ser feita antes do segundo adapter, e não depois de o teste começar a não achar nada.
  Não entra nesta fatia: mexer no teste de credencial aqui seria alargar o escopo sem necessidade.
