A ordem vai do motor para a tela:

- **Grupo 1, o rebobinamento.** Não depende do resto, e começa pela reprodução.
- **Grupos 2 e 3, o backend.** A derivação e a guarda da seção `poll` entram antes da saída do `Realtime`, para o
  `/info` já nascer derivado.
- **Grupo 4, a tela.**
- **Grupos 5 e 6, a documentação e a prova manual do critério de saída.**

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo do dashboard termina com o
`npm run build`.

## 1. Rebobinamento: reproduzir, decidir, corrigir (D5, `change-feed-polling`)

A causa do D5 é hipótese. As tarefas 1.2 e 1.3 decidem o que o grupo implementa, e a 1.4 é o portão: nada do 1.5 em
diante começa antes dela.

- [x] 1.1 Dar ao cursor falso do `ChangeFeedPollerTests` um jeito de apagar a linha de um tenant, e de apagá-la durante
  a leitura de uma página do feed falso, antes do avanço da marca. O `TryAdvanceWatermarkAsync` do falso passa a recusar
  quando a linha não existe, como o SQL. Sem isso, o resultado no falso não diz nada sobre o SQL
- [x] 1.2 Teste de reprodução: **cursor apagado entre passadas**. A primeira passada parte do `startFrom` e enfileira A,
  assentado. O cursor é apagado, e a passada seguinte deve enfileirar A de novo, com zero suprimidas. Rodar contra o
  código de hoje e anotar se passa ou falha. A hipótese prevê que passa
- [x] 1.3 Teste de reprodução: **cursor apagado no meio da primeira passada**, antes do primeiro avanço. A passada
  termina sem gravar a marca, e a seguinte deve partir do `startFrom` e enfileirar de novo as referências da primeira
  página, com zero suprimidas. Rodar contra o código de hoje e anotar. A hipótese prevê que falha
- [x] 1.4 **Portão.** Escrever no D5 do design o resultado de 1.2 e 1.3 e seguir a linha correspondente da tabela do D5:
  - **1.2 passa e 1.3 falha:** a hipótese se confirma. O D5 passa a dizer "confirmada", com os nomes dos testes, e a
    spec delta fica como está. Seguir para o 1.5;
  - **1.2 falha:** parar o grupo. Investigar a causa, reescrever o D5 com ela e ajustar a spec e as tarefas deste grupo
    pelo `/opsx:update`, antes de qualquer correção. A regra do cursor ausente só fica se for ela que corrige a causa
    achada;
  - **os dois passam:** a hipótese é desmentida. Parar o grupo e reproduzir contra o store SQL, com um teste de
    Infrastructure do poller sobre o `SqlChangeFeedCursorStore`. Levar o que se achar ao D5, à spec e às tarefas pelo
    `/opsx:update`. Se nada reproduzir, tirar da spec o caso do cursor ausente e o cenário "Cursor sem marca", deixar
    os testes 1.2 e 1.3 como prova de comportamento e manter o item do STATUS aberto, com o que foi tentado.

  As tarefas 1.5 a 1.7 valem só para o primeiro caso
- [x] 1.5 Teste primeiro: **cursor sem marca**. O registro tem pares do tenant-a, e o cursor existe sem `Watermark`,
  recriado por uma falha registrada. A passada seguinte descarta o registro e enfileira de novo. E, no
  `ChangeFeedPublicationLogTests`, o descarte explícito esquece os pares e a última marca vista só do (tenant, origem)
  pedido, e as outras partições ficam
- [x] 1.6 Implementar:
  - o descarte explícito no `ChangeFeedPublicationLog`;
  - no `ChangeFeedPoller`, o cursor relido sob o lease desce para o `PullAsync`. Se ele é nulo ou tem `Watermark`
    nula, o registro do (tenant, origem) é descartado antes do `StartAsync` e do `BeginPull`;
  - a regra da regressão no `BeginPull` fica como está, e o `Watermark_rewind_republishes_everything_in_the_reread_window`
    continua verde.

  O 1.3 passa a passar
