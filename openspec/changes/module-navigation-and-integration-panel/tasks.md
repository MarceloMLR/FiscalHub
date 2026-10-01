A ordem vai do dado para a tela, uma fatia vertical por grupo:

- **Grupos 1 e 2, os módulos.** Primeiro o perfil e o `/info`, depois a barra lateral que os lê.
- **Grupo 3, o segredo mascarado e os textos.** Com a primeira suíte de testes do front.
- **Grupos 4 e 5, o painel e o rebobinamento.**
- **Grupos 6 a 8, o teste de credencial.** A porta e o freio, depois a Avalara e o D365, e por fim a tela.
- **Grupo 9, a documentação.**
- **Grupo 10, a prova manual do critério de saída.**

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. Os grupos que mexem no dashboard
terminam com o `npm test` e o `npm run build`.

## 1. Os módulos no perfil (D2, `module-navigation`)

- [x] 1.1 Teste primeiro (Application):
  - **o padrão:** sem módulos gravados é `["Fiscal"]`;
  - **a normalização:** o valor repetido conta uma vez, e a ordem é Fiscal, Contábil, Inventário;
  - **a recusa:**
    - um valor desconhecido é recusado, com a mensagem que o nomeia e lista os aceitos;
    - a lista vazia é recusada;
  - **na `ConnectorProfileServiceTests`:**
    - a gravação sem `modules` mantém os gravados;
    - a gravação com uma lista inválida responde `Invalid`, sem tocar o cofre nem o perfil;
    - a leitura devolve os `modules`
- [x] 1.2 Application:
  - o tipo `TenantModules`, com os aceitos, o padrão, a normalização e a validação;
  - o `TenantConnectorProfile.Modules`;
  - o `ConnectorProfileRequest.Modules`, anulável: nulo mantém o gravado;
  - o `ConnectorProfileView.Modules`;
  - a validação na `ConnectorProfileService`, antes de qualquer escrita
- [x] 1.3 Infrastructure:
  - a coluna `Modules` (`nvarchar(200)`, anulável, JSON) na `ConnectorProfileRow`;
  - o mapeamento no `SqlConnectorProfileStore`;
  - a migração `AddConnectorProfileModules`, sem preencher nada;
  - o teste do store: ida e volta, e a coluna nula lida como `["Fiscal"]`
- [x] 1.4 Host:
  - o `/info` responde `modules`, para qualquer papel. Sem perfil, `["Fiscal"]`;
  - o `GET /connector` devolve os `modules`, e o `PUT /connector` os aceita
- [x] 1.5 `dotnet build` com 0 warnings e `dotnet test` verde

## 2. A navegação por módulo (D1, `module-navigation`)

- [x] 2.1 `types.ts` e `client.ts`: `modules` no `/info` e no perfil
- [x] 2.2 `App.tsx`:
  - o bloco "Integrações" no lugar de "Operação", com Fiscal, Contábil, Inventário e Agendamento. Os três primeiros
    aparecem pelos `modules` do `/info`, e o Agendamento sempre;
  - o Fiscal é a view `documents` de hoje, e o Agendamento, a `integrations`;
  - a tela de entrada:
    - o Fiscal;
    - sem ele, o primeiro módulo do tenant;
    - sem módulo nenhum, o Agendamento.

    Enquanto o `/info` carrega, a tela é o Fiscal;
  - o título e o subtítulo de cada view
- [x] 2.3 O painel reservado de Contábil e Inventário:
  - diz que o módulo é reservado e ainda não tem integração;
  - não tem nenhum documento, card nem contagem;
  - o comentário no componente aponta o D1: é outro domínio, e cada um é uma fatia própria
- [x] 2.4 Em Configurações, a aba "Módulos":
  - uma caixa de marcar por módulo, marcada conforme o perfil;
  - gravada pelo mesmo "Salvar", com o resto do perfil;
  - o salvar invalida o `info`, para a barra mudar sem recarregar a página
- [x] 2.5 `npm run build` verde

## 3. O segredo mascarado e os textos de Configurações (D3, D4, D5, `connector-secret-references`)

