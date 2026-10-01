## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido, incluindo onde a leitura refinou uma
premissa do pedido.

- **O cursor já guarda o diagnóstico, e ninguém o lê.**
  - **O que o `ChangeFeedCursor` tem:**
    - `LastPolledAt`;
    - `ConsecutiveFailures`;
    - `LastError`, cortado em 500 caracteres no `SqlChangeFeedCursorStore`;
    - `NotBefore`;
    - `Watermark`.
  - **Quem lê:** nenhum endpoint. O único jeito de ver é o `sqlcmd` do RUNNING §6.
- **A marca só sobe.** O `TryAdvanceWatermarkAsync` grava com `WatermarkTicks < @alvo` e com o `EXISTS` do lease, numa
  instrução só (fencing, ADR-0024). Não há método que a abaixe. Hoje, rebobinar é SQL: apagar o cursor ou fazer o
  `UPDATE` do `WatermarkTicks` (RUNNING §6).
- **A regra do rebobinamento já existe, e é por réplica.** O `ChangeFeedPublicationLog.BeginPull` zera os pares quando a
  marca lida é menor que a `LastWatermark` da partição, que é a última marca vista por aquela réplica. O `Forget` cobre
  o cursor ausente ou sem marca (change `automatic-integration-switch`, D5). Os dois estão em memória, por processo.
- **O lease.** O recurso é `changefeed:{origem}:{tenant}`, e o dono é o `OwnerId` da réplica. O `TryAcquireAsync`
  devolve falso quando outro dono tem o lease válido, e o coletor, nesse caso, pula o tenant na passada (`LeasesBusy`),
  sem contar falha.
- **O que a idempotência barra, e o que custa.**
  - **O que ela barra:** o `AlreadyProcessedAsync` barra só a linha `Submitted` ou `Confirmed` com o mesmo hash.
  - **O que vem antes:** o `DocumentPipeline` busca antes de conferir, porque é o cru que decide o hash.
  - **O custo da busca:** o `D365GoodsInvoiceSource.FetchAsync` faz 4 GETs fixos (o cabeçalho, as linhas, os impostos e
    os encargos). Pode fazer mais um, o do voucher, quando há ponte de complemento, e os endereços, que passam por
    cache.
  - **O que não é buscado:** o `DocumentRouter` ignora a NFS-e e o CT-e pelo tipo, antes de qualquer busca. A NF-e fora
    de escopo (não autorizada) para no cabeçalho.
  - **A premissa refinada:** o pedido diz que a nota ignorada "reprocessa e reenvia". A NFS-e ignorada volta a ser
    registrada como ignorada, sem busca e sem envio. Só a NF-e ignorada por status pode virar envio, se agora estiver
    autorizada. O texto da confirmação diz isso (D8).
- **O `startFrom` também não aparece.** Ele fica na seção `poll` do perfil, e o coletor o lê pelo
  `ChangeFeedPollSettings.StartFrom`, só quando o cursor não tem marca. Nenhuma tela o mostra. A investigação de 29/09
  foi causada pela ausência dele: nenhum erro, a marca avançando e zero documento (STATUS, "O `startFrom` que some").
- **A marca é a data de alteração.** A descoberta filtra pelo `SysModifiedDateTime`. Rebobinar para 01/09 relê as notas
  **alteradas** desde 01/09, e não as emitidas.
- **A Avalara tem duas credenciais.**
  - **Onde ficam:** as settings de saída têm as seções `sandbox` e `production`, cada uma com Client ID e Client Secret.
  - **O cache e a recusa:** são por tenant, ambiente, endpoint, cliente e a impressão do segredo.
  - **O esquecer:** o `Forget(tenant)` esquece tudo do tenant, e roda no salvar do perfil, pelo
    `IConnectorProfileObserver`.
- **O D365 tem um token com cache, e um atalho em desenvolvimento.**
  - **O cache:** o `ClientCredentialsD365TokenProvider` guarda um `ClientSecretCredential` por (tenant do Entra, app,
    impressão do segredo). O Azure.Identity guarda o token em cache na instância da credencial, e renova antes de
    vencer. Quem pede token por essa instância recebe o token em cache enquanto ele vale, que é de cerca de uma hora,
    sem ir ao Entra ID.
  - **O atalho:** em Development, o `D365DevelopmentTokenProvider` cai no Azure CLI quando o perfil não tem o segredo.
  - **A leitura vazia:** o comentário do `HeaderAsync` registra que, entre empresas, uma leitura vazia pode ser falta de
    acesso à empresa.
- **A tela de Configurações.** É a `ConnectorsPage`, com as abas "Entrada (ERP)" e "Saída (compliance)".
  - **O campo de segredo:** é `type="password"`, com `value={typed[...] ?? ''}`, o placeholder "digite para trocar" e o
    texto "configurado em <data>" embaixo.
  - **O envio:** o `withTyped` só manda o segredo digitado.
  - **Os textos:** a ajuda sob o interruptor e a mensagem "Perfil salvo. Novas integrações…" são os textos que saem.
- **O perfil tem três JSONs, e a tela os zera na troca de adapter.** O `changeInbound` começa de `{}` quando o ERP
  escolhido não é o gravado (o achado das settings em `{}`, no STATUS). Um campo que precise sobreviver à troca de
  adapter não pode morar nesses JSONs.
