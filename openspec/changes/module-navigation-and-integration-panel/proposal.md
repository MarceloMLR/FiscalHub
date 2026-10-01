## Why

O coletor do D365 guarda no cursor tudo o que é preciso para diagnosticá-lo: o último poll, as falhas seguidas e o
último erro. Nada disso aparece na tela.

- **O diagnóstico:** saber por que nenhuma nota entrou exige abrir o banco.
- **O rebobinamento:** voltar a marca exige SQL (RUNNING §6).
- **A credencial:** saber se ela funciona exige esperar uma nota falhar.

Na prova da fatia anterior, o `startFrom` que some e as settings em `{}` custaram investigação no banco. Foram a
terceira e a quarta vez.

A barra lateral também não comporta o que vem depois. O produto vai ter integração contábil e de inventário, e cada
cliente contrata um conjunto diferente de módulos. A navegação precisa de um lugar para eles, montado por configuração,
e não por código.

## What Changes

- **O bloco "Integrações" na barra lateral.**
  - **Os sub-blocos:** Fiscal, Contábil, Inventário e Agendamento.
    - **Fiscal:** é a tela de documentos de hoje.
    - **Agendamento:** é a tela de integração manual de hoje.
    - **Contábil e Inventário:** são lugares reservados, com um painel vazio. Não são um filtro da tabela de documentos
      fiscais: são outro domínio, e cada um vira uma fatia própria. **Esta fatia não entrega integração contábil nem
      de inventário.**
  - **A integração automática continua só Fiscal.** Contábil e Inventário serão cargas manuais pelo Agendamento. É
    decisão de produto, e não limitação.
- **Módulos configuráveis por cliente.**
  - **Quem marca:** o Admin, em Configurações, marca quais módulos o tenant tem.
  - **Onde fica:** no perfil de conector do tenant, que já é por cliente e já é JSON.
  - **Quem lê:** a barra lateral é montada a partir disso, pelo `/info`, para qualquer papel.
  - **Sem módulo gravado:** vale só o Fiscal, que é o comportamento de hoje.
  - **É apresentação, e não permissão.** A API continua respondendo para um módulo escondido. Restringir de fato é outra
    fatia.
- **O segredo configurado aparece mascarado.**
  - **O que muda:** o campo mostra uma máscara fixa ao lado de "configurado em <data>".
  - **A máscara é placeholder, e nunca valor.** Se os asteriscos virassem o valor do campo, o próximo salvar gravaria a
    string de asteriscos no cofre e destruiria o segredo.
  - **A regra não muda:** o campo é de escrita pura, e é enviado só quando o usuário digita.
  - **Os testes:** um teste da tela prova que salvar sem digitar nada não envia o campo. Um teste do servidor prova que
    o segredo gravado fica intacto.
  - **A defesa no servidor:** ele recusa um valor feito só de caracteres de máscara, com 400 e sem tocar o cofre.
- **Os textos de Configurações.**
  - **A mensagem de sucesso:** "Configurações salvas com sucesso".
  - **O texto de ajuda sob o interruptor:** sai.
- **O painel da integração automática.**
  - **Onde fica:** em Configurações, sob o interruptor, só para Admin.
  - **Quando aparece:** quando a integração automática gravada está ligada.
  - **O que mostra:**
    - a última verificação;
    - as falhas seguidas;
    - o último erro;
    - a marca atual;
    - o throttling pedido pela origem, quando houver;
    - o ponto de partida (`poll.startFrom`), como leitura. Com a marca ausente, o painel diz de onde a primeira
      passada vai começar, em vez de o administrador descobrir pelo silêncio.

    Os quatro primeiros existem no cursor e hoje não aparecem em lugar nenhum. O `startFrom` está no perfil, e também
    não aparece.
