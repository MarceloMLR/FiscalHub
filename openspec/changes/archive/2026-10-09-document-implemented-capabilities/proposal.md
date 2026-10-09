## Why

Três capabilities rodam sem spec: o poll de status, a dead-letter visível e o chamado de suporte. As três são de
antes da adoção do OpenSpec (ADR-0007, ADR-0010 e ADR-0021, de julho), e o `docs/STATUS.md` as lista como o próximo
passo 1. Sem spec, a próxima change que tocar nelas não tem contrato contra o qual comparar.

O rascunho `docs/specs-retroativas-rascunho.md` foi levantado lendo o código. Conferido requisito por requisito, parte
do que ele afirma não é o que o código faz. Em dois casos a frase errada também está num comentário do código. Esta
change escreve as três specs com o que foi verificado e registra cada divergência como achado, para decisão.

## What Changes

- **Três specs novas,** que descrevem comportamento que já roda. Nenhuma linha muda em `src/`, `tests/` ou
  `dashboard/`.
- **Os achados ficam visíveis, e não resolvidos em silêncio.** Onde o rascunho afirma algo que o código não faz, a
  spec diz só o que foi verificado, e o achado registra a troca. O `design.md`, na seção "Achados", traz o texto do
  rascunho, o comportamento real e os arquivos. Nenhuma troca é definitiva antes do aval do Marcelo.
- **A regra da troca:** a spec não afirma o que o código não faz. Ela também não transforma em requisito o que parece
  defeito. Nesse caso a frase sai, e a decisão fica com o Marcelo.
- **O `docs/STATUS.md` entra na change.** Ele já vem atualizado pelo Marcelo. As divergências dele com o código estão
  no `design.md`, e só entram no arquivo com aval.
- **O rascunho é apagado** no fim, depois de as três specs estarem fechadas.

## Capabilities

### New Capabilities

- `integration-status-poll`: a consulta, na plataforma, do status dos documentos em voo, pelo id externo. Cobre o
  desfecho de cada consulta, o limite de consultas que leva a `Unconfirmed` e o isolamento de falha por documento
  (ADR-0007 e ADR-0010).
- `dead-letter-visibility`: a mensagem que esgota as entregas, em qualquer fila, vira registro `DeadLettered` no
  store, com o motivo do Service Bus. Não reprocessa (ADR-0010).
- `support-ticket`: a abertura de chamado a partir de notas selecionadas, com um zip de rastreabilidade por nota, o
  teto de anexos, a descrição com o estado de cada nota, o provider pelo perfil do tenant e a estimativa do tamanho
  antes de abrir (ADR-0021).

### Modified Capabilities

Nenhuma. Três specs existentes encostam nestas, e ficam como estão:

- **`discovery-queue-consumer`:** tem o requisito "Dead-letter da fila de descoberta visível". Ele continua dono do
  que é próprio da descoberta, o grupo trazido na referência. A `dead-letter-visibility` é o mecanismo comum às duas
  filas.
- **`document-grouping`:** diz que o card "Com erro" conta as rejeitadas, as sem retorno e as da dead-letter. As
  specs novas não contradizem isso (achado A4).
- **`platform-response-trace`:** diz que qualquer papel abre chamado com o zip da nota. A `support-ticket` descreve
  como o chamado é montado.

## Non-goals

- **Corrigir os achados.** O achado que pedir código vira change própria, test-first. Se o Marcelo decidir trazer um
  deles para cá, a change deixa de ser só documentação e o `tasks.md` ganha o teste antes.
- **Escrever testes.** Não há mudança de comportamento. Os cenários que nenhum teste prova ficam listados no
  `design.md`, para uma change futura.
- **Backfill de outras capabilities.** São só estas três, que o STATUS lista. O resto do legado não vira spec de uma
  vez (CLAUDE.md, seção 9).
- **ADR novo.** Não há decisão de arquitetura. O porquê continua nas ADRs 0003, 0007, 0010 e 0021.
- **Mudar outras specs,** inclusive as três que encostam nestas.
- **A tela do chamado.** A spec cobre o que o servidor faz. O modal do dashboard fica fora.

## Impact

- **`openspec/specs/`:** três specs novas no archive. O total vai de 24 para 27.
- **`docs/`:** o `STATUS.md` entra como o Marcelo deixou, mais as correções que ele aprovar. O
  `specs-retroativas-rascunho.md` sai.
- **Código:** nenhum. O `dotnet build` e o `dotnet test` rodam uma vez no fim, só como guarda.
- **Sem impacto:** API, banco, mensageria, dashboard e o lado D365.