- [x] 1.7 Registrar no item do STATUS o caminho confirmado, com os nomes dos testes
- [x] 1.8 `dotnet build` com 0 warnings e `dotnet test` verde

## 2. Leitura derivada e guarda da seção `poll` (D4, D8, `automatic-integration`)

- [x] 2.1 Teste primeiro (Application.Tests): uma leitura do `ChangeFeedPollSettings` que não lança. Casos:
  - settings válidas com a seção, e sem a seção;
  - JSON inválido;
  - `poll.enabled = "sim"`;
  - `poll.overlapSeconds = 0`.

  O inválido dá "não lido", sem exceção
- [x] 2.2 Teste primeiro: a derivação do estado da integração automática, a partir do perfil e das origens que varrem.
  É verdadeira só com `Dynamics365` numa origem que varre e `poll.enabled = true`. Os casos falsos:
  - sem perfil;
  - `enabled = false`;
  - sem a seção;
  - `iScala` com `poll.enabled = true`;
  - settings ilegíveis
- [x] 2.3 Implementar as duas em `Application/Inbound`, puras, sem porta nova
- [x] 2.4 Teste primeiro (`ConnectorProfileServiceTests`): a guarda julga o que a gravação escreve na seção `poll`
  (D8).
  - **Recusa do valor novo:**
    - `enabled` mudado para `"sim"` dá `Invalid`;
    - `overlapSeconds` mudado de 300 para 0 dá `Invalid`;
    - um `poll` que deixa de ser objeto dá `Invalid`.

    A mensagem nomeia o campo, o que veio e a forma aceita. Não há escrita no cofre, nem upsert, nem aviso aos
    observadores.
  - **O inválido já gravado não tranca:** com `overlapSeconds = 0` gravado, uma gravação que troca um segredo, liga o
    `enabled` e devolve o `overlapSeconds` igual dá `Saved`. O segredo vai ao cofre, e o `overlapSeconds` continua 0.
  - **Outro adapter, ou sem perfil gravado:** todo campo da seção é julgado.
  - **Sem seção:** continua `Saved`.
  - **Seção válida:** chega intacta ao perfil. Com
    `{"enabled": true, "intervalSeconds": 300, "startFrom": "2015-01-01T00:00:00Z"}`, os três campos ficam, junto com
    a URL, as empresas e as referências de segredo
- [x] 2.5 Implementar a guarda no `ConnectorProfileService.SaveAsync`, junto das outras validações que rodam antes de
  qualquer escrita:
  - comparar a seção `poll` do pedido com a gravada do mesmo adapter, pela regra do `StoredFor`;
  - juntar numa seção só os campos introduzidos ou mudados;
  - passá-la pelo mesmo `ChangeFeedPollSettings.Parse` do poller.

  A mensagem é montada pelo serviço, e não é o texto da exceção
- [x] 2.6 `dotnet build` com 0 warnings e `dotnet test` verde

## 3. O `Realtime` sai, e o `/info` deriva (D1, D3, D4, D10, `automatic-integration`)

- [x] 3.1 Tirar o `Realtime`:
  - do `TenantConnectorProfile`;
  - do `ConnectorProfileRequest`, inclusive do `ToString`;
  - do `ConnectorProfileView`;
  - da cópia no `ConnectorProfileService`.

  Atualizar o comentário do `TenantConnectorProfile`, que fala em "se tem tempo real"
- [x] 3.2 Tirar o `Realtime` do `ConnectorProfileRow`, do `SqlConnectorProfileStore` e do seed dos dois tenants. Sai
  junto o comentário "iScala deste cliente não faz evento". O seed do tenant-a continua com `poll.enabled = false`
- [x] 3.3 Gerar a migração `RemoveConnectorProfileRealtime`:
  - o `Up` remove a coluna;
  - o `Down` a recria como `bit NOT NULL DEFAULT 0`;
  - o snapshot acompanha
