## 1. O contrato do despacho em dois passos

A fatia estrutural (design §1). Ela **não** muda comportamento observável: ao fim dela o hub envia
exatamente como envia hoje. Vem primeiro porque tudo depois depende da forma nova.

- [x] 1.1 Escrever o teste de conformidade `ComplianceDispatcherContractTests` em
      `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/`: classe base que roda
      `PrepareAsync` com um handler que **falha o teste se for chamado** (reusar o
      `StubHttpMessageHandler` existente). O teste falha agora porque a forma nova não existe.
      <br>**Evidência:** a suíte pega de verdade. Um `LyingDispatcherProof` temporário — adapter que faz
      `POST` de dentro do `PrepareAsync` — herdou a suíte e deu vermelho: *"PrepareAsync fez 1
      requisição(ões) à plataforma; a última foi POST http://localhost/documents"*. O arquivo da prova foi
      removido depois. A plataforma falsa **responde como se tivesse aceitado**, de propósito: a prova é a
      contagem, e não a exceção — senão bastaria engolir a exceção para "passar".
- [x] 1.2 Trocar `IComplianceDispatcher<TDocument>.SubmitAsync` por `PrepareAsync` + `SendAsync`, e criar
      o `PreparedDispatch<TDocument>` em `src/FiscalHub.Application/Outbound/Integration.cs`. `SendAsync`
      só aceita um `PreparedDispatch` — é o que fecha a ordem preparar→enviar no compilador.
      <br>**Nota:** o `PreparedDispatch` é classe abstrata, e não record, para não ganhar um `ToString`
      gerado que despejasse o payload num log (ADR-0027).
- [x] 1.3 Dividir o `AvalaraComplianceDispatcher.SubmitAsync` no ponto que já existe: tudo até o
      `SaveOutboundAsync` (inclusive) vira `PrepareAsync`; o POST e a classificação da resposta viram
      `SendAsync`. As rejeições permanentes de configuração e de contrato continuam saindo do
      `PrepareAsync`, antes de qualquer requisição de envio.
      <br>**Evidência:** `Preparing_traces_the_outbound_payload_without_sending` — a foto do destino fica
      gravada com `RequestCount == 0`. As recusas de configuração e de contrato seguem com 0 requisições
      (`Tenant_without_the_codes_is_rejected_before_any_request`,
      `Data_the_contract_cannot_represent_is_rejected_before_any_request`, e os 13 casos do
      `AvalaraDispatcherPlatformCodesTests`).
      <br>**Achado, decidido na revisão (opção A):** as quatro recusas de **autenticação** não descem para o
      `PrepareAsync` — conta de plataforma, credencial, endpoint de token e valor do segredo no cofre saem do
      token provider dentro do `ApplyAuthAsync`, no `SendAsync`. Com a chave desligada a nota fica em "Salva",
      porque são condição do tenant e não da nota; quem responde por elas é o "Testar credencial". Está
      escrito na spec (`A chave desligada não esconde rejeição`) e no design §4.
- [x] 1.4 Ajustar `DocumentPipeline.ProcessAsync` para chamar `PrepareAsync` e, em seguida, `SendAsync` —
      ainda sem chave nenhuma. Nenhum desfecho muda.
- [x] 1.5 Ajustar `DocumentPipelineTests`, `GoodsInvoicePipelineTests`, `AvalaraComplianceDispatcherTests`
      e `AvalaraDispatcherPlatformCodesTests` ao contrato novo; herdar a suíte de conformidade no teste da
      Avalara. Fatia fecha com `dotnet build` (0 warnings) e `dotnet test` verdes.
      <br>**Evidência:** `dotnet build --artifacts-path obj/guard` com **0 warnings, 0 erros**;
      `dotnet test` com **1380 aprovados, 0 falhas** (42 ignorados, os de banco/sandbox de sempre).
      Dashboard intacto nesta fatia, e mesmo assim conferido: `npm test` 177/177 e `npm run build` verdes.
      <br>**Desvios da lista nominal do design, aceitos na revisão do grupo 1:** (a) dois dublês a mais precisaram implementar a porta
      nova para compilar — `StatusPollerTests` e `DiscoveryToPipelineTests`
      (`tests/FiscalHub.Integration.Tests/`); (b) um arquivo de teste novo,
      `PrepareAndSend.cs`, com a extensão `SubmitAsync` = preparar-e-enviar, que é o que a esteira faz com
      a chave ligada. Com ela, os ~25 testes de desfecho do adapter (incluindo `AvalaraDispatcherPlatformCodesTests`
      e `SandboxFixtureTests`) seguiram **sem uma linha alterada** — o que é a prova de que a divisão não
      mudou comportamento observável.

## 2. Onde a chave mora

- [x] 2.1 Teste em `SqlConnectorProfileStoreTests`: a chave é lida e gravada, e um perfil gravado sem ela
      volta ligada.
      <br>**Evidência:** `The_outbound_dispatch_switch_goes_and_comes_back_and_a_profile_saved_without_it_is_on`
      (ligada por omissão → desligada → ligada de novo) e
      `A_profile_created_already_switched_off_is_saved_switched_off`, que confere o valor **na linha do banco**.
      O segundo cobre o caminho do INSERT, que é onde o erro fácil mora (ver 2.2).