- **Rebobinar pela tela, para Admin.**
  - **O que faz:** o Admin leva a marca para uma data escolhida, antes da atual, depois de uma confirmação cujo texto é
    verdadeiro.
  - **O que a confirmação diz:**
    - rebobinar redescobre as notas alteradas no ERP no período;
    - não reenvia o que não mudou: a idempotência por conteúdo só barra nota enviada ou confirmada com o mesmo hash;
    - a nota recusada, sem confirmação, ignorada ou na fila de falhas é processada de novo;
    - em uma linha, o custo real: cada NF-e relida faz pelo menos 4 consultas ao F&O, mesmo a que não será reenviada.
  - **Como grava:**
    - o rebobinamento toma o mesmo lease do coletor e grava a marca condicionada a ele;
    - ele passa pela mesma regra que já descarta o registro de publicações quando a marca regride (change
      `automatic-integration-switch`).
- **Testar a credencial, na Avalara e no D365.**
  - **O que devolve:** só se funcionou ou não, e o motivo. Nunca o token, nunca o segredo, nunca um cabeçalho com valor.
  - **O que testa:** a credencial gravada, lida do cofre no servidor. Com edição pendente na tela, o botão pede para
    salvar antes.
  - **O token é novo a cada teste, nos dois adapters.** O botão responde "essa credencial funciona agora".
    - **No D365:** cada teste cria uma instância nova da credencial do Entra ID, com cache próprio, e vai ao Entra ID.
    - **Na Avalara:** a troca do teste não usa o token em cache.

    O coletor e o envio continuam com o cache normal.
  - **O freio fica no endpoint de teste:**
    - é um limite por tenant e por adapter, com o mesmo intervalo de 5 minutos da recusa lembrada da Avalara, igual
      para os dois adapters;
    - ele tem um segundo motivo: cada teste é um pedido real ao emissor do token;
    - um teste que dá certo na Avalara esquece a recusa lembrada, como o salvar do perfil já faz;
    - o token do coletor do D365 não muda: sem recusa lembrada, e com o mesmo cache.
  - **O D365 não para no token.** O teste pega o token e faz uma leitura mínima (`$top=1` na `FSFiscalDocumentBR`),
    porque o token prova a credencial, e não a permissão.
- **ADR-0031.** Registra o painel e o rebobinamento pela tela, o teste de credencial com o freio no endpoint, e os
  módulos como apresentação.
- **BREAKING (contrato do front):** o `/info` ganha `modules`, e o `GET /connector` e o `PUT /connector` ganham
  `modules`. Um cliente antigo que não manda `modules` mantém o que está gravado. Front e back sobem juntos, e não há
  cliente em produção.

## Capabilities

### New Capabilities

- `module-navigation`: o que ela cobre:
  - o bloco "Integrações" da barra lateral e os quatro sub-blocos;
  - os módulos do tenant no perfil e a leitura deles pelo `/info`;
  - a marcação em Configurações;
  - o painel vazio de Contábil e Inventário;
  - a regra de que esconder um módulo é apresentação, e não permissão.
- `automatic-integration-panel`: o que ela cobre:
  - o painel sob o interruptor, com a última verificação, as falhas seguidas, o último erro, a marca e o `startFrom`,
    este como leitura;
  - o rebobinamento pela tela, com o lease, a confirmação e a passagem pela regra que descarta o registro de
    publicações.
- `connector-credential-test`: o teste da credencial gravada da Avalara e do D365. Cobre:
  - o token novo a cada teste;
  - o que a resposta pode e não pode conter;
  - o freio por tenant e por adapter no endpoint;
  - a leitura mínima do D365;
  - o pedido de salvar antes de testar.

### Modified Capabilities

- `change-feed-polling`: a marca d'água continua monotônica para o coletor. O rebobinamento do Admin pela tela passa a
  ser o único caminho que a faz regredir.
- `connector-secret-references`: duas regras novas:
  - a tela mostra o segredo configurado com uma máscara que é placeholder, e nunca valor;
  - o servidor recusa um valor feito só de caracteres de máscara.
- `avalara-tenant-authentication`: um teste de credencial que dá certo esquece a recusa lembrada do tenant, como o
  salvar do perfil.

## Non-goals