- **Leituras e papéis.** O `/info` é lido por qualquer papel, e é dele que a barra lateral tira o selo. O `/connector` é
  só para Admin. Os papéis são `Admin` e `Viewer`.
- **O front não tem teste.** O `package.json` não tem script nem dependência de teste.

## Goals / Non-Goals

**Goals:**

- O rebobinamento pela tela é o mesmo mecanismo do coletor, e não um segundo:
  - o mesmo lease;
  - o mesmo fencing;
  - a mesma regra de regressão do registro de publicações.
- O teste de credencial tem uma porta só na Application, com o freio e as regras da resposta num lugar só. Cada adapter
  só sabe falar com a ponta dele.
- A prova de que salvar sem digitar não destrói o segredo vem de um teste do que a tela envia, e não só do servidor.

**Non-Goals:**

- **Generalizar o painel para outro coletor.** Só o feed do D365 varre hoje. O painel lê o cursor da origem do adapter
  de entrada, seja ela qual for, mas não há um segundo coletor para provar a generalização.
- **Auditoria persistida do rebobinamento.** Fica uma linha de log. Uma trilha de auditoria em banco é outra fatia.

## Decisions

### D1. A navegação: o bloco "Integrações" e os lugares reservados

A barra lateral troca o bloco "Operação" (Documentos e Integrações) pelo bloco "Integrações".

| Sub-bloco | O que abre | Quando aparece |
|---|---|---|
| Fiscal | a tela de documentos de hoje (a view `documents`) | quando o tenant tem o `Fiscal` |
| Contábil | o painel reservado | quando o tenant tem o `Contabil` |
| Inventário | o painel reservado | quando o tenant tem o `Inventario` |
| Agendamento | a tela de integração manual de hoje (a view `integrations`) | sempre |

**Contábil e Inventário são lugares reservados, com um painel vazio. Não são um filtro da tabela de documentos fiscais:
são outro domínio, com outro modelo, outras portas e outros adapters, e cada um vira uma fatia própria. Esta fatia não
entrega integração contábil nem de inventário.** Quem ler a barra lateral com o Contábil marcado não deve concluir que
existe integração contábil. O painel reservado diz isso com todas as letras.

**A integração automática continua só Fiscal.** Contábil e Inventário serão cargas manuais pelo Agendamento. É decisão
de produto, e não limitação: o coletor por marca d'água poderia varrer outra entidade, mas o produto não quer isso para
esses dois domínios. Por isso o Agendamento aparece sempre, e o interruptor e o painel do coletor ficam no Fiscal.

- **A tela de entrada:** é o Fiscal. Sem ele, o primeiro módulo que o tenant tem, ou o Agendamento.
- **Enquanto o `/info` carrega:** a barra mostra o que mostrava antes da resposta, e a tela de entrada é o Fiscal. Um
  tenant sem o Fiscal vê a troca quando a resposta chega. É o mesmo tratamento do selo.
- **Alternativa considerada:** um seletor de módulo no topo da tela de documentos. Foi descartada, porque faria o
  Contábil parecer um filtro do Fiscal, que é justamente a leitura a evitar.

### D2. Os módulos no perfil de conector, numa coluna própria

O `TenantConnectorProfile` ganha os `Modules` (`IReadOnlyList<string>`), gravados numa coluna nova e anulável da
`ConnectorProfiles`: `Modules`, `nvarchar(200)`, com a lista em JSON (`["Fiscal","Inventario"]`).

- **O padrão:** a coluna nula quer dizer `["Fiscal"]`. A migração não preenche nada.
- **As regras num lugar só:** os valores aceitos, a ordem canônica, o padrão e a validação ficam num tipo só da
  Application (`TenantModules`). A gravação e o `/info` passam por ele.
- **Por que uma coluna, e não um dos JSONs:** as settings são por adapter, e a tela as zera quando o adapter muda
  (Context). Os módulos sumiriam numa troca de ERP. O perfil continua sendo o lugar certo, porque já é por cliente e é
  ele que a tela de Configurações grava.
- **Por que não o cadastro do tenant (`TenantRow`):** o cadastro guarda o nome, o CNPJ e se está ativo, e tem outra
  tela. O pedido é que os módulos fiquem no perfil do tenant, gravados pela mesma tela que grava o resto das
  Configurações.
- **Por que recusar a lista vazia:** a ausência já quer dizer "só o Fiscal". Uma lista vazia seria um segundo estado,
  "nenhum módulo", sem significado de produto, e com uma barra que só mostra o Agendamento. Recusar tira a ambiguidade
  entre `null` e `[]`.
- **Um cliente antigo:** um `PUT /connector` sem `modules` mantém o gravado, como o segredo ausente mantém a referência.
- **A leitura:** o `/info` responde `modules` para qualquer papel, e o `GET /connector` também, para o formulário.

**Isso é apresentação, e não permissão: a API continua respondendo para um módulo escondido. Restringir de fato é outra
fatia.** Um tenant sem o Fiscal ainda recebe o `/groups`, o `/documents`, o `/reading` e o resto, com as mesmas regras
de papel e de tenant. A barra lateral é a única coisa que muda.

### D3. O segredo mascarado: placeholder, e nunca valor

O campo de um segredo configurado passa a mostrar a máscara `••••••••` como placeholder, com "configurado em <data>"
embaixo. O valor do campo continua `typed[...] ?? ''`.