- [x] 2.2 `OutboundDispatchEnabled` em `TenantConnectorProfile` e em `ConnectorProfileRow`; ler e gravar em
      `SqlConnectorProfileStore`.
      <br>**Correção da tarefa:** não há o que mapear em `ProcessingDbContext` — um `bool` não anulável vira
      `bit NOT NULL` por convenção, como o `UserRow.Active`, e o arquivo não foi tocado.
      <br>**Decisão de implementação:** o padrão "ligada" fica no **CLR** (`= true` nas duas), e **não** como
      `HasDefaultValue(true)` no modelo EF. Com o default no modelo, o EF omite a coluna no INSERT quando o
      valor é o sentinela — que, num `bool`, é `false` —, e um perfil criado já com a chave desligada seria
      gravado **ligado**. É exatamente o que o teste do INSERT em 2.1 tranca.
- [x] 2.3 Migração `AddOutboundDispatchEnabled`: `bit NOT NULL DEFAULT 1` em `ConnectorProfiles`, sem
      backfill. Conferir que o `ProcessingDbContextModelSnapshot` foi regenerado.
      <br>**Evidência:** `20261016133954_AddOutboundDispatchEnabled.cs`, com `defaultValue: true` — o EF gerou
      `false` (o default do CLR, já que o modelo não declara default) e a correção foi à mão, como o
      `AddTenantsAndUserActive` fez com o `Users.Active`. Snapshot regenerado:
      `Migrations/ProcessingDbContextModelSnapshot.cs:149`.
      <br>**Correção da lista do design:** o snapshot mora em `Migrations/`, e não em `Persistence/`.
      <br>**Desvio de comando:** `dotnet ef migrations add ... --startup-project src/FiscalHub.Host` falha — o
      Host não referencia o `Microsoft.EntityFrameworkCore.Design`. Funciona com
      `--project src/FiscalHub.Infrastructure` sozinho.
- [x] 2.4 Criar a porta `IOutboundDispatchSwitch` (`IsOnAsync(tenantId, ct)`) e a implementação
      `OutboundDispatchSwitch` sobre o `IConnectorProfileStore`, em
      `src/FiscalHub.Application/Outbound/`. Tenant sem perfil = ligada.
- [x] 2.5 Teste novo `OutboundDispatchSwitchTests`: ligada por padrão, desligada quando gravada assim,
      ligada quando não há perfil, e o estado de um tenant não vaza para outro.
      <br>**Evidência:** os quatro casos, um teste cada. Guarda do grupo: `dotnet build` 0 warnings,
      `dotnet test` **1386 aprovados / 0 falhas** (+6), `npm test` 177/177 e `npm run build` verdes.

## 3. A chave nas duas respostas

- [x] 3.1 Teste em `ConnectorProfileServiceTests`: o `GET /connector` traz a chave, e o `PUT` a grava;
      ausente no corpo, mantém a gravada.
      <br>**Evidência:** `The_read_brings_the_outbound_dispatch_switch_and_the_write_saves_it`,
      `A_body_without_the_switch_keeps_the_one_already_saved` (a inversão: sem o `?? stored`, a chave desligada
      voltaria a ligar sozinha no primeiro salvar) e `A_first_profile_without_the_switch_is_saved_switched_on`.
- [x] 3.2 Teste em `InstanceInfoTests`: o `/info` responde `outboundDispatch`, inclusive sem perfil
      (ligada).
      <br>**Evidência:** `The_outbound_dispatch_switch_comes_from_the_profile` (ligada e desligada),
      `Without_a_profile_the_outbound_dispatch_is_on`, e o campo entrou na lista exata de propriedades que
      `The_response_brings_the_tree_and_no_module_list_of_the_tenant` já trancava.
- [x] 3.3 Implementar: o campo em `ConnectorProfileRequest`/`ConnectorProfileView`, em
      `ConnectorProfileService`, em `InstanceInfo`. Registrar o `IOutboundDispatchSwitch` no DI.
      <br>**Correção da tarefa:** o `PUT /connector` do `Program.cs` não precisou de nada — ele passa o
      `ConnectorProfileRequest` inteiro adiante, e o campo novo chega pelo próprio model binding. O único
      toque no Host foi o DI (`AddScoped<IOutboundDispatchSwitch, OutboundDispatchSwitch>()`, scoped porque
      lê o perfil pelo DbContext).
      <br>**Guarda do grupo:** `dotnet build` 0 warnings, `dotnet test` **1392 aprovados / 0 falhas** (+6),
      `npm test` 177/177 e `npm run build` verdes.

## 4. O corte e a situação "Salva"

