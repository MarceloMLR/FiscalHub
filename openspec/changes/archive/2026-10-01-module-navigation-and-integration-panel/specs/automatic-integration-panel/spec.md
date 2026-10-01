## Purpose

Mostrar ao Admin, na tela, o estado do coletor da integração automática do tenant (a última verificação, as falhas
seguidas, o último erro e a marca d'água), e deixar o Admin rebobinar a marca pela tela, sem abrir o banco.

## ADDED Requirements

### Requirement: O painel sob o interruptor

Em Configurações, sob o interruptor "Integração automática", o Admin MUST ver o painel da integração automática do
tenant dele.

- **Quando aparece:** assim que o interruptor é ligado na tela, com um adapter de entrada que varre, sem esperar o salvar.
  Desligar o interruptor na tela esconde o painel.
- **Por quê:** ligar, salvar e só então ver as opções para ajustar e salvar de novo é contraintuitivo (revisão da prova
  manual, 2026-10-01).
- **Antes de salvar:** o painel mostra o que está registrado. Se o adapter gravado ainda não varre, ele pede para salvar
  as configurações.

O painel mostra o que o coletor registrou:

- **a última busca:** o fim do último poll do tenant, com ou sem sucesso;
- **as falhas consecutivas:** zero depois de um sucesso;
- **o último erro:** o texto que o coletor registrou, como foi registrado. Aparece só quando há falha;
- **"sincronizado até":** a marca, o instante até o qual o que mudou no ERP já foi enfileirado;
- **a espera pedida pelo ERP:** o throttling, até quando a origem pediu para esperar. Aparece só quando está no futuro;
- **o ponto de partida:** o `poll.startFrom` gravado no perfil, só quando não há marca.

Os instantes aparecem no fuso do navegador.

**Os textos da tela** falam a língua de quem usa: "busca" e "sincronizado até". Eles MUST NOT usar "coletor", "marca" ou
"startFrom", que são palavras do código.

- **Sem cursor:** quando o tenant não tem cursor, o painel MUST dizer que nenhuma busca foi feita ainda.
- **Cursor sem marca:** o cursor existe sem marca quando nasceu de uma falha. O painel mostra as falhas e o erro.
- **De onde a primeira busca começa:** sem marca, seja sem cursor ou com cursor sem marca, o painel MUST dizer de onde a
  primeira busca vai começar:
  - **com `startFrom`:** da data dele;
  - **sem `startFrom`:** do momento em que ela rodar, e as notas alteradas antes disso não serão buscadas.

  Assim, o administrador vê o ponto de partida antes da busca, em vez de descobri-lo pelo silêncio: nenhum erro, a marca
  avançando e zero documento.
- **Com marca:** o ponto de partida não aparece, porque não vale mais.
- **O `startFrom` é só leitura.** O painel MUST NOT editá-lo. Definir o ponto de partida é outra operação, que não é
  rebobinar, e fica fora desta capability.
- **A atualização:** enquanto está aberto, o painel MUST se atualizar sozinho, no ritmo das passadas (15 segundos), sem
  recarregar a página.
- **A API:** o painel vem de `GET /connector/automatic`, só para Admin, sempre do tenant do usuário logado. Para um
  adapter de entrada que não varre, a resposta é 404.

#### Scenario: Coletor com erro
- **WHEN** o tenant-a tem a integração ligada, e o cursor dele registra 3 falhas seguidas, o último erro
  `AADSTS7000215: Invalid client secret provided` e a marca em 2026-09-29T14:00:00Z
- **THEN** o painel mostra as 3 falhas, o erro e a marca, sem que ninguém abra o banco

#### Scenario: Coletor saudável
- **WHEN** o último poll do tenant-a terminou com sucesso há 20 segundos
- **THEN** o painel mostra a última verificação, zero falhas e a marca, e não mostra erro

#### Scenario: Nenhuma busca ainda
- **WHEN** o Admin liga a integração do tenant-a pela primeira vez e salva, antes da primeira passada
- **THEN** o painel diz que nenhuma busca foi feita ainda

#### Scenario: Sem marca, com startFrom
- **WHEN** o tenant-a não tem cursor, e o perfil tem `poll.startFrom = 2015-01-01T00:00:00Z`
- **THEN** o painel diz que a primeira busca começa em 01/01/2015

#### Scenario: Sem marca, sem startFrom
- **WHEN** o tenant-a não tem cursor, e o perfil não tem `poll.startFrom`
- **THEN** o painel diz que a primeira busca começa no momento em que rodar, e que as notas alteradas antes disso não
  serão buscadas

#### Scenario: Com marca, o ponto de partida não aparece
- **WHEN** a marca do tenant-a está em 2026-09-29T14:00:00Z, e o perfil tem `poll.startFrom = 2015-01-01T00:00:00Z`
- **THEN** o painel mostra "sincronizado até" com a marca, e não mostra o ponto de partida
- **AND** o painel não oferece como editar o `startFrom`

#### Scenario: O painel acompanha as passadas
- **WHEN** o painel está aberto e uma passada falha
- **THEN** em até 15 segundos o painel mostra a falha nova, sem recarregar a página

#### Scenario: Ligar o interruptor mostra o painel antes de salvar
- **WHEN** a integração automática gravada do tenant-a está desligada, e o Admin liga o interruptor na tela, sem salvar
- **THEN** o painel aparece na hora, com o que está registrado

#### Scenario: Interruptor desligado na tela
- **WHEN** o interruptor está desligado na tela, gravado ou não
- **THEN** o painel não aparece

#### Scenario: Os textos sem a língua do código
- **WHEN** o Admin abre o painel
- **THEN** nenhum texto da tela diz "coletor", "marca" ou "startFrom"

#### Scenario: Viewer não lê o painel
- **WHEN** um Viewer do tenant-a chama `GET /connector/automatic`
- **THEN** a resposta é 403

### Requirement: Rebobinar a marca pela tela

No painel, o Admin MUST poder levar a marca d'água do tenant dele para um instante escolhido, pela tela, sem SQL. A API
é `POST /connector/automatic/rewind`, só para Admin e sempre do tenant do usuário logado.

- **Só para trás:** o instante MUST ser anterior à marca atual, e não pode estar no futuro. Fora disso, a gravação
  responde 400 com o motivo, e nada muda. Avançar a marca pularia notas em silêncio.
- **Sem marca:** sem cursor, ou com cursor sem marca, a gravação responde 409 e diz que ainda não houve nenhuma busca
  para o tenant. A primeira passada parte do `startFrom`.
- **O lease:** o rebobinamento MUST tomar o mesmo lease que o coletor usa para o tenant e a origem.
  - **Coletor lendo:** se o coletor está lendo o tenant, a gravação responde 409 e pede para tentar de novo em alguns
    segundos, e nada muda.
  - **A gravação:** a nova marca MUST ser gravada condicionada à posse do lease, na mesma operação atômica, e o lease
    MUST ser liberado depois, com ou sem sucesso.
- **O que não muda:**
  - a última verificação, as falhas, o último erro e o throttling do cursor;
  - o registro dos documentos;
  - a seção `poll` do perfil.
- **Integração desligada:** o rebobinamento não exige a integração ligada. Com ela desligada, a marca rebobinada vale
  quando ela for religada.
- **O registro:** cada rebobinamento MUST deixar uma linha de log com o usuário, o tenant, a marca de antes e a nova.

#### Scenario: Rebobinar para uma data anterior
- **WHEN** a marca do tenant-a está em 2026-09-29T14:00:00Z, e o Admin rebobina para 2026-09-01T00:00:00-03:00 e
  confirma
- **THEN** a marca passa a 2026-09-01T03:00:00Z
- **AND** a passada seguinte consulta a origem a partir dessa marca, menos a sobreposição

#### Scenario: O caminho da tela passa pela regra do registro de publicações
- **WHEN** o documento A foi enfileirado com o carimbo assentado, e o Admin rebobina pela tela a marca do tenant-a para
  antes do carimbo de A, com o processo de pé
- **THEN** a passada seguinte enfileira A de novo, e não o conta como suprimido
- **AND** isso vale sem reinício e sem passo manual

#### Scenario: O que a idempotência barra depois do rebobinamento
- **WHEN** a janela rebobinada tem:
  - a NF-e A, confirmada, com o mesmo conteúdo no ERP;
  - a NF-e B, recusada pela plataforma;
  - a NFS-e C, ignorada
- **THEN** A é lida de novo no ERP e não é enviada de novo
- **AND** B é lida, processada e enviada de novo
- **AND** C é registrada como ignorada de novo, sem leitura da nota no ERP

#### Scenario: Coletor lendo
- **WHEN** o coletor detém o lease do tenant-a, e o Admin confirma um rebobinamento
- **THEN** a gravação responde 409, pedindo para tentar de novo em alguns segundos
- **AND** a marca continua a que o coletor deixar

#### Scenario: Data depois da marca
- **WHEN** a marca do tenant-a está em 2026-09-01T00:00:00Z, e a gravação pede 2026-09-10T00:00:00Z
- **THEN** a gravação responde 400, dizendo que só é possível voltar a marca
- **AND** a marca não muda

#### Scenario: Data no futuro
- **WHEN** a gravação pede um instante depois de agora
- **THEN** a gravação responde 400, e a marca não muda

#### Scenario: Sem marca
- **WHEN** o tenant-a não tem cursor, e a gravação pede 2026-09-01T00:00:00Z
- **THEN** a gravação responde 409, dizendo que ainda não houve nenhuma busca para o tenant
- **AND** nenhum cursor é criado

#### Scenario: Viewer não rebobina
- **WHEN** um Viewer do tenant-a chama `POST /connector/automatic/rewind`
- **THEN** a resposta é 403, e a marca não muda

#### Scenario: Só o tenant do Admin
- **WHEN** o Admin do tenant-a rebobina a marca
- **THEN** a marca do tenant-c não muda

### Requirement: A confirmação diz o que o rebobinamento faz

Antes de gravar, a tela MUST pedir confirmação, com um texto curto e verdadeiro sobre o efeito e o custo. O texto MUST
dizer:

- a data escolhida, no fuso do navegador, no título ("Buscar novamente desde <data>?");
- que as notas **alteradas** no ERP desde essa data serão lidas de novo, e não as emitidas, porque a marca é a data de
  alteração;
- que as notas já enviadas e sem alteração não serão reenviadas;
- que as notas recusadas, com erro ou ignoradas serão processadas de novo;
- em uma linha, o custo: cada NF-e lida gera pelo menos 4 consultas ao ERP.

Cancelar MUST NOT enviar nada.

#### Scenario: Confirmar
- **WHEN** o Admin escolhe 01/09/2026 00:00 e pede para voltar a marca
- **THEN** a tela mostra a confirmação, com a data no título e as quatro frases
- **AND** só depois de confirmar a gravação é enviada

#### Scenario: Cancelar
- **WHEN** o Admin cancela a confirmação
- **THEN** nenhuma requisição de rebobinamento é enviada, e a marca não muda