- [x] 3.4 Ajustar os testes que constroem o `TenantConnectorProfile` com `Realtime`. São os adapters D365 e Avalara, o
  `ChangeFeedPollerTests`, o `ConnectorProfileServiceTests`, o `InboundSourceResolverTests`, o
  `SupportTicketServiceTests`, o `SqlConnectorProfileStoreTests` e o `DiscoveryToPipelineTests`. O round-trip do store
  SQL passa a conferir o perfil sem o campo
- [x] 3.5 Teste (Application.Tests), com as opções JSON da Web:
  - um corpo de `PUT` com `realtime: true` desserializa no `ConnectorProfileRequest` sem erro, e o campo é ignorado;
  - o `ConnectorProfileView` serializado não tem a propriedade `realtime`
- [x] 3.6 Host: o `/info` passa a devolver `{ environment, automaticIntegration }`.
  - **O cálculo:** vem da derivação do 2.2, com as origens tiradas de `IEnumerable<IDocumentChangeFeed>` resolvido no
    escopo da requisição.
  - **O que não muda:** o endpoint continua sem exigir papel.
  - **O que sai:** o `realtime`
- [x] 3.7 `dotnet build` com 0 warnings e `dotnet test` verde

## 4. A tela (D2, D3, D6, D9, `automatic-integration`)

- [x] 4.1 `types.ts` e `client.ts`:
  - o `ConnectorProfile` e o `ConnectorProfileRequest` perdem o `realtime`;
  - o tipo do `/info` passa a `{ environment: string; automaticIntegration: boolean }`
- [x] 4.2 `adapterSchemas.ts`: marcar o `Dynamics365` como adapter que varre, no próprio schema. O formato fica para a
  implementação (D3). Atualizar o comentário, que diz "Os campos que a tela não mostra (companies, poll…)"
- [x] 4.3 `ConnectorsPage`:
  - **O interruptor:** "Integração automática", só quando o adapter escolhido varre, com a frase do D6 embaixo.
  - **Estado:** vem de `inboundValues.poll?.enabled === true`.
  - **Mudança:** grava um booleano JSON em `poll.enabled` e preserva o resto da seção, sem passar pelo `setPath`.
  - **Envio:** o `PUT` não leva mais o `realtime`.
  - **Trocar de ERP:** para um adapter que não varre, o interruptor some. As settings vazias, que a troca já produz,
    não ganham seção `poll`
- [x] 4.4 `App.tsx`: `info?.automaticIntegration ?? false`, e o selo passa a dizer "Integração automática ligada"
- [x] 4.5 `npm run build` verde, com o `tsc --noEmit` sem erro

## 5. Documentação (D11)

- [x] 5.1 Escrever `docs/adr/0029-integracao-automatica-e-o-poll.md`, pelo template. O cabeçalho diz "Revisa:
  ADR-0019". Acrescentar a linha no índice do `docs/adr/README.md` e a nota de revisão no ADR-0019, no ponto que lista
  o `realtime` entre os campos comuns
- [x] 5.2 `docs/RUNNING.md` §6:
  - **Passo 1:** o SQL continua para o que não tem tela (`pageSize` de 20, `startFrom` em 2015), e passa a gravar
    `enabled: false`. Ligar é pela tela, em Conectores.
  - **Desligar:** passa a ser pela tela.
  - **Rebobinar:** apagar o cursor reprocessa sem reiniciar. Anotar que, se o `DELETE` cair no meio de uma passada, o
    log diz "lease perdido", e isso é esperado.
  - **Menções antigas:** conferir o §8, passo 1, e qualquer menção a "tempo real" ou `realtime`
- [x] 5.3 `docs/STATUS.md`:
  - **As duas lacunas de 2026-09-27:** o interruptor decorativo e o rebobinamento. Marcar `[x]` só com a evidência dos
    testes do grupo 1 e da prova manual do grupo 6. Se o portão 1.4 desviou o grupo, o item do rebobinamento fica
    aberto e anotado com o que se achou.
  - **Registrar como próximo passo:** as opções do coletor na tela ("ligar abre as opções"). Isso inclui corrigir por
    ali um campo da seção `poll` quebrado por SQL (D8).
  - **O que fica onde está:** o registro de publicação persistido segue na fatia de nuvem
