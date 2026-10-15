// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, screen } from '@testing-library/react';
import { renderWithProviders, testQueryClient } from '../../test/renderWithProviders';
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

const INFO = (canViewRawJson: boolean): InfoResponse => ({
  environment: 'Production',
  automaticIntegration: true,
  inboundScans: true,
  connectors: [],
  canViewRawJson,
});

const BOTAO = /Visualizar JSON/;

// O `/info` que **não respondeu** é cache vazio com a chamada pendente — e não um `canViewRawJson: false`. São
// estados diferentes, e é justamente o que separa "o servidor disse que não" de "o servidor não disse nada".
function render(info: InfoResponse | undefined) {
  const qc = testQueryClient();
  if (info !== undefined) {
    qc.setQueryData(['info'], info);
  }
  // A leitura do desfecho é semeada em todos os casos: ela não é a ligação sob teste, e sem isso a consulta ficaria
  // pendente de rede.
  qc.setQueryData(['reading', DOC.tenantId, DOC.naturalKey], READING);
  return renderWithProviders(<DocumentDetail doc={DOC} />, { queryClient: qc });
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