- [x] 4.1 Testes em `DocumentPipelineTests`, antes do código: (a) chave ligada envia, como hoje; (b) chave
      desligada **não** chama `SendAsync`, chama `PrepareAsync` e grava "Salva" com motivo; (c) chave
      desligada não suprime a rejeição de validação nem a rejeição de contrato vinda do `PrepareAsync`.
      <br>**Evidência:** `With_the_switch_on_the_document_is_sent_as_always`,
      `With_the_switch_off_the_document_is_prepared_saved_and_never_sent` (prepara 1, envia 0, grava o motivo),
      `With_the_switch_off_an_invalid_document_is_still_rejected_and_not_saved`,
      `With_the_switch_off_a_rejection_from_the_preparation_is_still_a_rejection`,
      `With_the_switch_off_the_domain_photo_and_the_metadata_are_still_recorded` e
      `The_switch_is_read_for_the_tenant_of_the_document`.
      <br>**Correção da tarefa:** o "ignorado de tipo fora do escopo" **não** cabe no `DocumentPipelineTests`
      — ele sai do `DocumentRouter`, antes da esteira, e o roteador não tem dependência nenhuma da chave. Não
      há o que a chave pudesse suprimir ali, e o `DocumentRouterTests` já prova o ignorado com um pipeline
      falso que nunca é chamado. A estrutura é a prova; um teste novo não acrescentaria.
- [x] 4.2 `IntegrationStatus.Saved` e `IProcessingStore.RecordSavedAsync`.
      <br>**Nota:** o `Status` é persistido como **texto** (`HasConversion<string>`, `nvarchar(20)`), então o
      valor novo não pede migração nem depende da posição no enum.
      <br>⚠️ **Toque forçado em arquivo de dead-letter, para o Marcelo resolver no merge:** o método novo na
      porta `IProcessingStore` quebra a compilação de **todos** os dublês dela, e um deles é o
      `DeadLetterHandlerTests.FakeStore`
      (`tests/Adapters/Messaging/FiscalHub.Adapters.Messaging.ServiceBus.Tests/DeadLetterHandlerTests.cs`).
      A edição é **uma linha aditiva** — `public Task RecordSavedAsync(...) => Task.CompletedTask;` no fim da
      classe —, não encosta em comportamento de dead-letter nenhum, e era isso ou build vermelho. Os outros
      dublês ajustados: `DocumentRouterTests`, `StatusPollerTests`, `GoodsInvoicePipelineTests`,
      `DispatchToMockTests` e `DiscoveryToPipelineTests`. Nos dois testes de integração, o pipeline passou a
      receber o `OutboundDispatchSwitch` **real** sobre o perfil real, em vez de um dublê.
- [x] 4.3 O corte em `DocumentPipeline.ProcessAsync`: preparar sempre; enviar só com a chave ligada; senão,
      `RecordSavedAsync` com o motivo de envio desligado.
      <br>O motivo é a constante `DocumentPipeline<T>.OutboundDispatchOffReason`, no formato dos outros
      desfechos ("salva: …", como "ignorado: …"), e os testes comparam com ela em vez de repetir o texto.
- [x] 4.4 Testes em `SqlProcessingStoreTests`: `RecordSavedAsync` preserva o `ExternalId` já gravado (como
      o `RecordIgnoredAsync`), e `AlreadyProcessedAsync` passa a considerar `Saved` terminal — com o
      gatilho manual continuando a furar.
      <br>**Evidência, no store:** `Saving_a_document_without_sending_records_it_with_the_reason_and_no_external_id`,
      `Saving_a_document_that_was_once_sent_keeps_its_external_id`,
      `A_saved_document_is_terminal_so_the_overlap_pass_does_not_reprocess_it`,
      `A_saved_document_whose_content_changed_reenters_the_pipeline` e `Saving_the_same_document_again_keeps_one_row`.
      <br>**Evidência, na esteira** (o gatilho manual não é visível do store — quem pula a pergunta é o
      pipeline): `Turning_the_switch_back_on_does_not_resend_what_is_already_saved` (religar não envia e não
      regrava a foto do domínio), `The_manual_reprocess_takes_a_saved_document_out_with_the_switch_on` e
      `The_manual_reprocess_with_the_switch_off_saves_it_again_instead_of_sending` (o pedido por fora da tela
      também não fura a chave).
      <br>**Prova por inversão:** tirando `|| d.Status == IntegrationStatus.Saved` do `AlreadyProcessedAsync`,
      o `A_saved_document_is_terminal_…` cai (`Assert.True() Failure / Expected: True`); a linha foi
      restaurada e a suíte voltou a 43/43. É o ponto que teria quebrado em silêncio.
- [x] 4.5 Implementar os dois em `SqlProcessingStore`.
- [x] 4.6 Teste de que uma nota em "Salva" não entra na passada de consulta de status. Confirmar que o
      filtro atual (`Submitted` **com** `ExternalId`) já basta; se bastar, o teste é a prova e nada muda no
      `StatusPoller`.
      <br>**Correção da tarefa:** o teste **não** cabe no `StatusPollerTests` — lá o store é falso, e
      `ListPendingAsync` devolve o que o teste mandar, o que não provaria nada. O filtro vive no
      `SqlProcessingStore.ListPendingAsync`, e é lá que a prova está: `A_saved_document_does_not_enter_the_status_poll`,
      com os dois casos — a nota que nunca foi enviada, e a que já foi um dia, **guarda o identificador antigo**
      e voltou a "Salva" (o filtro é pelo estado, então ela também fica de fora). O `StatusPoller` não mudou.
      <br>**Guarda do grupo:** `dotnet build` 0 warnings, `dotnet test` **1407 aprovados / 0 falhas** (+15),
      `npm test` 177/177 e `npm run build` verdes.