- [x] 5.4 `docs/STATUS.md`: abrir o item **"O rótulo 'Tempo real' da lista de grupos"** (D12), com a distinção escrita:
  - o `RealTime` do modo de integração diz como o documento entrou, e não se o conector roda sozinho. É a mesma palavra
    para conceitos diferentes;
  - renomear mexe em contrato: o valor gravado em `Trigger`, o modelo do `IDocumentQueries`, o rótulo e o valor padrão
    da `GroupsPage`. Fica para outra fatia.

  Usar o formato **Falta / Prova / Sintoma** dos outros itens

## 6. Prova manual do critério de saída

**Como o `host.log` da prova de 2026-09-27 foi lido.** É a saída do `dotnet run` por `Tee-Object`, sem relógio, e as
linhas citadas são as do arquivo convertido para UTF-8.

- **O relógio é o tick.** A cada 15 segundos o poller lista os perfis por adapter. Só com o poll ligado ele lê o cursor
  do tenant.
- **O que marca a gravação do perfil:** o `UPDATE [ConnectorProfiles] SET [InboundSettings]`, que é o `PUT /connector`.
- **O que marca o cursor que não existia:** o `INSERT INTO [ChangeFeedCursors]`.
- **O que o log mostra do processo:** foi um só, o `dotnet run` de PID 87012 (uma única "Application started", linha
  80), e não houve linha `fail:`. O arquivo foi relido até as 23:12 (linha 15450), e cada leitura continua a anterior.

- [x] 6.1 Preparar: `docker compose up -d` e `az login`. Rodar o SQL do RUNNING §6, passo 1, com `enabled: false` e
  `startFrom` em 2015. Subir o host e o dashboard e entrar como Admin do tenant-a. A barra lateral não mostra o selo
  - **O log sustenta:**
    - a migração `RemoveConnectorProfileRealtime` aplicada (linha 35);
    - o coletor desligado e calado até a primeira gravação: 33 ticks sem leitura do cursor e sem linha do feed (linhas
      92 a 1413);
    - o D365 autenticando com a credencial do tenant (linha 1587).
  - **O selo ausente:** conferência visual do usuário, sem linha de log.
- [x] 6.2 Pela tela, sem tocar no banco, ligar e salvar:
  - em até 15 segundos o log registra a passada, com as 14 referências na fila de descoberta;
  - o selo "Integração automática ligada" aparece sem recarregar
  - **O log sustenta o "ligar":**
    - **A gravação:** linha 1439.
    - **O tick seguinte lê o cursor:** linha 1502, ou seja, em até 15 segundos.
    - **O cursor nasce do `startFrom`:** linha 1569.
    - **A passada:** "1 tenant(s) consultado(s), 14 referência(s) na fila de descoberta, 0 suprimida(s)" (linha 1830).
    - **A esteira até a plataforma:** 5 envios ao sandbox da Avalara (`taxcompliance/v2/fiscal/dfe`, linhas 1985 a
      2425), todos recusados com HTTP 400.
  - **O selo aparecendo sem recarregar:** conferência visual do usuário, sem linha de log.
- [x] 6.3 Antes e depois de salvar, conferir pelo `GET /connector` que `companies`, `pageSize`, a seção `poll` (fora o
  `enabled`) e os `establishments` ficaram iguais (guarda do RUNNING §8)
  - **Conferência visual do usuário, sem linha de log.** O log não registra o corpo do `GET /connector`.