**ATENÇÃO: o mascarado tem que ser PLACEHOLDER, nunca o valor do campo. Se os asteriscos virarem valor, o próximo salvar
grava a string de asteriscos no cofre e destrói o segredo. A regra não muda: campo de escrita pura, enviado só quando o
usuário digita. Com teste que prove que um salvar sem digitar nada preserva o segredo.**

- **O tamanho da máscara:** fixo, de 32 caracteres, mais ou menos a largura de um Client ID na tela. Foram 8 até a
  prova manual (2026-10-01). Um tamanho que acompanhasse o segredo vazaria o comprimento dele.
- **Visível fora de foco** (revisado no D14): o MUI esconde o placeholder enquanto o rótulo está dentro do campo. O rótulo
  do segredo fica sempre recolhido, e a máscara aparece sem o campo em foco.
- **Um segredo não configurado:** o campo fica sem máscara, com "não configurado".
- **O envio:** a montagem do payload do perfil sai da `ConnectorsPage` e vai para um módulo puro. A regra do
  `withTyped` vai junto e não muda: só entra o segredo digitado e não vazio.
- **A defesa no servidor:** a `ConnectorProfileService` recusa, na validação, antes de qualquer escrita, um campo de
  escrita feito só de `*`, `•`, `●` e `∗`, com ou sem espaços. A mensagem nomeia o campo e não repete o valor. Se um
  dia um cliente mandar a máscara, a gravação falha alto, e o segredo não é destruído em silêncio.
- **O custo aceito:** um segredo real feito só desses caracteres é recusado.
- **A prova, nos dois lados:**
  - **o que a tela envia:** um teste do módulo puro. Com um perfil carregado, com o segredo configurado e nada
    digitado, o payload não tem `clientSecret` nem nenhum caractere de máscara. Com uma mudança só na URL base, também
    não;
  - **o que o servidor faz:**
    - o teste que já existe, `Absent_write_field_keeps_the_stored_reference_without_touching_the_vault`, prova que o
      campo ausente preserva o segredo;
    - um teste novo prova que a máscara como valor é recusada sem tocar o cofre e com a referência intacta.

### D4. A primeira suíte de testes do front: `vitest`

O teste da tela (D3) pede um runner no dashboard, que hoje não tem nenhum. Entra o `vitest`, como dependência de
desenvolvimento, com o script `npm test`.

- **O que é testado:** só módulos puros, sem DOM e sem React Testing Library. A dependência fica mínima, e o teste
  cobre exatamente a regra: o que o payload leva.
- **Por que o `vitest`:** usa a mesma configuração do Vite, sem config extra, e não entra no bundle.
- **Alternativas consideradas:**
  - **só o teste do servidor:** prova que o campo ausente preserva o segredo, mas não que a tela manda o campo ausente.
    A regra que pode quebrar é a da tela;
  - **Playwright:** exigiria o host e o navegador de pé, e é pesado demais para uma função pura.

### D5. Os textos de Configurações

- **A mensagem de sucesso:** "Configurações salvas com sucesso".
- **O texto sob o interruptor:** o "Busca sozinha as notas novas no ERP…" sai. O painel (D6) ocupa o lugar e diz o que
  o coletor fez, que é mais útil do que descrever o que ele faria.

### D6. O painel: uma leitura do cursor, só para Admin

- **Onde fica:** em Configurações, sob o interruptor, na aba do ERP.
- **Quando aparece** (revisado no D14): assim que o interruptor é ligado na tela, com um adapter que varre, sem esperar o
  salvar. A primeira versão seguia a integração gravada, pela regra do selo. Na prova manual, isso obrigava a ligar,
  salvar e só então ver as opções.
- **A API:** `GET /connector/automatic`, com `RequireRole("Admin")` e o tenant do usuário logado.
  - **Quem responde:** uma leitura na Application (`Inbound`). Ela usa o `IConnectorProfileStore`, o
    `IChangeFeedCursorStore` e as origens que o host varre, as mesmas que o `/info` já passa ao `AutomaticIntegration`.
  - **A origem:** é o adapter de entrada do perfil. A origem do feed casa com o nome do adapter (ADR-0025).
  - **O que responde:**
    - 404 quando o adapter não varre;
    - 200 com `cursor: null` quando não há cursor;
    - 200 com os cinco campos do cursor, nos demais casos;
    - em todo 200, o `startFrom` gravado, ou `null`.
- **O `startFrom`, como leitura.** A leitura tira o `startFrom` do perfil pelo `ChangeFeedPollSettings`, o mesmo parser
  do coletor. O painel mostra o que o coletor vai usar, e não uma segunda interpretação do JSON.
  - **Sempre legível:** o painel só aparece com a seção `poll` legível, pela regra do `/info`. Então o `startFrom`
    mostrado é sempre um valor válido, ou a ausência.
  - **Sem marca:** o painel diz de onde a primeira passada vai começar. Com o `startFrom`, dele. Sem, do instante em que
    ela rodar, e o que mudou antes fica de fora.

    É a metade que importa do achado do `startFrom` que some. A investigação de 29/09 foi causada por essa ausência:
    nenhum erro, a marca avançando e zero documento. Com o painel, o ponto de partida aparece antes da passada.
  - **Com marca:** o painel mostra o `startFrom` também, e diz que ele só vale quando a marca não existe.
  - **Por que não editar:** definir o ponto de partida é outra operação, que não é rebobinar. O rebobinamento move a
    marca de um cursor que existe. O `startFrom` decide onde nasce um cursor que ainda não existe. Editá-lo pela tela
    fica fora, e o STATUS registra que ele continua exigindo SQL. Exibir custa quase nada, e fecha a metade que importa.
