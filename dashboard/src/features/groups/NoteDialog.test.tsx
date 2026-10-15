// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, screen } from '@testing-library/react';
import { renderWithProviders, testQueryClient } from '../../test/renderWithProviders';
import { NoteDialog } from './NoteDialog';
import type { DocumentReading, DocumentSummary, InfoResponse } from '../../types';

// O outro botão da visão técnica — "Baixar arquivos", o zip das fotos cruas —, pela mesma regra e pelos mesmos três
// casos do `DocumentDetail.test.tsx`. São dois arquivos porque são duas telas que chamam o `canViewRawJson` por conta
// própria: desligar uma não desliga a outra, e é isso que cada arquivo prova.
//
// **O que este arquivo não faz:** não abre o `TicketModal`, e não afirma nada sobre o "Visualizar JSON" do
// `DocumentDetail` que vive dentro deste diálogo — esse tem o teste dele.

const NOTE: DocumentSummary = {
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

const BOTAO = /Baixar arquivos/;

function render(info: InfoResponse | undefined) {
  const qc = testQueryClient();
  if (info !== undefined) {
    qc.setQueryData(['info'], info);
  }
  qc.setQueryData(['reading', NOTE.tenantId, NOTE.naturalKey], READING);
  return renderWithProviders(<NoteDialog note={NOTE} onClose={() => {}} />, { queryClient: qc });
}

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('o botão "Baixar arquivos" segue o que o /info disser', () => {
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
    // O diálogo está aberto: o que falta é o botão, e não a tela.
    expect(screen.getByText('Detalhes da nota')).toBeTruthy();
    // E as ações que não dependem do /info continuam onde estavam.
    expect(screen.getByRole('button', { name: /Abrir chamado/ })).toBeTruthy();
  });
});