- [x] 3.1 Teste primeiro (servidor, `ConnectorProfileServiceTests`):
  - **a recusa:**
    - `••••••••`, `********` e `* * * *` num campo de escrita são recusados com `Invalid`;
    - a mensagem nomeia o campo e não repete o valor;
    - o cofre não é tocado, e a referência gravada fica;
  - **o aceito:** `ab*cd` vai para o cofre;
  - **o que já existe:** o `Absent_write_field_keeps_the_stored_reference_without_touching_the_vault` continua verde,
    sem mudança
- [x] 3.2 `ConnectorProfileService`: a recusa do valor feito só de caracteres de máscara, na validação
- [x] 3.3 Dashboard, a suíte de testes:
  - o `vitest` como dependência de desenvolvimento, e o script `npm test`;
  - a montagem do payload do perfil sai da `ConnectorsPage` para um módulo puro (`connectorPayload.ts`). A regra do
    `withTyped` vai junto e não muda;
  - no mesmo módulo, a comparação que diz se há edição pendente
- [x] 3.4 Teste do front (`connectorPayload.test.ts`):
  - **sem digitar:** com um perfil carregado, com o Client Secret configurado e nada digitado, o payload não tem
    `clientSecret` nem nenhum caractere de máscara, na entrada e nas duas seções da saída;
  - **só a URL base mudada:** o mesmo;
  - **com um segredo digitado:** o payload leva esse valor, e só ele;
  - **a edição pendente:**
    - não há logo depois de carregar;
    - há depois de mudar um campo, ou de digitar um segredo;
    - some depois de salvar
- [x] 3.5 `ConnectorsPage`:
  - o campo de um segredo configurado tem o placeholder `••••••••` e "configurado em <data>" embaixo, com o valor
    vazio;
  - o não configurado continua sem máscara, com "não configurado";
  - a mensagem de sucesso é "Configurações salvas com sucesso";
  - o texto sob o interruptor sai
- [x] 3.6 `dotnet build` com 0 warnings, `dotnet test`, `npm test` e `npm run build` verdes

## 4. O painel da integração automática (D6, `automatic-integration-panel`)

- [x] 4.1 Teste primeiro (Application), a leitura do painel:
  - **o adapter que não varre:** responde "não varre";
  - **sem cursor:** responde o cursor nulo;
  - **com cursor:** devolve a última verificação, as falhas, o erro, a marca e o throttling;
  - **o `startFrom`:** vem do perfil, pelo `ChangeFeedPollSettings`, com ou sem cursor. Sem `startFrom` gravado, vem
    nulo;
  - **o tenant:** é sempre o do contexto
- [x] 4.2 A leitura na Application (`Inbound`), e o `GET /connector/automatic` no host:
  - só Admin;
  - 404 quando não varre;
  - 200 com `cursor: null` ou com os campos, e sempre com o `startFrom`
- [x] 4.3 Conferir, lendo o código, o que entra no `RecordFailureAsync` do coletor: as mensagens do D365, do Entra ID, do
  cliente OData e das settings. Nenhuma leva token nem segredo. Anotar o que foi lido nesta tarefa

  **Lido (2026-09-29).** O coletor grava o `ex.Message` de qualquer falha. As origens:
  - **o `D365ODataClient`:** "OData do F&O respondeu <status>: <até 300 caracteres do corpo>", e as duas mensagens de
    throttling. O cabeçalho `Authorization` é montado na requisição e nunca entra em mensagem;
  - **o `D365ChangeFeed`:** "sem perfil de conector", "resposta vazia" e "SysModifiedDateTime inválido", com o RecId;
  - **o `D365InboundSettings`:** as mensagens nossas, que citam o campo e não repetem o valor. A do segredo em claro
    diz só que ele não é aceito;
  - **o Azure.Identity:** a `AuthenticationFailedException`, com o texto do AADSTS. Ele traz o código, o app e o trace
    id, e não o segredo;
  - **o `ClientCredentialsD365TokenProvider`:** as nossas, sobre a referência e o cofre, sem o valor.
- [x] 4.4 Dashboard, o painel sob o interruptor, na aba do ERP:
  - **quando aparece:** quando o `automaticIntegration` do `/info` é verdadeiro;
  - **a atualização:** a cada 15 segundos;
  - **o que mostra:**
    - a última verificação, com "há N s/min", no fuso do navegador;
    - as falhas seguidas;
    - o último erro, só com falha;
    - a marca;
    - o throttling, só no futuro;
  - **os estados vazios:** "o coletor ainda não passou por este tenant" e "a marca ainda não nasceu";
  - **o `startFrom`, só leitura:**
    - **sem marca:** o painel diz de onde a primeira passada vai começar. Com o `startFrom`, a data dele. Sem, "no
      instante em que rodar, e o que mudou antes fica de fora";
    - **com marca:** o `startFrom` aparece, com "só vale quando a marca não existe";
    - **nenhum controle de edição:** definir o ponto de partida não é rebobinar (D6)