- **A atualização:** o painel se atualiza a cada 15 segundos (`refetchInterval`), que é o ritmo das passadas.
- **O último erro:** vai à tela como está no banco, até 500 caracteres. É o `ex.Message` que o coletor registra: nossas
  mensagens, a do Azure.Identity e a do cliente OData. Nenhuma leva o token nem o segredo, e o texto já é o do log. O
  painel é só para Admin. A tarefa confere, lendo o código, o que entra no `RecordFailureAsync`.
- **Por que em Configurações, e não numa tela própria:** o painel fica ao lado do interruptor que o liga e do segredo
  que costuma ser a causa do erro. A correção e o diagnóstico ficam na mesma tela.

### D7. O rebobinamento: o lease do coletor, e a regra que já existe

`POST /connector/automatic/rewind`, com `{ "watermark": "<instante ISO com fuso>" }`, com `RequireRole("Admin")` e o
tenant do usuário logado. Um serviço da Application (`Inbound`) faz:

1. **A origem:** resolve a origem pelo perfil. Se o adapter não varre, responde 404.
2. **O lease:** toma o lease `changefeed:{origem}:{tenant}`, com um dono próprio (`rewind:{guid}`) e prazo curto (30
   segundos). Ocupado, responde 409: o coletor está lendo.
3. **A validação, sob o lease:** relê o cursor.
   - sem cursor, ou sem marca, responde 409;
   - um alvo que não é anterior à marca, ou que está no futuro, responde 400.
4. **A gravação:** grava pelo método novo do store, `TryRewindWatermarkAsync`. É uma instrução só:
   `UPDATE … SET WatermarkTicks = @alvo WHERE WatermarkTicks > @alvo AND EXISTS (lease válido do dono)`. É o mesmo
   fencing do avanço, com a comparação invertida. Enquanto o serviço detém o lease, o coletor não consegue avançar a
   marca entre a leitura e a gravação, porque o avanço exige o lease dele.
5. **O log:** "rebobinamento: <usuário> levou a marca do tenant <t> de <antes> para <depois>".
   - **Quem escreve:** o host, e não o serviço. O resultado do serviço leva a marca de antes e a nova, e o endpoint
     escreve a linha com o usuário do token.
   - **Por quê:** a Application não depende de logging. É o mesmo padrão do coletor, que devolve o resumo da passada
     para o worker registrar. Decidido no apply, em 2026-09-29.
6. **O fim:** libera o lease, sempre.

**O caminho da tela passa pela mesma regra do registro de publicações.** O serviço não chama o `Forget`. A passada
seguinte lê a marca rebobinada, menor que a `LastWatermark` da partição, e o `BeginPull` zera o registro, como no
rebobinamento por SQL. É o caminho que o cenário "Rebobinamento da marca" já testa.

- **Por que não chamar o `Forget`:** o registro está em memória, por réplica. O `Forget` só alcançaria a réplica que
  atendeu o HTTP, e não a que vai fazer o poll. A regra de regressão roda na réplica que faz o poll.
- **O teste que confirma:** um teste da Application com o `ChangeFeedPoller`, o `ChangeFeedPublicationLog` real e o
  serviço de rebobinamento:
  1. a primeira passada enfileira A assentado;
  2. o serviço rebobina;
  3. a segunda passada enfileira A de novo, com 0 suprimidas.

  O rebobinamento é feito pelo serviço, e não por uma edição direta no store falso.
- **O que o rebobinamento não mexe:**
  - a última verificação, as falhas, o erro e o throttling;
  - os `ProcessedDocuments`;
  - a seção `poll`.

  A passada seguinte vem no intervalo normal, até 60 segundos no padrão. Zerar a última verificação para antecipá-la
  apagaria o diagnóstico do painel e furaria a regra do intervalo.
- **Por que só para trás:** avançar pularia notas em silêncio. A marca que sobe é do coletor.
- **Por que exigir uma marca:** sem cursor, a primeira passada parte do `startFrom`, pelo caminho do `Forget` para o
  cursor sem marca. Criar o cursor pela tela seria um segundo jeito de a marca nascer, competindo com o `startFrom`.
- **O instante:** a tela usa data e hora no fuso do navegador (`datetime-local`), com o máximo na marca atual. Ela
  manda o ISO com o fuso, e o servidor grava em ticks UTC (ADR-0017).
- **Com a integração desligada:** o rebobinamento vale do mesmo jeito, e a marca rebobinada vale quando for religada. O
  painel só aparece ligada, então isso só é alcançável pela API. A regra fica escrita para não virar um caso sem dono.

### D8. A confirmação: um texto verdadeiro

A confirmação é um modal (o `Modal` que já existe), e não o `window.confirm`, porque o texto tem várias linhas. O texto,
na versão curta da revisão do D14:

> **Buscar novamente desde 01/09/2026 00:00?**
>
> - As notas alteradas no ERP desde essa data serão lidas de novo.
> - Notas já enviadas e sem alteração não serão reenviadas.
> - Notas recusadas, com erro ou ignoradas serão processadas de novo.
> - Cada NF-e lida gera pelo menos 4 consultas ao ERP.
>
> [Cancelar] [Buscar novamente]

Por que cada frase é verdadeira:

| Frase | De onde vem |
|---|---|
| "alteradas" | a descoberta filtra pelo `SysModifiedDateTime`, e não pela data de emissão |
| "enviada ou confirmada, com o mesmo conteúdo" | o `AlreadyProcessedAsync` barra só `Submitted` e `Confirmed` com o mesmo hash |
| "recusada, sem confirmação, na fila de falhas ou ignorada" | `IntegrationError`, `Unconfirmed`, `DeadLettered` e `Ignored` não barram |
| "se couber, enviada" | a NFS-e ignorada volta a ser ignorada; a NF-e ignorada por status só vai se agora estiver autorizada; a recusada na validação é recusada de novo se nada mudou |
| "pelo menos 4 consultas" | o `FetchAsync` faz 4 GETs fixos, mais o do voucher, às vezes, e a busca vem antes da idempotência |
| "cada NF-e" | a NFS-e e o CT-e são ignorados antes da busca e não custam consulta de nota |

- **O que o texto não conta:** a descoberta também custa uma consulta por página, com até o tamanho da página de
  referências. É pequeno perto das 4 por nota, e a linha do custo fica em uma linha, como pedido.
- **Contra a deriva:** o texto mora num lugar só da tela, com um comentário que aponta o `AlreadyProcessedAsync` e o
  `FetchAsync`. Quem mudar a idempotência ou a busca tem de rever a frase.

### D9. O teste de credencial: uma porta, um serviço, um freio

- **A porta:** `IConnectorCredentialTest`, na Application (`Connectors`), implementada nos adapters.
  - **Quem ela é:** tem o nome do adapter e o lado, pelo `ConnectorSettingsKind` que já existe.
  - **O que faz:** `TestAsync(profile, environment, ct)` devolve um `CredentialTestOutcome` com o veredito
    (`Worked`, `Refused`, `Unavailable` ou `Incomplete`) e o motivo.
- **O serviço:** a `ConnectorCredentialTestService`, na Application.
  1. lê o perfil do tenant logado;
  2. escolhe a implementação pelo lado e pelo adapter gravado. Sem implementação, é 400;
  3. consulta o freio;
  4. chama o teste;
  5. registra a recusa no freio;
  6. devolve `{ worked, reason, retryAt? }`.
- **O esquecer:** o serviço é `IConnectorProfileObserver`, e o salvar do perfil esquece o freio do tenant.
- **A API:** `POST /connector/test`, com `{ "side": "inbound" | "outbound", "environment"?: "Sandbox" | "Production" }`,
  com `RequireRole("Admin")`. A requisição não carrega credencial. O teste usa a gravada, lida do cofre no servidor,
  como decidido com o usuário.
- **A resposta:** devolve apenas se funcionou ou não, e o motivo. Nunca o token, nunca o segredo, nunca cabeçalho com
  valor. Revisado no D14: a tela recebe só uma mensagem curta, e o motivo detalhado abaixo vai para o log do host.
  - **Quem escreve o motivo:** o nosso código. O texto da plataforma só entra redigido, pela `TokenExchangeRedaction`
    que o envio já usa na Avalara.
  - **No D365:** entra só o código `AADSTS`, tirado da mensagem por expressão regular. A mensagem inteira do
    Azure.Identity não entra: ela não traz o segredo, mas traz a resposta do Entra inteira, e o código basta.
- **Os testes da resposta:**
  - nos adapters, com um handler HTTP falso que devolve um token conhecido e uma recusa que repete o segredo, o motivo
    não contém nenhum dos dois;
  - no serviço, a resposta serializada tem só os três campos.
- **"Salve antes de testar":** a tela calcula a edição pendente comparando o payload do formulário com o payload do
  perfil carregado, pelo mesmo módulo puro da D3, e olhando se há segredo digitado. O teste do módulo cobre a
  comparação. Com edição pendente, o botão avisa e não chama.
- **Por que na Application:** o freio, a credencial gravada e a forma da resposta são iguais para qualquer adapter. O
  adapter só sabe falar com a ponta dele. Fica na regra hexagonal: a porta na Application e a implementação no adapter.

### D10. O teste da Avalara: um token novo, sem o cache nem a recusa do envio

O `IAvalaraTokenProvider` ganha um `ProbeAsync(settings)`.

1. **A credencial:** resolve pelo mesmo `ResolveAsync`. Sem segredo, o resultado é `Incomplete`.
2. **A troca:** pula o `TryCached` e troca pelo mesmo `FetchAsync` do envio, sob a mesma trava por credencial. O pedido,
   a leitura da resposta e o tratamento da recusa são os mesmos.
3. **Na recusa:** o `FetchAsync` já grava a recusa lembrada da credencial. A recusa do teste vale para o envio (delta de
   `avalara-tenant-authentication`).
4. **No sucesso:** esquece a recusa e os tokens do tenant, e guarda o token novo. É o que o salvar do perfil já faz,
   mais o token.