## 5. As contagens no servidor

- [x] 5.1 Teste em `SqlDocumentQueriesTests`: a faixa `Salvas` conta a nota salva; ela entra no total e
      fica fora de finalizadas, em processamento e com erro.
      <br>**Evidência:** o `Counts_every_status_in_its_card` ganhou a oitava linha, com o estado `Saved`, e
      passou a exigir `Total = 8, Finalizadas = 1, EmProcessamento = 2, ComErro = 3, Salvas = 1` — a
      comparação é do record inteiro, então faixa errada ou total errado quebra. Mais
      `A_group_of_only_saved_notes_counts_them_all_as_saved_and_nothing_else`, que é o caso do selo do grupo
      (3 salvas → `Total 3, Salvas 3`, e zero nas outras três).
- [x] 5.2 `Salvas` em `DocumentGroup` e `ModelTotals` (`IDocumentQueries`); a quarta faixa em
      `SqlDocumentQueries`, nas duas consultas (grupos e totais).
      <br>O campo é `required` nos dois records, como os irmãos: um chamador que esqueça a contagem não
      compila. **Guarda do grupo:** `dotnet build` 0 warnings, `dotnet test` **1408 aprovados / 0 falhas**,
      `npm test` 177/177 e `npm run build` verdes.

## 6. A tela — Configurações

- [x] 6.1 O interruptor "Enviar ao conector de saída" em `ConnectorsPage`, na aba de saída, com o campo em
      `useConnector` e no corpo do `PUT` (`connectorPayload`). Rótulo que diga o eixo, para não se
      confundir com "Integração automática", que é de entrada.
      <br>O interruptor fica **antes** do seletor de plataforma, de propósito: a chave é do tenant e vale para
      toda a saída, e não de um conector. Abaixo dele, uma linha dizendo o que acontece desligado.
      <br>**Correção da lista do design:** o campo **não** entrou no `useConnector.ts` — ele é só o hook da
      consulta; o estado do formulário mora no `ConnectorsPage`, e o campo entrou no `ConnectorForm` do
      `connectorPayload.ts`.
- [x] 6.2 Teste em `connectorPayload.test.ts`: a chave vai no corpo com o valor do formulário.
      <br>**Evidência:** quatro casos — vai no corpo ligada e desligada; o perfil gravado desligado abre o
      formulário desligado; uma resposta **sem o campo** abre ligada (o comportamento de sempre); e virar a
      chave conta como edição pendente, então o botão de testar credencial pede para salvar antes.
- [x] 6.3 Conferir na tela, nos dois temas, que os dois interruptores se distinguem à primeira leitura.
      <br>✅ **Aprovada na conferência em tela do dono do produto (2026-10-16).** Os dois se distinguem à primeira
      leitura, nos dois temas. A distinção que está no código: abas diferentes (Entrada × Saída), rótulos que dizem
      o eixo ("Integração automática" × "Enviar ao conector de saída") e a linha de apoio no de saída. O rótulo
      bastou — a aba de entrada **não** precisou de linha de apoio, e nada mudou por causa desta conferência.

## 7. A tela — a nota

- [x] 7.1 `'Saved'` em `types.ts`; rótulo "Salva" e tom `pending` em `StatusChip.tsx`; **fora** de
      `FAILURE_STATUSES`.
      <br>O `FAILURE_STATUSES` não foi tocado, que é o ponto: "Salva" não é falha, fica fora do filtro de
      falhas e não conta como erro, como a ignorada.
      <br>⚠️ **Faltou a outra metade, e ela virou a 10.4:** ficar fora de `FAILURE_STATUSES` está certo, mas o
      botão de reprocessar era decidido por `isFailure` — então a nota em "Salva" ficou **sem botão**, mesmo com
      a chave ligada. A elegibilidade precisava de lista própria.
- [x] 7.2 Testes em `DocumentDetail.test.tsx`: (a) com a chave desligada, nota rejeitada **não** mostra o
      botão e mostra o aviso; (b) com a chave ligada, o botão volta; (c) com o `/info` ainda sem resposta,
      nem botão habilitado nem aviso.
      <br>**Evidência:** os três, mais três que a spec pede — a chave desligada tira o botão também da
      `Unconfirmed` e da `DeadLettered` (porta única da saída), a nota em "Salva" mostra o aviso e não tem
      botão, e a nota aceita segue sem botão.
      <br>**Prova por inversão:** trocando a condição do botão por `failed` sozinho, **três** caem — inclusive
      o do `/info` sem resposta, que é o caso em que a tela não pode afirmar nada. Restaurado: 9/9.
      <br>⚠️ **Dois destes testes afirmavam o comportamento errado, e foram reescritos na 10.2/10.3:** o "a nota
      aceita também não ganha botão" exigia a faixa numa nota que nunca teve reprocesso, e todo caso de "Salva"
      foi escrito com a chave **desligada** — por isso nenhum deles pegou o defeito da 10.4.
