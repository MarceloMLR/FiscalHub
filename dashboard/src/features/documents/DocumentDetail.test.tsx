// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders, testQueryClient } from '../../test/renderWithProviders';
import { api } from '../../api/client';
import { Modal } from '../../components/Modal';
import { DocumentDetail } from './DocumentDetail';
import type { DocumentReading, DocumentSummary, InfoResponse } from '../../types';

// A **ligação** dos botões da visão técnica: o `canViewRawJson` já tem teste próprio (`features/rawJson.test.ts`, 4
// casos) e continua como está — o que ficou sem teste é a tela *chamar* a função e mostrar ou esconder o botão pelo
// que ela disser. Foi por aí que o defeito 1 de 2026-10-15 passou: a função estava certa, e quem errava era o cache do
// React Query por trás dela.
//
// A regra é de `platform-response-trace`: "A tela MUST usar essa resposta para mostrar os botões, e MUST NOT
// mostrá-los antes de o `/info` responder, nem se ele falhar". Daí os **três** casos — e não dois.
//
// **O que este arquivo não faz:** não abre o `RawJsonModal` nem afirma o que ele mostra. A ligação sob teste é
// *mostrar ou esconder o botão*, e não o que ele abre.

const DOC: DocumentSummary = {
  tenantId: 'acme',
  naturalKey: '35260100000000000000550010000000011000000017',
  type: 'NFe',
  status: 'Confirmed',
  attempts: 1,
  updatedAt: '2026-10-15T12:00:00Z',
  reprocessings: 0,
};

const READING: DocumentReading = { fields: [], omissions: [] };

const INFO = (canViewRawJson: boolean, outboundDispatch = true): InfoResponse => ({
  environment: 'Production',
  automaticIntegration: true,
  inboundScans: true,
  connectors: [],
  canViewRawJson,
  outboundDispatch,
});

const BOTAO = /Visualizar JSON/;
const REPROCESSAR = /Reprocessar/;
// A explicação do botão desativado. O trecho é o do caminho ("Ligue-o em Configurações…") de propósito: o começo dela
// — "o envio ao conector de saída está desligado" — também aparece no **motivo gravado** da nota em "Salva", e um
// matcher que casasse com os dois não provaria qual dos textos está na tela.
const EXPLICACAO = /Ligue-o em Configurações → Conectores → Saída/;

// Uma nota rejeitada pela plataforma: é nela que o botão de reprocessar aparece.
const REJEITADA: DocumentSummary = { ...DOC, status: 'IntegrationError', reason: 'Plataforma de compliance rejeitou: CFOP inválido' };

// A nota que parou com a chave desligada. Ela **é** reprocessável — o reprocesso manual é o único caminho de saída
// desse estado (ADR-0040) —, e não é falha.
const SALVA: DocumentSummary = {
  ...DOC,
  status: 'Saved',
  reason: 'salva: o envio ao conector de saída está desligado nas configurações do tenant',
};

// O `/info` que **não respondeu** é cache vazio com a chamada pendente — e não um `canViewRawJson: false`. São
// estados diferentes, e é justamente o que separa "o servidor disse que não" de "o servidor não disse nada".
function render(info: InfoResponse | undefined, doc: DocumentSummary = DOC) {
  const qc = testQueryClient();
  if (info !== undefined) {
    qc.setQueryData(['info'], info);
  }
  // A leitura do desfecho é semeada em todos os casos: ela não é a ligação sob teste, e sem isso a consulta ficaria
  // pendente de rede.
  qc.setQueryData(['reading', doc.tenantId, doc.naturalKey], READING);
  return renderWithProviders(<DocumentDetail doc={doc} />, { queryClient: qc });
}

// O mesmo detalhe **dentro do modal**, que é como ele aparece na tela (`NoteDialog`). É o hospedeiro que o defeito da
// dica mexia: o corpo do modal é `overflow: auto`.
function renderNoModal(onClose: () => void, doc: DocumentSummary = REJEITADA) {
  const qc = testQueryClient();
  qc.setQueryData(['info'], INFO(true, false));
  qc.setQueryData(['reading', doc.tenantId, doc.naturalKey], READING);
  return renderWithProviders(
    <Modal title="Detalhes da nota" onClose={onClose}>
      <DocumentDetail doc={doc} />
    </Modal>,
    { queryClient: qc },
  );
}

beforeEach(() => {
  // Nenhuma chamada deve sair daqui; a que sair (o `/info` do caso "sem resposta") fica pendente para sempre, que é o
  // estado que o caso precisa.
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  // Os espiões do `api` são de métodos reais: sem restaurar, o dublê de um caso vazaria para o seguinte.
  vi.restoreAllMocks();
});