- [x] 6.4 Desligar pela tela e salvar:
  - em até 15 segundos o log do feed fica em silêncio para o tenant-a, sem passada, falha ou aviso de configuração;
  - o selo some
  - **O log sustenta o desligar**, no mesmo processo (o `dotnet run` de PID 87012):
    - **A gravação:** leitura do perfil e `UPDATE` (linhas 14207 a 14222), com a tela recarregando em seguida (14229 e
      14234).
    - **Em até 15 segundos:** o tick anterior à gravação ainda consulta o tenant (linha 14179). O primeiro tick depois
      dela já não consulta (14241).
    - **O silêncio:** dali até o fim da leitura (linha 14624, às 23:02), 24 ticks listam o tenant sem consultá-lo, e não
      há linha do feed, `warn:` nem `fail:`.
  - **O banco, lido às 23:00 e às 23:02, confirma:** o `poll` gravado é
    `{"enabled":false,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}`. Desligar manteve a
    seção inteira, e o cursor ficou de pé, com a marca em 22:55:24.
  - **O selo sumindo:** conferência visual do usuário, sem linha de log.
  - **Sobre o primeiro desligado do log:** o trecho das linhas 1832 a 2601 veio de fora da tela, sem gravação do perfil
    pelo host. Ele não conta para esta tarefa.
- [x] 6.5 Ligar pela tela e salvar: a passada volta, retomando da marca, sem reenfileirar o histórico
  - **O log sustenta o religar**, no mesmo processo:
    - **A gravação:** as leituras do perfil e o `UPDATE`, nas linhas 15278 a 15293.
    - **Em até 15 segundos:** o último tick antes dela ainda não consulta o tenant (linha 15268). O primeiro depois
      dela já consulta (15336).
    - **O cursor não é recriado:** não há `INSERT INTO [ChangeFeedCursors]` depois da gravação, e o `StartAsync` achou a
      linha com a marca.
    - **A passada:** "1 tenant(s) consultado(s), 0 referência(s) na fila de descoberta, 0 suprimida(s)" (linha 15426),
      sem `warn:` nem `fail:`.
  - **Por que isso é retomar da marca, e não do `startFrom`.** A URL da consulta ao F&O sai redigida no log
    (`FSFiscalDocumentBRs?*`), e o `since` não aparece. Mas a leitura a partir do `startFrom` de 2015 devolveu as 14
    referências nas duas vezes anteriores (linhas 1830 e 2944), e esta devolveu 0: o que ela leu foi só a janela da marca
    menos a sobreposição, e o histórico não voltou para a fila.
  - **O banco, lido às 23:13, confirma:**
    - o `poll` gravado é `{"enabled":true,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}`,
      com a seção preservada;
    - a marca avançou de 22:55:24 para 23:12:58 (horário local), com 0 falhas.
  - **As tentativas anteriores não serviram:**
    - até as 23:07 (linha 14971), nenhum `PUT` do religar tinha chegado ao host;
    - o religar da linha 2647 encontrou o cursor apagado, e partiu do `startFrom`.
- [x] 6.6 Apagar o cursor (`DELETE FROM ChangeFeedCursors WHERE TenantId = 'tenant-a'`), com o host de pé: a passada
  seguinte redescobre as 14 referências, com zero suprimidas, sem reiniciar o processo
  - **Sustentado.**
    - **O `DELETE`:** foi fora do host e não aparece no log.
    - **O efeito dele:** o host recria o cursor na linha 2724, e o `INSERT` só acontece quando a linha não existe.
    - **A redescoberta:** a passada seguinte, no mesmo processo, dá "14 referência(s) na fila de descoberta, 0
      suprimida(s)" (linha 2944).
    - **A variante:** foi a do `DELETE` entre passadas, com o coletor desligado no meio. É a que já funcionava antes da
      correção (1.2). O caminho corrigido, o do `DELETE` no meio da passada, só tem prova por teste.
    - **Efeito colateral:** a redescoberta reenviou as 5 NF-e 55 ao sandbox (linhas 3057 a 3483, todas com HTTP 400).
- [x] 6.7 Registrar os resultados, com as linhas de log, no STATUS (5.3)