- [x] 7.3 Implementar em `DocumentDetail.tsx`: o botão sob `isFailure(status) && info?.outboundDispatch`,
      e a faixa de aviso que diz que o reprocesso está indisponível porque o envio está desligado.
      <br>**Texto escolhido** (a pergunta em aberto do design §7): *"O reprocesso está indisponível: o envio ao
      conector de saída está desligado para este tenant. Ligue-o em Configurações → Conectores → Saída para
      voltar a enviar."* Diz o estado, a causa e o caminho. Vale a sua leitura.
      <br>**Nota de tela:** numa nota em "Salva" apareciam **duas** faixas — esta, e a do motivo gravado
      ("salva: o envio … desligado").
      <br>⚠️ **Superado pela 10.2 e pela 10.3.** A conferência em tela de 2026-10-16 recusou o desenho: a faixa
      aparecia em qualquer nota (inclusive "Finalizado" e "Ignorado", que nunca tiveram reprocesso), e o botão
      removido contrariava o texto da própria spec, que fala de *botão desativado sem explicação*. O botão passa
      a ficar visível e desativado, com o motivo nele; a faixa sai, e a repetição acima some junto.
- [x] 7.4 `outboundDispatch` em `InfoResponse` (`types.ts`).
      <br>Sete fixtures de teste do dashboard passaram a exigi-lo (o campo é obrigatório na interface), e foi
      isso que apontou cada lugar que lê o `/info`.

## 8. A tela — cards e selo

- [x] 8.1 Teste em `cards.test.ts`: `cardSums` soma `salvas`.
      <br>**Evidência:** `somam as salvas, e elas ficam fora de finalizadas, em processamento e com erro` —
      a comparação é do objeto inteiro (`toEqual`), então uma soma que vazasse para outra faixa quebraria.
- [x] 8.2 Teste novo `GroupStatusChip.test.tsx`: grupo só de salvas → selo de salvas; grupo com salva e
      rejeitada → "Com pendências"; grupo só de confirmadas → "Finalizado".
      <br>**Evidência:** os três, mais a precedência sobre "Processando" e o caso misto (salvas + finalizadas
      → "Salvas", porque ainda há nota que ninguém enviou).
      <br>**Prova por inversão:** desligando a condição `salvas > 0`, caem dois — `só de salvas` volta a dizer
      "Finalizado", que é exatamente a mentira de tela que esta change conserta. Restaurado: 5/5.
- [x] 8.3 `salvas` em `DocumentGroup`/`ModelTotals` (`types.ts`), em `CardSums`/`cardSums`, o quarto card
      em `GroupsPage` e o estado novo em `GroupStatusChip`.
      <br>**Decisão da pergunta em aberto (posição do card):** depois de "Em processamento" e **antes** de
      "Com erro" — a leitura vai do que terminou para o que precisa de alguém, e "Com erro" fica por último.
      A linha de KPIs passou de 4 para 5 colunas. A cor é o token `--pending-text`, o mesmo tom do selo
      "Salva": nenhuma cor de status nova (a spec `dashboard-brand-identity` proíbe), e o rótulo faz a
      distinção.
- [x] 8.4 Conferir na tela: com notas salvas, "Documentos" e "Salvas" batem, e o selo da linha não diz
      "Finalizado". Conferir o contraste do card novo nos dois temas.
      <br>✅ **Aprovada na conferência em tela do dono do produto (2026-10-16), com dado real.** Ele rodou uma
      captura de verdade com a chave **desligada**: a nota entrou e aparece como **"Salva"**, com as contagens
      batendo ("Documentos" × "Salvas") e o selo do grupo correto — não diz "Finalizado". O contraste do quinto
      card serviu nos dois temas e a linha de 5 colunas coube sem quebrar rótulo. Nada mudou por causa desta
      conferência: é a esteira inteira, do corte no pipeline até o card, conferida ponta a ponta na tela.

## 9. Fechamento

- [x] 9.1 ADR `docs/adr/0041-capturar-sem-enviar.md` — o despacho em dois passos (o que garante e o que
      não garante, como no design §1) e onde a chave mora. Atualizar `docs/adr/README.md`. O "não garante"
      tem de incluir as **duas** ressalvas: o POST escrito dentro do `PrepareAsync`, e o preparado montado
      na mão de dentro do assembly do adapter (pedido da revisão do grupo 1 — quem lê o ADR precisa achar).
      Citar também onde as recusas de autenticação aparecem, que é a decisão A da mesma revisão.
      <br>✅ `docs/adr/0041-capturar-sem-enviar.md`, com as duas ressalvas nomeadas em "o que NÃO garante", a
      decisão A numa seção própria ("As recusas de autenticação não aparecem com a chave desligada"), a
      Alternativa C registrada como o caminho se a garantia total virar requisito, e o rollback. Índice
      atualizado.