- **O ambiente:** o teste é por seção. O botão aparece no Sandbox e em Produção, e cada um testa a própria credencial.
  Testar a credencial de Produção antes de trocar o ambiente ativo é o caso de uso.
- **Por que o teste para no token:** o contrato verificado no sandbox só tem o envio e a consulta de status, e os dois
  são sobre um documento. Não há uma leitura sem efeito que tenha sido verificada. Inventar uma chamada, como a consulta
  de um id falso, seria presumir o comportamento da plataforma, e poderia dar um "funciona" falso. A permissão aparece
  no primeiro envio, no detalhe do documento.
- **O adapter `Mock` de saída:** não tem teste, e responde 400. O adapter `Avalara` apontado para o `MockComplianceApi`
  funciona, porque o mock emite token.

### D11. O teste do D365: um token novo do Entra ID, e uma leitura

O teste fica no adapter `Ingress.D365Poll`.

1. **As settings:** lê o `D365InboundSettings` do perfil gravado. Um auth incompleto, ou o segredo ausente no cofre,
   é `Incomplete`, com o campo que falta.
2. **O token, sempre novo:** o `ClientCredentialsD365TokenProvider` ganha um `GetFreshTokenAsync(connection)`.
   - **A resolução:** resolve a credencial pelo mesmo caminho do coletor: a referência do próprio tenant e o valor no
     cofre.
   - **A instância nova:** cria uma instância nova de `ClientSecretCredential`, pela mesma fábrica (`_createCredential`).
     Pede o token com ela, e a descarta. A instância não entra no dicionário `_credentials`.
   - **Por que isso basta:** o Azure.Identity guarda o token em cache na instância da credencial (documentação do
     `ClientSecretCredential.GetTokenAsync`). Uma instância nova começa sem cache, e vai ao Entra ID.
   - **O coletor:** o `GetTokenAsync` dele não muda, com a mesma instância reusada e o mesmo cache.
   - **O Azure CLI:** o teste nunca passa pelo `D365DevelopmentTokenProvider`, e nunca cai no Azure CLI, nem em
     desenvolvimento. Ele responde sobre a credencial gravada, e não sobre o `az login` de quem roda o host.
3. **A recusa do Entra:** a `AuthenticationFailedException` é `Refused`, com o código `AADSTS`.
4. **A leitura:** um GET só, sem seguir `nextLink`:
   `/data/FSFiscalDocumentBRs?$top=1&$select=FiscalDocumentRecId&cross-company=true`. Os desfechos estão na tabela da
   spec.
   - **O 200 vazio:** é `Worked`, com o aviso de que, entre empresas, a leitura vazia não prova o acesso às empresas. A
     falta de acesso à empresa também devolve vazio.
   - **Tempo esgotado, 5xx e 429:** são `Unavailable`.

**Por que o token novo é requisito, e não detalhe.** O botão existe para responder "essa credencial funciona agora".

- **O teste que reusasse o cache mentiria.** Reusar o token do coletor passaria com um segredo revogado no Entra ID até
  cerca de uma hora depois. Isso não seria um limite do teste. Seria um teste que mente, e exatamente para quem está
  tentando decidir se o problema é a credencial.
- **O que o teste prova:**
  - o Entra ID aceita a credencial agora, porque emitiu um token novo;
  - o F&O aceita esse token e dá a permissão de leitura, também agora.

**As alternativas consideradas:**

- **Reusar o `GetTokenAsync` do coletor:** descartada, porque é o teste que mente.
- **O teste montar o próprio `ClientSecretCredential`, fora do provedor:** descartada.
  - **O problema:** duplicaria a regra da referência. É ela que impede um tenant de usar a credencial de outro, com o
    prefixo do tenant (ADR-0027), e ela tem de morar num lugar só.
  - **O que se escolheu:** com o `GetFreshTokenAsync` no provedor, a resolução e a fábrica são as mesmas do coletor. O
    teste do adapter confere, pela fábrica, que cada teste cria a própria instância.

### D12. O freio fica no endpoint, e não no token do coletor do D365

O endpoint de teste ganha um limite por tenant e por adapter, com o mesmo intervalo de 5 minutos da recusa lembrada da
Avalara, e vale igual para a Avalara e para o D365.

- **O que o freio lembra:** só o `Refused`, por 5 minutos.
- **Dentro do intervalo:** o teste devolve o motivo lembrado e o `retryAt`, sem requisição.
- **Um teste que dá certo:** esquece a recusa lembrada da Avalara (D10).
- **O salvar do perfil:** esquece o freio do tenant.
- **O token do coletor do D365 não muda:** sem recusa lembrada, e com o mesmo cache.

**Por que os dois adapters têm proteções diferentes por dentro, e a mesma proteção no botão.**

- **Na Avalara:** o endpoint de token é proprietário, e o risco dele é desconhecido. A recusa lembrada no provedor
  existe para que cada nota em voo não seja uma tentativa de login com a credencial errada.
- **No D365:** o endpoint de token é o Entra ID, cujo comportamento com credencial de cliente é conhecido.
  - **O coletor já é limitado:** pelo `intervalSeconds` de 60, com uma tentativa de token por passada, por tenant.
  - **O token bom fica em cache:** o Azure.Identity o guarda.
  - **O Entra ID não trava aplicação:** o bloqueio inteligente vale para conta de usuário, e não para credencial de
    cliente.
  - **O custo de uma recusa lembrada ali:** só atrasaria a recuperação depois de o administrador corrigir o segredo, e
    exigiria mais código para esquecê-la.
