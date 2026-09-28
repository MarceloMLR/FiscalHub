## Why

O interruptor "Integração em tempo real" da tela de conectores é decorativo:

- **O que ele grava não controla nada.** O booleano `Realtime` do perfil é gravado, servido pelo `/info` e só desenha o
  selo "Tempo real ligado" na barra lateral. Quem liga e desliga o coletor de verdade é o `poll.enabled` das
  `InboundSettings`, que não tem tela e só se muda por SQL.
- **Os dois já discordam no seed.** O tenant-a nasce com `Realtime = true` e `poll.enabled = false`: em dev, a barra
  lateral diz "Tempo real ligado" com o coletor parado. É exatamente o pior caso, uma tela dizendo "ligado" com o
  coletor parado.
- **O nome promete o que ninguém faz.** "Tempo real" soa como integração por evento, que nenhum ERP nosso faz hoje. A
  pergunta do usuário é outra: roda sozinho ou não.

Hoje é impossível ligar ou desligar a integração de um tenant sem acesso ao banco.

Junto, o rebobinamento do RUNNING §6 não funciona. Apagar o cursor, para o `startFrom` valer de novo, não reprocessa
nada enquanto o processo segue de pé: a passada reporta tudo como "suprimida por já publicada". O procedimento está
documentado, não funciona e não avisa (STATUS, lacunas de 2026-09-27).

## What Changes

- **O interruptor liga e desliga o coletor do tenant.** Ele passa a gravar no `poll.enabled` das `InboundSettings`,
  e em nenhum outro lugar.
  - **Nome novo:** "Integração automática".
  - **Ligar:** grava `poll.enabled = true` e preserva o resto da seção (`intervalSeconds`, `overlapSeconds`,
    `startFrom`). Num perfil sem a seção `poll`, cria só `{"enabled": true}` e os padrões valem.
  - **Desligar:** grava `poll.enabled = false` e mantém a seção. Tirar a seção faria o poller avisar de hora em hora
    que o poll "não está configurado".
  - **É pausa, não reset.** Desligar não mexe no cursor. Religar retoma da marca d'água, e nada do que mudou no ERP
    nesse meio-tempo se perde.
  - **Quando vale:** na passada seguinte, sem reinício. O tick é de 15 segundos.
- **O `Realtime` deixa de ser campo gravado.**
  - **O que sai:** o campo sai do `TenantConnectorProfile`, do `ConnectorProfileRequest`, do `ConnectorProfileView`,
    da linha do EF e do seed. Uma migração remove a coluna.
  - **O que o `/info` devolve:** passa a derivar do perfil. É `automaticIntegration`, verdadeiro só quando o adapter
    de entrada varre e o `poll.enabled` está ligado. Settings ilegíveis contam como desligado.
  - **O que o selo mostra:** "Integração automática ligada", a partir do `/info`. Enquanto o `/info` carrega, o selo
    não afirma nada. Hoje o padrão da tela é `true`.
- **O interruptor só aparece para adapters de entrada que varrem.** Hoje só o `Dynamics365` varre.
  - **O que é "varrer":** o adapter tem um feed de mudanças registrado no host (`IDocumentChangeFeed`). É a mesma
    fonte que o poller usa, e não uma lista paralela.
  - **Na tela:** a marca fica junto do schema do adapter, onde os campos de cada adapter já moram.
  - **Trocar de ERP:** se a tela trocar para um adapter que não varre, o interruptor some.
- **Rebobinar volta a funcionar.**
  - **A regra pretendida:** quando, sob o lease, o cursor do tenant não existe ou existe sem marca d'água, o poller
    descarta o registro de publicação daquele (tenant, origem) antes da leitura. Nada muda na regra atual: a marca que
    regride continua zerando o registro.
  - **Sem passo manual:** o registro se corrige sozinho.
  - **A causa ainda é hipótese.** Ela saiu da leitura do código e nunca foi observada: o cursor apagado no meio de
    uma passada, antes do primeiro avanço da marca. Nesse caso, a última marca vista ficaria igual ao `startFrom`, e a
    regressão não seria detectada (design, D5).
  - **A reprodução vem primeiro:** dois testes pela variante do `DELETE`, que é a do roteiro e não tem teste. Um apaga
    o cursor entre passadas, e o outro no meio da passada.
  - **Se a hipótese se confirmar,** entra a regra acima.
  - **Se os testes a desmentirem,** a regra não entra "por via das dúvidas". A correção passa a ser a da causa achada,
    e o design, a spec e as tarefas do grupo são ajustados antes de implementar.
- **ADR-0029.** Registra três decisões:
  - a integração automática é o `poll.enabled`;
  - o perfil perde o flag de tempo real;
  - "varrer" é ter feed registrado.

  Revisa o ADR-0019, que tipava o `realtime` como campo comum do perfil. O ADR-0019 ganha a nota de revisão.
- **BREAKING (contrato do front):**
  - o `/info` troca `realtime` por `automaticIntegration`;
  - o `GET /connector` perde o `realtime`;
  - o `PUT /connector` deixa de ler o `realtime`: o campo, se vier, é ignorado.

  Front e back sobem juntos nesta fatia. Não há cliente em produção.