- [x] 9.2 Registrar as duas pendências nos Próximos passos de `docs/STATUS.md`: (a) o selo do grupo ainda
      mente para "Ignorado"; (b) promover a suíte de conformidade a biblioteca compartilhada quando houver
      um segundo adapter de saída.
      <br>Itens **10** e **11**, aditivos no fim da lista numerada — longe do item 1, que é onde o D2 do outro
      worktree encosta.
- [x] 9.3 `dotnet build` (0 warnings) e `dotnet test` verdes; `npm test` no `dashboard/` verde. Com o Host
      rodando, usar `--artifacts-path obj/guard` dentro do repositório para não esbarrar na DLL travada.
      <br>**Guarda final:** `dotnet build --artifacts-path obj/guard` → **0 warnings, 0 erros**; `dotnet test`
      → **1408 aprovados, 0 falhas** nos 11 assemblies (42 ignorados, os de banco/sandbox de sempre);
      `npm test` → **193/193** em 19 arquivos; `npm run build` → verde.
      <br>**Nota de execução:** `npm test` e `dotnet build` **não** rodam juntos nesta máquina — concorrendo,
      o vitest estoura "Timeout calling fetch" no transform, que é sobrecarga e não defeito. Rodados um de
      cada vez.
- [x] 9.4 `openspec validate --all --strict` verde.
      <br>**29 passed, 0 failed.**
      <br>**Conferência do item solto da lista do design:** o `tools/MockComplianceApi` **não** precisou
      acompanhar a divisão, como o design supunha — ele é o outro lado do HTTP (um servidor), não implementa
      `IComplianceDispatcher`, e compila intocado.

## 10. Os quatro consertos da conferência em tela (2026-10-16)

Quatro achados do dono do produto olhando a tela pronta. São todos de **tela e de spec** — o servidor não muda
nenhuma linha. A fatia não foi arquivada, então o delta e o design são corrigidos no lugar.