- [x] 4.5 `dotnet build` com 0 warnings, `dotnet test` e `npm run build` verdes

## 5. O rebobinamento pela tela (D7, D8, `automatic-integration-panel`, `change-feed-polling`)

- [x] 5.1 Teste primeiro (Infrastructure, SQLite, junto do `ChangeFeedFencingSqlTests`), o `TryRewindWatermarkAsync`:
  - **com o lease do dono e um alvo abaixo da marca:** grava e devolve verdadeiro;
  - **um alvo igual ou acima:** não grava;
  - **o lease de outro dono, ou vencido:** não grava;
  - **sem linha:** não grava e não cria;
  - **os outros campos:** a última verificação, as falhas, o erro e o throttling ficam como estavam
- [x] 5.2 O `IChangeFeedCursorStore.TryRewindWatermarkAsync`, e a implementação no `SqlChangeFeedCursorStore`: uma
  instrução, com `WatermarkTicks > @alvo` e o `EXISTS` do lease
- [x] 5.3 Teste primeiro (Application), o serviço de rebobinamento:
  - **o adapter que não varre:** "não varre";
  - **o lease ocupado pelo coletor:** "ocupado", e nada gravado;
  - **sem cursor, ou sem marca:** "sem marca", e nenhum cursor criado;
  - **o alvo que não é anterior à marca, ou no futuro:** "inválido";
  - **o sucesso:** grava;
  - **o lease:** é liberado no sucesso e na falha;
  - **o log:** uma linha com o usuário, o tenant, a marca de antes e a nova;
  - **o tenant:** é o do contexto

  **Onde ficou o log (2026-09-29).** A Application não depende de logging. O resultado do serviço leva a marca de antes e
  a nova, e o teste o confere. A linha é escrita pelo endpoint (5.6), com o usuário do token, como o resumo da passada do
  coletor é escrito pelo worker. O D7 do design registra isso
- [x] 5.4 O serviço na Application (`Inbound`), com o dono `rewind:{guid}` e o prazo de 30 segundos
- [x] 5.5 Teste da passagem pela regra (Application), com o `ChangeFeedPoller`, o `ChangeFeedPublicationLog` real e o
  serviço:
  1. uma passada enfileira A, assentado;
  2. o serviço rebobina para antes do carimbo de A;
  3. a passada seguinte enfileira A de novo, com 0 suprimidas.

  O rebobinamento é feito pelo serviço, e não por edição direta no store
- [x] 5.6 Host, o `POST /connector/automatic/rewind`:
  - só Admin;
  - 200 com a marca nova;
  - 400 com o motivo (só para trás, ou no futuro);
  - 404 quando não varre;
  - 409 com o lease ocupado, ou sem marca
- [x] 5.7 Dashboard, no painel:
  - **a entrada:** o "Voltar a marca", com data e hora (`datetime-local`), o máximo na marca atual, e o envio do ISO
    com o fuso;
  - **a confirmação:** o `Modal`, com o texto exato do D8. O texto mora num lugar só, com o comentário que aponta o
    `AlreadyProcessedAsync` e o `FetchAsync`;
  - **cancelar:** não envia nada;
  - **o sucesso:** atualiza o painel;
  - **o 409 e o 400:** o motivo aparece no painel
- [x] 5.8 `dotnet build` com 0 warnings, `dotnet test` e `npm run build` verdes

## 6. O teste de credencial: a porta, o serviço e o freio (D9, D12, `connector-credential-test`)

- [x] 6.1 Teste primeiro (Application), o serviço de teste:
  - **o adapter sem teste:** "sem teste";
  - **o `Incomplete`:** não entra no freio;
  - **o `Refused`:**
    - entra no freio por 5 minutos, por tenant e por adapter, e, na saída, também por ambiente;
    - dentro do intervalo, o teste devolve o motivo lembrado e o `retryAt`, sem chamar o adapter;
    - depois do intervalo, chama de novo;
  - **o `Unavailable` e o sucesso:** não entram no freio;
  - **salvar o perfil:** esquece o freio do tenant, pelo `IConnectorProfileObserver`;
  - **o isolamento:** o outro tenant e o outro adapter não são afetados;
  - **a resposta:** tem só `worked`, `reason` e `retryAt`