- **Os painéis de Contábil e Inventário.** Eles precisam de domínio, portas e adapters próprios. Cada um é uma fatia.
- **Restringir o acesso por módulo na API.** Esconder o módulo não tira a permissão. Restringir é outra fatia.
- **A integração automática para Contábil e Inventário.** É decisão de produto que ela fique só no Fiscal.
- **Mudar o token do coletor do D365.** Nem recusa lembrada, nem outra regra de cache. Só o teste pede um token novo
  (design D11).
- **Um teste que prove a permissão na Avalara.** O teste da Avalara para no token (design D10).
- **Editar pela tela os outros campos da seção `poll`** (intervalo, sobreposição e `startFrom`). O rebobinamento só mexe
  na marca.
  - **O `startFrom`:** aparece no painel, como leitura. Editá-lo fica fora: definir o ponto de partida é outra operação,
    que não é rebobinar. Continua exigindo SQL, e isso fica registrado no STATUS.
- **Avançar a marca pela tela.** Só para trás. Avançar pularia notas em silêncio.
- **O aviso do cursor que nasce sem `startFrom`, e as settings que viram `{}` na troca de ERP.** Continuam no STATUS.
  O painel as torna visíveis, mas não as corrige.
- **O rebobinamento com mais de uma réplica.** A regra do registro de publicações vale por réplica, e o host roda uma
  só hoje. O limite fica escrito no design (Risks) e no STATUS.

## Impact

- **Domain:** nada.
- **Application:**
  - `Connectors`:
    - o `TenantConnectorProfile` ganha os `Modules`, com a validação e o padrão Fiscal;
    - o `ConnectorProfileRequest` e o `ConnectorProfileView` ganham os `Modules`;
    - a `ConnectorProfileService` recusa o segredo feito de máscara;
    - entram a porta `IConnectorCredentialTest` e o serviço de teste, com o freio. O serviço é observador do perfil.
  - `Inbound`:
    - o `IChangeFeedCursorStore` ganha o rebobinamento condicionado ao lease;
    - entram a leitura do painel e o serviço de rebobinamento.
- **Infrastructure:**
  - o `SqlChangeFeedCursorStore` ganha o rebobinamento;
  - o `SqlConnectorProfileStore` e a linha do perfil ganham os `Modules`;
  - uma migração (`AddConnectorProfileModules`).
- **Adapters:**
  - `Outbound.Avalara`: o teste de credencial e a troca de token do teste no `AvalaraTokenProvider`. A troca não lê o
    cache nem a recusa, e o sucesso esquece a recusa;
  - `Ingress.D365Poll`: o teste de credencial, com uma credencial nova do Entra ID por teste e a leitura `$top=1`. O
    caminho do coletor não muda.
- **Host:**
  - o `/info` e o `/connector` com os `modules`;
  - entra o `GET /connector/automatic`, que é o painel;
  - entra o `POST /connector/automatic/rewind`;
  - entra o `POST /connector/test`.

  Os três endpoints novos são só para Admin.
- **Dashboard:**
  - `App.tsx`: o bloco "Integrações" e os sub-blocos pelos módulos;
  - os painéis vazios de Contábil e Inventário;
  - `ConnectorsPage`:
    - a aba de módulos;
    - a máscara;
    - os textos;
    - o painel e o rebobinamento;
    - o botão de teste;
  - a montagem do payload do perfil vai para um módulo puro, com teste;
  - `types.ts` e `client.ts`;
  - **a primeira suíte de testes do front:** `vitest`, só como dependência de desenvolvimento (design D4).
- **Docs:**
  - o ADR-0031;
  - o RUNNING §6: o rebobinamento pela tela, e o SQL fica como alternativa;
  - o STATUS.
- **Testes:**
  - os módulos: o padrão, a validação e o `/info`;
  - a máscara recusada, e o salvar sem digitar;
  - o painel;
  - o rebobinamento: o lease ocupado, só para trás, e o fencing;
  - a passada depois do rebobinamento pelo serviço, com 0 suprimidas;
  - o teste de credencial: o freio, o esquecer no salvar, o esquecer da Avalara no sucesso, a leitura do D365 e a
    resposta sem token nem segredo.