- [x] 10.1 **O subtítulo do card "Salvas" afirmava um status ao vivo.** Dizia *"salvas sem enviar: envio
      desligado"* — texto fixo que continuava na tela com a chave já **religada**. Os irmãos dele sempre foram
      definições de categoria ("confirmados pelo compliance", "aguardando retorno do compliance", "rejeitados,
      sem retorno ou falha"). Passa a ser **"gravadas sem enviar ao compliance"**.
      <br>Os quatro subtítulos saíram do JSX e viraram `CARD_NOTES` em `cards.ts` — é o que deixa o teste que
      cobre os cards (`cards.test.ts`) alcançá-los sem montar a página inteira.
      <br>**Evidência:** `o da "Salvas" define a categoria, e não o estado da chave` e `nenhum deles afirma um
      estado ao vivo` (a guarda contra a reincidência: nenhum subtítulo fala de ligado/desligado).
- [x] 10.2 **A faixa de aviso sai do `DocumentDetail`.** Ela aparecia em QUALQUER nota com a chave desligada —
      inclusive numa "Ignorado" ou "Finalizado", que nunca teve botão de reprocessar —, atribuindo à chave uma
      indisponibilidade que não é dela. A explicação passa a existir **só onde o botão existe**.
      <br>**Evidência:** `a nota que nunca teve reprocesso, com a chave desligada: nenhum botão e NENHUMA
      explicação` (Finalizado e Ignorado). O teste antigo que afirmava o contrário — "a nota aceita também não
      ganha botão", que exigia a faixa — foi reescrito: ele estava errado.
- [x] 10.3 **O botão fica visível e desativado, com o motivo nele.** Decisão do dono do produto, alinhada com o
      texto da própria spec ("um botão desativado sem explicação deixa o usuário olhando um botão morto").
      <br>**Acessibilidade, que é o que decide a forma:** um `<button disabled>` não recebe hover nem foco em
      vários navegadores, e um `title` nele nunca chegaria ao usuário. Então o desativado é `aria-disabled="true"`
      mais guarda no `onClick` (não dispara a mutation), o botão continua focável, e o motivo é um `role="tooltip"`
      ligado por `aria-describedby`, que aparece no **hover**, no **foco por teclado** e no **clique/toque**.
      Sem dependência nova — `dashboard/src/components/` só tem Controls, FhDataGrid, Modal e StatusChip.
      <br>**Texto:** *"O envio ao conector de saída está desligado para este tenant. Ligue-o em Configurações →
      Conectores → Saída para voltar a enviar."*
      <br>**Evidência:** `a nota rejeitada com a chave desligada: o mesmo botão desativado, com a mesma
      explicação`, `a chave desligada desativa o botão de qualquer nota que o tenha` e `a explicação alcança o
      mouse, o teclado e o toque — e some quando se sai` (este confere também `document.activeElement`, que é o
      que um `disabled` tiraria).
      <br>**Desvio de teste, anotado:** o `.focus()` do jsdom não propaga o `focusin` até a raiz do React 18, então
      o teste foca de verdade (prova a focabilidade) **e** dispara `fireEvent.focusIn`. No navegador, um só tabular
      faz as duas coisas.
- [x] 10.4 **O mais sério: a nota em "Salva" não tinha botão nem com a chave ligada.** O botão era
      `failed && outboundDispatch === true`, e `FAILURE_STATUSES` não inclui `Saved` — então a nota em "Salva"
      ficava sem o seu **único** caminho de saída, contra o ADR-0041, contra a proposal e contra um cenário já
      publicado no próprio delta. O servidor sempre aceitou: `DocumentReprocess.ReprocessAsync` não tem guarda de
      status. Nenhum teste pegou porque **todo** caso de "Salva" foi escrito com a chave desligada.
      <br>**Conserto:** `REPROCESSABLE_STATUSES` / `isReprocessable` = falhas **+** `Saved`, **ao lado** de
      `FAILURE_STATUSES` em `StatusChip.tsx`, com o comentário dizendo por que as duas listas são diferentes e o
      que quebra se alguém as unificar (de um lado: "Salva" entra no filtro de falhas e na contagem de erro; do
      outro: a nota em "Salva" fica sem saída). `Saved` **não** entra em `FAILURE_STATUSES`.
      <br>**Evidência — o teste que faltava:** `a nota em "Salva" com a chave ligada: botão habilitado, e o clique
      reprocessa` (chama `api.reprocess` com o tenant e a chave natural da nota). Mais `a nota em "Salva" com a
      chave desligada: o botão continua lá, desativado, com a explicação`, que prova a guarda do clique.
- [x] 10.5 Spec, design e ADR acertados — a fatia não foi arquivada, então o delta ainda é editável.
      <br>**Spec (`outbound-dispatch-switch`):** a requirement do reprocesso passa a dizer *visível e desativado,
      com a explicação no próprio botão*, com as três entradas e a proibição de explicar onde não há botão; os
      cenários de "indisponível" foram reescritos; o "Ligar devolve o botão" perdeu a menção à faixa; entraram o
      cenário da nota que nunca teve reprocesso e o da explicação pelo teclado e pelo toque; e entrou a requirement
      nova **"A nota em 'Salva' é reprocessável, e isso não a torna falha"**, com as duas listas e o porquê. O
      requisito das duas respostas passou a dizer que, sem resposta do `/info`, o botão fica **ausente**.
      <br>**Spec (`document-grouping`, MODIFIED, com todos os cenários publicados mantidos):** o subtítulo do card
      diz o que entra na contagem e não afirma estado de configuração, com cenário próprio.
      <br>**Design:** a §7 foi reescrita (quem tem botão × como o botão sai, as duas listas, a acessibilidade e os
      três estados do `/info`); a §3 e a §5 perderam a menção à faixa de aviso; a pergunta em aberto do texto do
      aviso foi fechada, e no lugar dela fica a leitura do tooltip na tela. A `proposal.md` foi acertada na mesma
      linha.
      <br>**ADR-0041:** a seção de "Salva" terminal ganhou a consequência de tela — a nota em "Salva" tem botão,
      as duas listas e o porquê —, e o desativado-com-motivo.
- [x] 10.6 Guarda: `npm test` no dashboard **sozinho**, `openspec validate --all --strict`, e a prova por
      inversão de cada comportamento novo.
      <br>**Guarda:** `npm test` → **197/197** em 19 arquivos (eram 193; +4); `npm run build` verde;
      `openspec validate --all --strict` → **29 passed, 0 failed**. Nenhum arquivo C# foi tocado nesta correção,
      então não houve `dotnet build` a fazer. O `npm test` rodou **sozinho**, como a nota da 9.3 manda.
      <br>**Prova por inversão, uma a uma** (hash conferido depois de cada restauração —
      `DocumentDetail.tsx` volta a `F67AEF59…7EAB` e `cards.ts` a `5E50E979…F4BE`):
      <br>① `isReprocessable` → `isFailure`: caem **os dois** testes da nota em "Salva" (*…com a chave ligada:
      botão habilitado, e o clique reprocessa* e *…com a chave desligada: o botão continua lá, desativado, com a
      explicação*), as duas com *"Unable to find … button … /Reprocessar/"*. É o defeito original, reproduzido.
      <br>② guarda do `onClick` removida (mantendo o tooltip, para isolar a guarda): cai *…com a chave desligada:
      o botão continua lá…* com *"expected 'reprocess' to not be called at all, but actually been called 1 times"*.
      <br>**Achado desta inversão, consertado no teste:** a asserção do clique estava passando por acidente — a
      mutation do React Query não chama o servidor dentro do clique, então `not.toHaveBeenCalled()` logo após o
      `fireEvent.click` passava **mesmo sem a guarda**. O teste ganhou a espera antes da asserção, e só então a
      inversão ficou vermelha.
      <br>③ o motivo trocado por um `title` no botão (o atalho que parece resolver): caem *a nota rejeitada com a
      chave desligada…* e *a explicação alcança o mouse, o teclado e o toque…* — que é exatamente a razão de não
      usar `title`.
      <br>④ `showReprocess` trocado por `reprocessable || chave desligada`: caem *a nota que nunca teve reprocesso…*
      (botão onde não devia haver) e *com o /info ainda sem responder…* (o terceiro estado).
      <br>⑤ `CARD_NOTES.salvas` de volta a *"salvas sem enviar: envio desligado"*: caem os dois testes do
      subtítulo, inclusive a guarda `nenhum deles afirma um estado ao vivo`.
- [x] 10.7 Conferir na tela o botão desativado e o tooltip, nos dois temas.
      <br>✅ **Fechada na conferência de 2026-10-16**, em duas passadas.
      <br>**Primeira passada, aprovada em partes.** O botão desativado e o tooltip estão certos: ele parece
      desativado, o texto diz o que precisa, e o motivo chega pelas três entradas. O único reparo: a dica era
      `position: absolute` dentro do corpo `overflow: auto` do modal, e o modal do detalhe é mais **baixo** do
      que ela — abrir a explicação crescia o `scrollHeight` do corpo e fazia aparecer **barra de rolagem**. Um
      tooltip não mexe no layout de quem o hospeda. Consertado na 10.8.
      <br>**Segunda passada, aprovada.** Com o portal, a dica flutua por cima sem criar barra de rolagem no
      modal baixo, não tapa o "Visualizar JSON", e o contraste do tom `warn` serve nos dois temas.
      <br>**Conferido junto, com a nota em "Salva" real (a captura da 8.4):** com a chave desligada, a nota
      salva mostra o selo "Salva", o motivo gravado e o botão "Reprocessar" **presente e desativado** — antes
      desta fatia ela não tinha botão nenhum; religando a chave, o mesmo botão volta a ficar **habilitado**,
      com hover sem dica. É o defeito do achado 4 provado em tela, e não só por teste. O clique com a chave
      ligada não foi exercido de propósito: ele envia a nota de verdade ao sandbox da plataforma.
- [x] 10.8 **A dica flutua: portal para o `document.body`, em vez de `absolute` no fluxo.** O reparo da 10.7.
      Ela era `position: absolute` dentro do corpo do modal, que é `overflow: auto` (`components/Modal.tsx`), com
      o cartão em `overflow: hidden` — então não flutuava: crescia o `scrollHeight` do corpo e fazia aparecer
      **barra de rolagem** no modal do detalhe, que é mais baixo do que ela.
      <br>**Conserto:** `createPortal` (o `react-dom` já é dependência) para o `document.body`, `position: fixed`,
      posicionada pelo `getBoundingClientRect()` do próprio botão — abaixo e alinhada à direita dele, **acima** se
      não couber abaixo, presa à janela dos dois lados, `zIndex` 60 (o modal é 50). Enquanto aberta, fecha na
      **rolagem** (escutada em **captura**, que é como se pega a rolagem do corpo do modal) e no **resize**: uma
      dica ancorada num botão que se moveu é pior do que uma dica que fechou. As três entradas de abertura (hover,
      foco, clique/toque), o `role="tooltip"`, o id, o `aria-describedby` e o texto não mudaram.
      <br>**Evidência — o teste novo:** `a dica sai do detalhe e vai para o body, fixa na janela`, que asserta a
      **estrutura** (a dica NÃO é descendente do container do detalhe; o pai dela é o `body`; `position: fixed`), e
      não só o texto — um teste de texto passaria com e sem o portal. Mais
      `rolando, a dica fecha em vez de ficar apontando para onde o botão já não está` e
      `clicar na dica não fecha o modal que a hospeda` (o portal muda o lugar no DOM, não na árvore React: o
      clique continua subindo até o `stopPropagation` do cartão — conferido, e não presumido; o caso ainda confere
      que o X do modal fecha, para não passar por vacuidade). Os testes que já existiam não foram afrouxados.
      <br>**Prova por inversão:** tirando o `createPortal` (a dica volta a renderizar no fluxo, `absolute`), cai
      **o teste nomeado** — `a dica sai do detalhe e vai para o body, fixa na janela`, com
      *"AssertionError: expected true to be false"* no `container.contains(dica)`. Restaurado e conferido por
      hash: `DocumentDetail.tsx` volta a `0430DFAA…27E5`.
      <br>**Guarda:** `npm test` **sozinho** → **200/200** em 19 arquivos (eram 197; +3); `npm run build` verde;
      `openspec validate --all --strict` → **29 passed, 0 failed**. Nenhum arquivo C# tocado.
      <br>**Spec e design acertados junto:** a requirement do reprocesso ganhou *"a explicação MUST flutuar por
      cima, sem mexer no layout de quem a hospeda"* e o cenário `A explicação flutua, e não empurra o que está em
      volta`; a §7 do design descreve o portal e o porquê, e a pergunta em aberto do tooltip registra a releitura
      pendente.