- [x] 6.2 Application (`Connectors`):
  - a porta `IConnectorCredentialTest`;
  - o `CredentialTestOutcome`, com os vereditos;
  - o serviço, com o freio em memória, o intervalo nas opções (padrão de 5 minutos) e o registro como observador do
    perfil
- [x] 6.3 Host, o `POST /connector/test`:
  - só Admin;
  - `{ side, environment? }`;
  - 200 com o resultado;
  - 400 para um adapter sem teste, ou um lado ou ambiente inválido
- [x] 6.4 `dotnet build` com 0 warnings e `dotnet test` verde

## 7. O teste de credencial nos adapters (D10, D11, `connector-credential-test`, `avalara-tenant-authentication`)

- [x] 7.1 Teste primeiro (Avalara), o `ProbeAsync`:
  - **o cache:** pede ao endpoint mesmo com um token válido em cache;
  - **a recusa lembrada:** pede ao endpoint mesmo com uma recusa lembrada;
  - **o sucesso:**
    - esquece a recusa do tenant, e o `GetTokenAsync` seguinte do envio não falha com ela;
    - guarda o token novo;
  - **a recusa:** fica lembrada para o envio;
  - **o outro tenant:** continua com a recusa lembrada dele;
  - **o motivo:** não contém o segredo, mesmo quando o corpo da recusa o repete

  **Nota (2026-09-29):** a ordem não foi teste primeiro. Os testes do `ProbeAsync` foram escritos logo depois da
  implementação (7.2), na mesma sessão. Os do cache e da recusa lembrada falhariam se o teste passasse pelo
  `GetTokenAsync`
- [x] 7.2 O `IAvalaraTokenProvider.ProbeAsync` e a implementação no `AvalaraTokenProvider`:
  - o mesmo `ResolveAsync` e o mesmo `FetchAsync`, sob a mesma trava;
  - sem o `TryCached`;
  - no sucesso, o `Forget` do tenant, e o token novo no cache
- [x] 7.3 Teste primeiro (Avalara), o teste de credencial:
  - **os desfechos:**
    - o token saiu é `Worked`;
    - 400 ou 401 é `Refused`, e pede para conferir o Client ID e o Client Secret do ambiente;
    - 5xx, 429 e tempo esgotado são `Unavailable`;
    - a resposta sem token é `Refused`;
    - sem segredo é `Incomplete`, sem pedido;
  - **o ambiente:** é o escolhido;
  - **o que o motivo não contém:** o token, o segredo, `Bearer` nem `Authorization`
- [x] 7.4 O teste de credencial da Avalara, e o registro no DI do adapter
- [x] 7.5 Teste primeiro (D365), com a fábrica de credencial e o HTTP falsos:
  - **o token novo:**
    - dois testes seguidos criam duas instâncias de credencial pela fábrica, e cada uma pede o token;
    - o `GetTokenAsync` do coletor continua reusando a instância dele: um teste no meio não cria nem troca a instância
      do coletor;
    - os testes do `ClientCredentialsD365TokenProvider` que já existem continuam verdes, sem mudança;
  - **a URL da leitura:** é exatamente `/data/FSFiscalDocumentBRs?$top=1&$select=FiscalDocumentRecId&cross-company=true`,
    com um GET só, sem seguir `nextLink`;
  - **os desfechos:**
    - um registro é `Worked`;
    - vazio é `Worked`, com o aviso das empresas;
    - a `AuthenticationFailedException` com `AADSTS7000215` é `Refused`, só com o código e sem ir ao F&O;
    - 401, 403 e 404 são `Refused`, cada um com o motivo da tabela da spec;
    - 5xx, 429 e tempo esgotado são `Unavailable`;
    - um auth incompleto, ou o segredo ausente no cofre, é `Incomplete`, sem rede;
  - **o que o motivo não contém:** o token nem o segredo