describe('o botão "Visualizar JSON" segue o que o /info disser', () => {
  it('o /info dizendo que pode: o botão existe', () => {
    render(INFO(true));

    expect(screen.getByRole('button', { name: BOTAO })).toBeTruthy();
  });

  it('o /info dizendo que não pode: o botão não existe', () => {
    render(INFO(false));

    expect(screen.queryByRole('button', { name: BOTAO })).toBeNull();
  });

  it('o /info sem responder: o botão não existe — para papel nenhum', () => {
    render(undefined);

    expect(screen.queryByRole('button', { name: BOTAO })).toBeNull();
    // E a tela está desenhada: o que falta é o botão, e não o detalhe inteiro.
    expect(screen.getByText(DOC.naturalKey)).toBeTruthy();
  });
});

// A mesma ligação, para a chave de envio (capture-without-dispatch, 7.2): **quem** tem botão sai do status (as falhas
// e a "Salva", pelo `isReprocessable`), e **como** ele sai — habilitado ou desativado-com-motivo — sai do
// `outboundDispatch` do /info. Quem nunca teve botão continua sem nenhum, e sem explicação nenhuma: a chave não
// explica uma indisponibilidade que não é dela.
describe('o reprocesso segue a chave de envio do /info', () => {
  const reprocessar = () => screen.queryByRole('button', { name: REPROCESSAR });

  // O foco de verdade (`.focus()`), que é o que prova que o botão é focável — um `disabled` não seria —, mais o
  // `focusIn`: o React 18 escuta `focusin` na raiz, e o `focus()` do jsdom não o propaga até lá. No navegador, um só
  // tabular faz as duas coisas.
  const focar = (el: HTMLElement) => {
    el.focus();
    fireEvent.focusIn(el);
  };

  it('a nota em "Salva" com a chave ligada: botão habilitado, e o clique reprocessa', async () => {
    // O caso que faltava. "Salva" não é falha, e por isso não entra em `FAILURE_STATUSES`; mas o reprocesso manual é
    // o **único** caminho de saída dela (ADR-0040) — decidir o botão por `isFailure` a deixava sem saída pela tela.
    const chamada = vi.spyOn(api, 'reprocess').mockResolvedValue(undefined);
    render(INFO(true, true), SALVA);

    const botao = screen.getByRole('button', { name: REPROCESSAR });
    expect(botao.getAttribute('aria-disabled')).toBeNull();
    expect(screen.queryByText(EXPLICACAO)).toBeNull();

    fireEvent.click(botao);

    await waitFor(() => expect(chamada).toHaveBeenCalledWith(SALVA.tenantId, SALVA.naturalKey));
    expect(await screen.findByText(/Nota reenviada à origem/)).toBeTruthy();
  });

  it('a nota em "Salva" com a chave desligada: o botão continua lá, desativado, com a explicação', async () => {
    const chamada = vi.spyOn(api, 'reprocess');
    render(INFO(true, false), SALVA);

    const botao = screen.getByRole('button', { name: REPROCESSAR });
    expect(botao.getAttribute('aria-disabled')).toBe('true');

    fireEvent.click(botao);

    expect(screen.getByRole('tooltip').textContent).toMatch(EXPLICACAO);
    expect(botao.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id);
    // Desativado é desativado: nada sai daqui. A espera é necessária — a mutation do React Query não chama o
    // servidor dentro do clique, e sem ela a asserção passaria mesmo com a guarda do `onClick` removida.
    await new Promise((r) => setTimeout(r, 20));
    expect(chamada).not.toHaveBeenCalled();
  });

  it('a nota rejeitada com a chave desligada: o mesmo botão desativado, com a mesma explicação', () => {
    render(INFO(true, false), REJEITADA);

    const botao = screen.getByRole('button', { name: REPROCESSAR });
    expect(botao.getAttribute('aria-disabled')).toBe('true');

    focar(botao);

    expect(screen.getByRole('tooltip').textContent).toMatch(EXPLICACAO);
  });

  it('a chave desligada desativa o botão de qualquer nota que o tenha', () => {
    // A chave é porta única de toda a saída: a sem retorno e a da dead-letter também ficam desativadas.
    for (const status of ['Unconfirmed', 'DeadLettered'] as const) {
      render(INFO(true, false), { ...REJEITADA, status });
      expect(screen.getByRole('button', { name: REPROCESSAR }).getAttribute('aria-disabled')).toBe('true');
      cleanup();
    }
  });

  it('com a chave ligada, a nota rejeitada tem o botão habilitado e sem explicação alguma', () => {
    render(INFO(true, true), REJEITADA);

    expect(screen.getByRole('button', { name: REPROCESSAR }).getAttribute('aria-disabled')).toBeNull();
    expect(screen.queryByText(EXPLICACAO)).toBeNull();
  });

  it('a nota que nunca teve reprocesso, com a chave desligada: nenhum botão e NENHUMA explicação', () => {
    // Finalizado e Ignorado nunca tiveram botão de reprocessar. Uma faixa dizendo que o envio está desligado
    // atribuiria à chave uma indisponibilidade que não é dela — a explicação só existe onde o botão existe.
    for (const status of ['Confirmed', 'Ignored'] as const) {
      render(INFO(true, false), { ...DOC, status });
      expect(reprocessar()).toBeNull();
      expect(screen.queryByText(EXPLICACAO)).toBeNull();
      expect(screen.queryByRole('tooltip')).toBeNull();
      expect(screen.getByText(DOC.naturalKey)).toBeTruthy();       // e a tela está desenhada
      cleanup();
    }
  });

  it('com o /info ainda sem responder, o botão fica ausente — nem habilitado, nem desativado', () => {
    // O terceiro estado: sem resposta em mãos a tela não afirma nenhum dos dois. É a regra do `canViewRawJson`.
    render(undefined, REJEITADA);

    expect(reprocessar()).toBeNull();
    expect(screen.queryByText(EXPLICACAO)).toBeNull();
    expect(screen.getByText(REJEITADA.naturalKey)).toBeTruthy();   // e a tela está desenhada
  });

  it('a explicação alcança o mouse, o teclado e o toque — e some quando se sai', () => {
    // Um `<button disabled>` não recebe hover nem foco em vários navegadores, e aí um `title` nunca chegaria a quem
    // precisa. Por isso o desativado é `aria-disabled`: o botão continua focável, e as três entradas funcionam.
    render(INFO(true, false), REJEITADA);
    const botao = screen.getByRole('button', { name: REPROCESSAR });

    fireEvent.mouseOver(botao);                                    // mouse
    expect(screen.getByRole('tooltip').textContent).toMatch(EXPLICACAO);
    fireEvent.mouseOut(botao);
    expect(screen.queryByRole('tooltip')).toBeNull();

    focar(botao);                                                  // teclado
    expect(document.activeElement).toBe(botao);                    // focável — é o que um `disabled` tiraria
    expect(screen.getByRole('tooltip').textContent).toMatch(EXPLICACAO);
    fireEvent.focusOut(botao);
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.click(botao);                                        // clique/toque
    expect(screen.getByRole('tooltip').textContent).toMatch(EXPLICACAO);
  });
});