- **O caminho sem limite é o botão:** o coletor tenta uma vez por minuto, e uma pessoa clica quantas vezes quiser. Por
  isso o freio vai no botão, igual para os dois.

Projetar para o risco real, e não para a simetria.

**O segundo motivo do freio: cada teste é um pedido real ao emissor do token.** Com o token novo a cada teste (D10 e
D11), nenhum clique é absorvido por um cache. No D365, cada clique é uma instância nova de credencial e um pedido ao
Entra ID, exatamente o padrão que a orientação do Azure.Identity pede para evitar, porque leva o Entra ID a responder
429. O coletor continua reusando a instância, e é por isso que ele não precisa de freio. O botão não reusa de propósito,
e por isso precisa.

**O painel reduz ainda mais o valor de proteger contra a sondagem continuada.** Com o painel mostrando o último erro, um
segredo errado do D365 fica visível em cerca de um minuto: a passada seguinte, no intervalo padrão de 60 segundos, mais
uma passada de 15 segundos e uma atualização do painel, no pior caso. Quem erra o segredo vê o erro na tela antes de ter
motivo para clicar em testar de novo.

- **O refinamento da chave:** na Avalara, a chave do freio inclui o ambiente. O `Sandbox` e o `Production` são duas
  credenciais. Uma recusa no Sandbox travar por 5 minutos o teste de Produção seria frear a credencial errada. O freio
  continua por tenant e por adapter. O ambiente só separa as duas credenciais do mesmo adapter.
- **O que não entra no freio:**
  - **a indisponibilidade (5xx, 429, tempo esgotado):** não é sondagem da credencial;
  - **a credencial incompleta:** não faz requisição;
  - **o sucesso:** lembrar um sucesso seria o mesmo teste que mente, só que no freio. Um "funcionou" lembrado por 5
    minutos passaria por cima de um segredo revogado nesse meio-tempo. O volume de cliques de uma pessoa fica muito
    abaixo do que faz o Entra ID responder 429, e um 429 volta como `Unavailable`, sem inventar resposta.
- **Onde fica:** em memória, por réplica, como a recusa lembrada da Avalara. Com N réplicas, o limite vira N testes por
  5 minutos por chave. Hoje o host roda uma réplica.

### D13. ADR-0031

- **O que ele registra:**
  - o rebobinamento pela tela como o mesmo mecanismo do coletor (D7). Ele complementa o ADR-0024, em que o avanço da
    marca é monotônico (a instrução com `WatermarkTicks < @w`) e o backfill é feito apagando o cursor. O ADR-0024 não
    muda: o coletor continua sem fazer a marca regredir, e a tela ganha o segundo caminho de backfill, sob o lease;
  - o teste de credencial (D9 a D12):
    - o token novo a cada teste, como requisito;
    - o freio no endpoint, com os dois motivos;
    - a razão das proteções diferentes;
  - os módulos como apresentação, e não permissão (D2).
- **Por que um ADR, e não só a change:** a exceção à marca monotônica e a política de freio são decisões que uma fatia
  futura precisa encontrar sem ler esta change.

### D14. Revisões da prova manual (2026-10-01)

O usuário testou as duas credenciais (D365 e Avalara), a certa e a errada, e pediu estes ajustes.

- **As mensagens do teste de credencial.** A tela mostra só:
  - "Credenciais e conexão válidas", quando funciona;
  - "Credenciais ou ambiente inválidos", na recusa e na credencial incompleta;
  - "Não foi possível conectar agora. Tente novamente em instantes", na indisponibilidade;
  - com o freio, o horário do próximo teste.

  As duas primeiras são as do pedido. A terceira é decisão nossa: dizer "inválidos" a uma plataforma fora do ar levaria o
  Admin a trocar um segredo certo, que é o mesmo erro que o token novo existe para evitar.

  **O detalhe não se perde.** O motivo de cada adapter (o código AADSTS, o status HTTP, o campo que falta) vai para uma
  linha de log do host, com o tenant, o adapter, o ambiente, o veredito e se veio do freio. A resposta da API leva só a
  mensagem curta.
- **A máscara do segredo.** Ela existia como placeholder, mas não aparecia: o MUI esconde o placeholder enquanto o
  rótulo está dentro do campo. O rótulo do segredo passa a ficar recolhido, e a máscara aparece logo que a tela abre. A
  regra de segurança não muda: é placeholder, e nunca valor.
- **A URL do token sai da tela.** Ela é opcional, e sem ela o hub a monta pela URL base. Um `tokenUrl` já gravado
  continua valendo e volta intacto ao salvar, porque a tela preserva os campos que não mostra. A consequência: a tela não
  limpa um `tokenUrl` gravado. Isso só se faz por SQL.
- **O painel acompanha o interruptor da tela (D6).** As opções aparecem assim que o interruptor é ligado, sem salvar.
  - **Antes de salvar:** o painel mostra o que está registrado.
  - **Se o adapter gravado ainda não varre:** a API dá 404, e o painel pede para salvar as configurações.