- [x] 7.6 O teste de credencial do D365:
  - **o provedor:** o `ClientCredentialsD365TokenProvider.GetFreshTokenAsync`. Ele usa a mesma resolução da referência
    e a mesma fábrica, cria uma instância nova por chamada e não a guarda no `_credentials`;
  - **o que não muda:** o `GetTokenAsync` do coletor;
  - **o registro:** o teste usa o provedor registrado como ele mesmo, se preciso, e nunca o
    `D365DevelopmentTokenProvider`. Ele entra no DI do adapter
- [x] 7.7 `dotnet build` com 0 warnings e `dotnet test` verde

## 8. O botão de teste na tela (D9, `connector-credential-test`)

- [x] 8.1 `types.ts` e `client.ts`: o `testConnector(side, environment?)`
- [x] 8.2 `ConnectorsPage`:
  - **onde fica o "Testar credencial":**
    - na aba do ERP, para o `Dynamics365`;
    - em cada seção da Avalara, Sandbox e Produção;
    - não aparece para os outros adapters;
  - **com edição pendente:** o botão avisa "Salve antes de testar: o teste usa a credencial gravada", e não chama;
  - **o resultado:** um aviso ao lado do botão, com "Funcionou" ou "Não funcionou" e o motivo. Com o freio, o horário
    em que um novo teste é aceito
- [x] 8.3 `npm test` e `npm run build` verdes

## 9. Documentação

- [x] 9.1 `docs/adr/0031-*.md` (pelo `0000-template.md`), com o que o D13 lista:
  - o rebobinamento pela tela como o mesmo mecanismo do coletor, que complementa o ADR-0024;
  - o freio no endpoint, com a razão das proteções diferentes nos dois adapters;
  - os módulos como apresentação, e não permissão.

  Registrar no `docs/adr/README.md`
- [x] 9.2 `docs/RUNNING.md`:
  - **§6:** o rebobinamento pela tela, e o SQL como alternativa;
  - **o painel:** o que ele mostra, e onde o coletor registra;
  - **§8:** o botão de teste da credencial, e o que ele prova e não prova
- [x] 9.3 `docs/STATUS.md`:
  - **itens novos:**
    - o rebobinamento com mais de uma réplica, que precisa de uma geração persistida no cursor;
    - o freio do teste em memória, por réplica;
    - a seção `poll` ilegível, que esconde o painel e o selo enquanto o coletor registra a falha;
    - a restrição de acesso por módulo na API;
    - as fatias de Contábil e Inventário;
  - **o achado do `startFrom` que some:**
    - o registro de que ele segue sem edição pela tela foi feito no planejamento (2026-09-29);
    - no apply, anotar o que mudou: o painel mostra o `startFrom` e a marca, e diz de onde a primeira passada vai
      começar
- [x] 9.4 `d365/README.md`, se couber: o teste da credencial lê a `FSFiscalDocumentBRs` com `$top=1`, e isso exige o
  privilégio da role do pacote

## 10. Prova manual do critério de saída

- [x] 10.1 Preparar:
  - `scripts/up.ps1`, `az login`, e o host e o dashboard de pé;
  - a migração `AddConnectorProfileModules` aplicada no log da subida;
  - o tenant-a com o `Dynamics365` e a integração ligada

  **Parcial (2026-09-30).**
  - **Visto no banco:** a migração está no `__EFMigrationsHistory`.
  - **O achado:** as `InboundSettings` do tenant-a estavam `{}`, o achado do STATUS de 29/09. Sem URL, `auth` nem
    `poll`, o `/info` dava `automaticIntegration: false`, e não havia painel, rebobinamento nem teste do D365.
  - **O que foi feito:** o usuário refez a entrada pela tela.
  - **Sem linha de log:** o host rodou sem o `Tee-Object`.

  **Feito (2026-10-01).**
  - **O host:** subiu às 11:52 com o código novo. A DLL do D365 em `bin` é das 11:51, já com a classificação do 11.10.
  - **A integração:** ligada pela tela. O cursor avançou até 10:54 e buscou até 11:25.
  - **Sem log em arquivo**, de novo.