// A dica **flutua**: ela não pode mexer no layout de quem a hospeda (conferência em tela de 2026-10-16). O modal do
// detalhe é mais baixo do que a dica, e com ela em `position: absolute` dentro do corpo `overflow: auto` do modal, o
// `scrollHeight` do corpo crescia e aparecia uma **barra de rolagem** só por abrir a explicação.
//
// Por isso as asserções aqui são de **estrutura**, e não de texto: um teste que só procurasse a explicação passaria
// com e sem o portal, e não fecharia defeito nenhum.
describe('a explicação flutua por cima, sem mexer no layout do modal', () => {
  it('a dica sai do detalhe e vai para o body, fixa na janela', () => {
    const { container } = render(INFO(true, false), REJEITADA);
    const botao = screen.getByRole('button', { name: REPROCESSAR });

    fireEvent.mouseOver(botao);

    const dica = screen.getByRole('tooltip');
    // O portal: fora da subárvore do detalhe — e é ser descendente dela que fazia a dica empurrar o corpo do modal.
    expect(container.contains(dica)).toBe(false);
    expect(dica.parentElement).toBe(document.body);
    expect(dica.style.position).toBe('fixed');
    // E continua sendo a descrição acessível do botão: mudar de lugar no DOM não desliga o leitor de tela.
    expect(botao.getAttribute('aria-describedby')).toBe(dica.id);
  });

  it('rolando, a dica fecha em vez de ficar apontando para onde o botão já não está', () => {
    render(INFO(true, false), REJEITADA);

    fireEvent.mouseOver(screen.getByRole('button', { name: REPROCESSAR }));
    expect(screen.getByRole('tooltip')).toBeTruthy();

    // A rolagem do corpo do modal não sobe por bolha; a escuta é em **captura**, e é isso que este caso prova.
    fireEvent.scroll(document.body);

    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('clicar na dica não fecha o modal que a hospeda', () => {
    // O portal muda o lugar no DOM, não na árvore React: o clique continua subindo até o `stopPropagation` do cartão
    // do modal. Conferido por teste em vez de presumido — é a troca que mais facilmente fecharia o modal sem querer.
    const onClose = vi.fn();
    renderNoModal(onClose);

    fireEvent.mouseOver(screen.getByRole('button', { name: REPROCESSAR }));
    fireEvent.click(screen.getByRole('tooltip'));

    expect(onClose).not.toHaveBeenCalled();
    // E o espião está ligado de verdade: o X do modal fecha.
    fireEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