## Capabilities

### New Capabilities

- `automatic-integration`: o interruptor "Integração automática" da tela de conectores. Cobre:
  - a gravação no `poll.enabled`, como única fonte, sem outro campo guardando o mesmo estado;
  - ligar e desligar preservando a seção `poll`;
  - o interruptor só para adapter que varre;
  - a leitura derivada, no `/info` e no selo da barra lateral;
  - o efeito na passada seguinte, sem tocar no banco e sem reiniciar.

### Modified Capabilities

- `change-feed-polling`:
  - em "Supressão de par já publicado", entram os cenários do cursor apagado entre passadas e no meio de uma passada,
    que valem em qualquer resultado. O rebobinamento passa a incluir o cursor ausente, ou sem marca, no início do poll.
    Essa parte depende da hipótese do D5 e só se fixa depois dos testes de reprodução;
  - em "Seleção dos tenants a consultar", o desligado de propósito passa a ficar em silêncio, sem consulta, falha
    nem aviso no resumo da passada;
  - em "Marca d'água persistida por tenant e origem", desligar não mexe na marca, e o religado retoma da marca
    preservada, e não do `startFrom`.

## Non-goals

- **Botão de "reprocessar" na tela.** Rebobinar põe documentos reais de volta na esteira e, conforme o caso (ver a
  mudança de versão do canônico no STATUS), reenvia à plataforma. Quem pode fazer isso, com qual confirmação e com qual
  efeito na idempotência é decisão de produto, que esta fatia não toma. Apagar o cursor continua sendo ato de operação,
  por SQL.
- **Tela para os demais campos do poll** (intervalo, sobreposição, `startFrom`). Ficam registrados como o próximo passo
  natural: ligar o interruptor abre as opções.
- **Registro de publicação persistido**, para sobreviver a réplicas. É da fatia de nuvem, e já está no STATUS.
- **Saúde do coletor na tela.** O selo diz "ligada" (configurada para rodar), e não "rodando sem erro". As falhas
  seguem no log e no cursor (`ConsecutiveFailures`, `LastError`).
- **O rótulo "Tempo real" da lista de grupos.** É a mesma palavra para outro conceito. O `RealTime` do modo de
  integração diz como o documento entrou, e não se o conector roda sozinho. Renomear mexe em contrato (o valor gravado
  nos documentos e o modelo das consultas), e por isso fica para outra fatia. A distinção fica escrita no design (D12)
  e num item do STATUS.
- **Recusar `poll.enabled = true` num adapter que não varre.** O valor é inofensivo, porque nenhum feed consulta esse
  tenant. O `/info` também não o mostra como ligado.

## Impact

- **Application:**
  - `Connectors`: `TenantConnectorProfile`, `ConnectorProfileRequest` e `ConnectorProfileView` perdem o `Realtime`, e o
    `ConnectorProfileService` deixa de copiá-lo;
  - `Inbound`:
    - o `ChangeFeedPollSettings` ganha uma leitura que não lança (para o `/info`);
    - entra a derivação do estado da integração automática (adapter que varre e `poll.enabled`), com teste;
    - o `ChangeFeedPublicationLog` ganha o descarte explícito de um (tenant, origem);
    - o `ChangeFeedPoller` passa a descartar o registro quando o cursor lido sob o lease não existe ou não tem marca.

  Nenhuma porta nova, e nenhuma dependência de Infrastructure.
- **Infrastructure:**
  - o `ConnectorProfileRow` e o `SqlConnectorProfileStore` perdem o `Realtime`;
  - a migração `RemoveConnectorProfileRealtime` remove a coluna, e o snapshot acompanha;
  - o seed perde o `Realtime`.
- **Host:** o `/info` passa a devolver `automaticIntegration`, a partir do perfil e das origens dos feeds registrados.
- **Dashboard:**
  - `ConnectorsPage`: o interruptor "Integração automática" lê e grava o `poll.enabled` nas settings de entrada, e só
    aparece para o adapter que varre;
  - `adapterSchemas`: a marca do adapter que varre;
  - `App.tsx`: o selo "Integração automática ligada", falso enquanto carrega;
  - `types.ts` e `client.ts`: sem `realtime`, e com `automaticIntegration` no `/info`.
- **Testes:**
  - o poller, com o cursor apagado entre passadas e no meio de uma passada;
  - o registro de publicação, com o descarte;
  - a derivação do estado;
  - o serviço de perfil e o store SQL sem o campo;
  - os construtores de `TenantConnectorProfile` nos testes de adapter, que hoje passam `Realtime`.
- **Docs:**
  - `docs/adr/0029-*.md`, com o índice e a nota no ADR-0019;
  - `docs/RUNNING.md` §6: ligar e desligar pela tela, e o rebobinamento que reprocessa sem reiniciar;
  - `docs/STATUS.md`: as duas lacunas fechadas com evidência, e o registro persistido segue na fatia de nuvem.