- [ ] 10.2 O painel, sem abrir o banco:
  1. como Admin, ver a última verificação e a marca;
  2. gravar um Client Secret errado do D365 pela tela;
  3. ver o erro `AADSTS` e as falhas seguidas no painel, em cerca de um minuto;
  4. voltar o segredo certo, e ver as falhas zerarem;
  5. o `startFrom`: com a marca de pé, o painel mostra o `startFrom` gravado. Apagar o cursor (RUNNING §6), e o painel
     diz, antes da passada, de onde a primeira vai começar.

  Registrar os horários e as linhas do log

  **Parcial (2026-10-01).** Sem log em arquivo; a evidência é do banco.
  - **Os passos 1 a 3:** o cursor registrou 25 falhas seguidas com o erro do Entra ID, até 11:25, com o Tenant do Entra
    ID com um caractere a mais. O usuário as viu no painel.
  - **O passo 4:** com o Tenant corrigido e a integração religada, as falhas foram a 0, sem erro, na busca das 14:05:38.
  - **O passo 5, não feito na tela:**
    - a primeira metade (o `startFrom` com a marca de pé) deixou de valer com o 11.6: com marca, o ponto de partida não
      aparece;
    - a segunda (apagar o cursor e ver de onde a primeira busca começa) está provada só pelo teste da leitura do painel
      (4.1) e pela lógica do componente.
- [x] 10.3 O teste de credencial:
  - **os dois certos:** testar a Avalara (Sandbox) e o D365 com as credenciais certas dá "Funcionou";
  - **o segredo errado:**
    - com um segredo errado, dá "Não funcionou", com o motivo;
    - um segundo clique dentro de 5 minutos devolve o motivo lembrado, sem linha de requisição no log;
    - salvar o perfil libera o teste;
  - **a resposta:** conferir na rede do navegador que ela não tem token nem segredo;
  - **o token novo, se der para revogar um segredo no Entra ID:**
    1. criar um segundo segredo no app, gravá-lo pela tela, e deixar o coletor passar;
    2. apagar esse segredo no Entra ID;
    3. testar na hora: o teste dá "Não funcionou", enquanto o coletor segue com o token em cache até ele vencer.

    Sem essa revogação, o token novo fica provado só pelo teste 7.5, e isso é anotado

  **Parcial (2026-10-01), conferência visual do usuário, sem linha de log:**
  - **O que deu certo:** as credenciais do D365 e da Avalara foram validadas, e o teste com a credencial errada também
    respondeu.
  - **O que o usuário pediu:** as mensagens eram longas demais, e viraram as curtas do grupo 11.
  - **O que não foi relatado:** o freio, a conferência na rede e a revogação no Entra ID. Ficam para a conferência do
    11.9

  **Fechado (2026-10-01), conferência visual do usuário:**
  - **O freio:** visto na tela ("Novo teste disponível às 12:00"), que levou ao texto novo do aviso;
  - **o resto:** o usuário confirmou as tarefas concluídas;
  - **a revogação no Entra ID:** não foi relatada. O token novo fica provado pelos testes 7.5 (duas instâncias, dois
    pedidos, e a do coletor intacta)
- [x] 10.4 O segredo mascarado:
  - abrir Configurações com o segredo configurado, e ver a máscara e a data com o campo vazio;
  - salvar sem digitar;
  - conferir no log que o cofre não foi escrito, e que o teste de credencial continua dando "Funcionou"

  **Parcial (2026-09-30/10-01).**
  - **O cofre preservado, visto pela API:** um salvar com o código novo gravou os módulos no banco, e o Client Secret do
    Sandbox continuou na versão de 2026-09-29T17:12:50Z. O salvar não tocou o cofre.
  - **A máscara, na tela:** não aparecia. O usuário viu o campo vazio, só com "configurado em". A causa e a correção
    estão na 11.3, e a conferência fica para o 11.9

  **Fechado (2026-10-01):**
  - **A máscara:** o usuário a viu na tela e pediu a cor do texto (11.11) e as 32 bolinhas (11.12);
  - **o teste depois do salvar sem digitar:** deu "Credenciais e conexão válidas", pela conferência do usuário