- **Os textos da tela, sem a língua do código.**

  | Antes | Depois |
  |---|---|
  | "coletor" | sai |
  | "Última verificação" | "Última busca" |
  | "Falhas seguidas" | "Falhas consecutivas" |
  | "Marca" | "Sincronizado até" |
  | o throttling | "Aguardando o ERP até" |
  | "Voltar a marca para" | "Buscar novamente desde" |

  - **O ponto de partida:** o `startFrom` só aparece sem marca, que é quando ele decide de onde a primeira busca começa.
    Com marca, ele não vale mais, e a linha confundia.
  - **A confirmação:** ficou com quatro frases curtas (D8), sem perder o que a primeira versão afirmava: "alteradas", o
    que não é reenviado, o que é processado de novo, e o custo.
  - **As mensagens do servidor no rebobinamento:** também mudam, porque vão para a tela. Por exemplo: "A integração
    automática está buscando notas agora…", "Escolha uma data no passado".
  - **O painel reservado dos módulos e a aba de módulos:** ficaram com uma frase cada.
- **A credencial errada lida como indisponível (segunda rodada).** Com a credencial errada, o D365 respondia "o Entra ID
  não respondeu como esperado": o teste só aceitava como recusa a falha com o código AADSTS na mensagem de fora, e o
  código pode vir só na exceção interna.
  - **A regra agora:** uma falha só é indisponibilidade com uma causa de rede na cadeia de exceções (`NetworkFailure`,
    na Application, usado pelos dois adapters). O resto é "Credenciais ou ambiente inválidos": o segredo errado, o app ou
    o tenant que não existe, o formato inválido, o host que não existe, e o 404 ou 403 no endpoint de token.
- **A URL do F&O que persiste.** Não é defeito. Ela é dado do perfil, no SQL Server, que grava num volume nomeado
  (`sql-data`). Sobrevive a derrubar o host, a tela e os emuladores. O que não persiste é o segredo no emulador do Key
  Vault, que não tem volume.

## Risks / Trade-offs

- **[O rebobinamento com mais de uma réplica]**
  - **O caso:** a regra do registro de publicações vale por réplica. Uma réplica cuja última marca vista é anterior ao
    alvo do rebobinamento não vê regressão.
  - **O efeito:** os pares dela são todos até essa marca, e ela só poderia suprimir os da faixa de sobreposição acima
    do alvo. Algumas notas dessa faixa não seriam reenfileiradas por ela.
  - **Mitigação:**
    - hoje o host roda uma réplica;
    - fica um item no STATUS: com mais de uma, o rebobinamento precisa de uma geração persistida no cursor, que cada
      réplica compara no `BeginPull`. O rebobinamento por SQL de hoje tem o mesmo limite.
- **[O último erro na tela]** → o texto é o que já está no banco e no log, e o painel é só para Admin. A tarefa D6
  confere que nenhuma mensagem que entra no `RecordFailureAsync` leva token ou segredo.
- **[Os cliques que dão certo vão todos ao emissor do token]**
  - **O caso:** o sucesso não entra no freio (D12), e cada teste pede um token novo.
  - **Mitigação:**
    - o volume de uma pessoa clicando fica muito abaixo do que faz o Entra ID responder 429;
    - um 429 volta como `Unavailable`, e não como resposta inventada;
    - o coletor continua reusando a credencial.
- **[A seção `poll` ilegível esconde o painel]**
  - **O caso:** o painel segue a regra do `/info`. Com a seção `poll` ilegível, o `/info` diz desligada, e o painel e o
    selo não aparecem. O coletor, enquanto isso, registra a falha no cursor.
  - **Mitigação:** a gravação pela tela já recusa um valor novo ilegível (`automatic-integration`). Só um SQL chega a
    esse estado. Fica no STATUS, e fora desta fatia.
- **[O freio em memória]** → com N réplicas, vira N testes por 5 minutos. É aceito, como na recusa lembrada da Avalara.
- **[A máscara recusada como segredo real]** → o custo é aceito (D3). A mensagem diz para digitar o segredo.
- **[O texto da confirmação envelhece]** → a frase mora num lugar só, com o comentário que aponta de onde vem cada
  afirmação (D8).
- **[O módulo escondido, com a API aberta]** → é o desenho (D2). Está escrito na spec, no design e no ADR, para ninguém
  tratar como segurança.
- **[Uma dependência nova no front]** → o `vitest` é só de desenvolvimento e não entra no bundle.

## Migration Plan

1. **A migração `AddConnectorProfileModules`:** a coluna `Modules`, anulável, sem preencher nada. Nula quer dizer
   `["Fiscal"]`, então todo tenant existente continua vendo o que via.
2. **Front e back sobem juntos.**
   - O `/info` e o `/connector` ganham `modules`. Um front antigo os ignora.
   - Um front antigo que salva o perfil não manda `modules`, e o gravado fica.
3. **O rollback:** tirar a coluna com a migração de volta. Os endpoints novos (`/connector/automatic`, o `rewind` e o
   `/connector/test`) não guardam estado em banco. Só o rebobinamento grava, na coluna que já existe.

## Open Questions

- **O texto do painel reservado de Contábil e Inventário.** O que a spec fixa é que ele diz "reservado" e não mostra
  documento fiscal. A redação final pode ser ajustada na prova visual sem mudar nada do desenho.
- **O papel de Suporte no painel.** Quando o papel existir (o gancho do ADR-0030), ele pode ver o painel sem rebobinar.
  Fica para a fatia que o criar.