- [x] 10.5 O rebobinamento:
  - **pela tela:** rebobinar o tenant-a para uma data antes das notas da `brmf`. Conferir o texto da confirmação e
    confirmar;
  - **no log:**
    - a linha do rebobinamento;
    - a passada seguinte com as referências na fila e 0 suprimidas;
  - **no banco:**
    - as NF-e recusadas foram reenviadas;
    - as confirmadas, se houver, não foram;
    - as NFS-e voltaram a ser ignoradas

  **Feito (2026-10-01, 14:05 local).** Sem log em arquivo; a evidência é do banco.
  - **Pela tela:** o usuário buscou novamente desde 01/01/2015 e confirmou.
  - **Na busca seguinte voltaram as 14 notas da `brmf`,** entre 14:05:41 e 14:05:50:
    - as 5 NF-e foram reprocessadas e enviadas ao sandbox. Voltaram `IntegrationError`, com a recusa conhecida (6, 12 e 9
      campos: `operacao`, `tipoPagamento`, `parceiro.Codigo`…);
    - as 9 NFS-e voltaram a ser ignoradas.
  - **O cursor:** a marca seguiu até 14:05:36, com 0 falhas.
  - **As confirmadas:** a base tinha sido limpa antes, e não havia nenhuma, então "as confirmadas não foram reenviadas"
    não se aplica.
  - **O "0 suprimidas":** não se distingue aqui. O host reiniciou às 11:52, e o registro de publicações, em memória, já
    estava vazio. A regra de regressão está provada pelo teste 5.5.
- [x] 10.6 Os módulos:
  - com o tenant-a só com o Fiscal, a barra não mostra o Inventário, para o Admin e para o Viewer;
  - marcar o Inventário e salvar mostra o Inventário, com o painel reservado;
  - como Viewer, o `/groups` continua respondendo com o Fiscal desmarcado

  **Feito (2026-09-30 e 10-01).**
  - **No banco e na API:**
    - os módulos gravados pela tela (`["Fiscal","Contabil","Inventario"]`);
    - o `/info` com eles, para o Admin e para o Viewer;
    - o Viewer com 200 no `/groups`, e 403 no painel, no teste e no rebobinamento.
  - **O `/groups` com o Fiscal desmarcado:** a chamada foi feita com o Fiscal marcado. Os endpoints de documentos não
    leem os módulos (D2), e a resposta não depende deles.
  - **A barra lateral:** conferência visual do usuário, para o Admin e para o Viewer, sem linha de log.
- [x] 10.7 Registrar a prova no STATUS, com as linhas do log, e o que foi só conferência visual do usuário, sem linha
  de log

  **Feito (2026-10-01), para a prova parcial:** a sessão no STATUS separa o que foi visto no banco e na API, o que foi
  conferência visual e o que ficou aberto (10.2, 10.5, 10.6 e 11.9). Não houve linha de log, porque o host rodou sem o
  `Tee-Object`.

## 11. Ajustes da prova manual (2026-10-01, design D14)

O usuário testou as duas credenciais, a certa e a errada, e pediu estes ajustes. Cada um fecha com build e teste, e a
conferência na tela fica para o 11.9.

- [x] 11.1 As mensagens do teste de credencial, no servidor:
  - **a resposta:** a `ConnectorCredentialTestService` responde só com as frases curtas:
    - "Credenciais e conexão válidas";
    - "Credenciais ou ambiente inválidos", na recusa e na credencial incompleta;
    - "Não foi possível conectar agora. Tente novamente em instantes", na indisponibilidade, que é decisão nossa (D14);
  - **o detalhe:** o motivo detalhado sai no `CredentialTestLog`, e o `POST /connector/test` o escreve no log do host,
    com o tenant, o adapter, o ambiente, o veredito e se veio do freio;
  - **os testes:** a mensagem por veredito, e o detalhe no log, inclusive o lembrado pelo freio. São 21 testes do
    serviço, verdes
- [x] 11.2 A tela do teste:
  - mostra a mensagem do servidor e, com o freio, "Corrija e salve para testar de novo, ou aguarde até HH:MM", porque o
    "Novo teste disponível às HH:MM" da primeira versão não dizia de onde vinha nem como sair;
  - com edição pendente, "Salve as alterações antes de testar"
- [x] 11.3 A máscara do segredo visível fora de foco.
  - **A causa:** o MUI esconde o placeholder enquanto o rótulo está dentro do campo.
  - **A correção:** o rótulo do campo de segredo fica sempre recolhido.
  - **O que não muda:** a máscara continua placeholder, e nunca valor. O teste do payload continua verde
- [x] 11.4 A URL do token sai da tela, no `OUTBOUND_ADAPTERS` da Avalara.
  - **O `tokenUrl` já gravado:** continua valendo e volta intacto ao salvar.
  - **O RUNNING:** §3 e §8 ajustados
- [x] 11.5 As opções da integração automática aparecem assim que o interruptor é ligado na tela, sem salvar.
  - **O 404:** quando o adapter gravado ainda não varre, o painel pede para salvar as configurações
- [x] 11.6 Os textos da tela, sem a língua do código:
  - **o painel:** "Situação", "Última busca", "Falhas consecutivas", "Sincronizado até" e "Aguardando o ERP até". O ponto
    de partida só aparece sem marca;
  - **o rebobinamento:**
    - "Buscar novamente desde";
    - a confirmação em quatro frases;
    - "A próxima busca começa em…";
  - **as mensagens do servidor no rebobinamento:** sem "coletor" nem "marca", com os testes ajustados;
  - **o painel reservado:** "Este módulo ainda não está disponível";
  - **a aba de módulos:** uma frase
- [x] 11.7 Os artefatos:
  - as specs `automatic-integration-panel`, `connector-credential-test`, `module-navigation` e
    `connector-secret-references`;
  - o design (o D14, e os apontamentos no D3, D6, D8 e D9);
  - o ADR-0031;
  - o RUNNING;
  - `openspec validate --strict` ok
- [x] 11.8 Build e testes:
  - **o .NET:** o host estava de pé e travava a pasta `bin` dele. O build foi feito sem o host, e o host compilou numa
    saída à parte. 0 warnings. Os testes deram 306 na Application, 117 na Infrastructure, 230 na Avalara e 164 no D365,
    todos verdes;
  - **o dashboard:** `npm test` com 14 testes e `npm run build` verdes
- [x] 11.10 A credencial errada caía como indisponível (segunda rodada, 2026-10-01).
  - **O sintoma:** com a credencial errada do D365, a tela mostrava "O Entra ID não respondeu como esperado, e o token
    não saiu." Era o detalhe interno, porque o host ainda rodava o código anterior ao 11.1 (subiu às 10:31, e o 11.1 é das
    11:01). Mas, mesmo no código novo, o caso seria classificado como indisponível.
  - **A correção:** só uma causa de rede na cadeia é indisponibilidade (`NetworkFailure`).
    - **No D365:** a falha do Entra ID sem causa de rede é recusa, com o AADSTS procurado na cadeia inteira. O formato
      inválido do tenant ou do Client ID é recusa. O host do F&O que não existe é recusa.
    - **Na Avalara:** o 404, o 403 e o 405 no endpoint de token são recusa, e o host que não existe também.
  - **Os testes:** 5 novos no D365, entre eles o caso da prova, e 5 na Avalara. Build sem warnings, e todas as suítes
    verdes
- [x] 11.11 A máscara com cara de campo preenchido (2026-10-01).
  - **O pedido:** na tela, a máscara parecia um placeholder, cinza e clara, e não um campo preenchido.
  - **A correção:** no segredo configurado, o placeholder ganha a cor do texto e a opacidade cheia.
  - **O que não muda:** continua sendo placeholder, e não valor. O teste do payload continua verde, e o `npm run build`
    também
- [x] 11.12 A máscara com 32 bolinhas, mais ou menos a largura do Client ID (2026-10-01).
  - **O tamanho:** continua fixo, e não conta os caracteres do segredo.
  - **Os testes:** o teste do payload procura a constante e os caracteres de máscara, e continua valendo. `npm test` e
    `npm run build` verdes
- [x] 11.9 Conferência na tela, com o host reiniciado no código novo (e, se possível, com `| Tee-Object -FilePath
  host-fatia4.log`):
  - **o teste de credencial:** as três mensagens curtas. O freio com "Corrija e salve para testar de novo, ou aguarde
    até…" no segundo clique. A linha
    `Teste de credencial do tenant …` no log, com o detalhe;
  - **a máscara:** `••••••••` visível sem clicar no campo;
  - **a Avalara:** não tem mais o campo da URL do token;
  - **o painel:**
    - aparece ao ligar o interruptor, antes de salvar;
    - some ao desligar;
    - os textos novos;
  - **o rebobinamento:** "Buscar novamente desde", a confirmação curta, e a passada seguinte;
  - **o resto:** o 10.2, o 10.5 e o 10.6, que ainda não foram relatados

  **Feito (2026-10-01), conferência visual do usuário:**
  - as mensagens curtas, o aviso do freio, a máscara, a Avalara sem a URL do token, o painel ao ligar o interruptor e os
    textos novos;
  - a busca seguinte ao rebobinamento fica com o 10.5.
