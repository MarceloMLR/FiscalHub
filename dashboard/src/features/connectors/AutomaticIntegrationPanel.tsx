import type { ReactNode } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { RewindMark } from './RewindMark';

// A situação da integração automática (design D6): o que o coletor registrou no cursor do tenant, sem abrir o banco.
// Aparece assim que o interruptor é ligado na tela, e se atualiza no ritmo das passadas. Os textos falam a língua de quem
// usa: "busca" e "sincronizado até", e não "coletor", "marca" ou "startFrom", que são palavras do código (revisão de
// 2026-10-01). O ponto de partida só aparece sem marca, que é quando ele decide alguma coisa.

const PASS_INTERVAL_MS = 15_000;

function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function ago(iso: string): string {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `há ${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `há ${minutes} min`;
  return `há ${Math.round(minutes / 60)} h`;
}

export function AutomaticIntegrationPanel() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['automatic-panel'],
    queryFn: api.automaticPanel,
    refetchInterval: PASS_INTERVAL_MS,
    retry: false,
  });

  if (isLoading) {
    return (
      <Frame>
        <CircularProgress size={18} />
      </Frame>
    );
  }
  if (error || !data) {
    // 404: o ERP gravado ainda não é um que busca sozinho (ex.: acabou de ser escolhido na tela).
    const notSaved = (error as Error | null)?.message.startsWith('404');
    return (
      <Frame>
        <Typography variant="body2" color="text.secondary">
          {notSaved ? 'Salve as configurações para ver a situação.' : 'Não foi possível carregar a situação.'}
        </Typography>
      </Frame>
    );
  }

  const cursor = data.cursor;
  const waiting = cursor?.notBefore && new Date(cursor.notBefore).getTime() > Date.now() ? cursor.notBefore : null;

  return (
    <Frame>
      {!cursor && (
        <Typography variant="body2" color="text.secondary">
          Nenhuma busca feita ainda.
        </Typography>
      )}

      {cursor && (
        <>
          <Row label="Última busca">
            {cursor.lastPolledAt ? `${formatInstant(cursor.lastPolledAt)} (${ago(cursor.lastPolledAt)})` : '—'}
          </Row>
          <Row label="Falhas consecutivas">{cursor.consecutiveFailures}</Row>
          {cursor.watermark && <Row label="Sincronizado até">{formatInstant(cursor.watermark)}</Row>}
          {waiting && <Row label="Aguardando o ERP até">{formatInstant(waiting)}</Row>}
          {cursor.consecutiveFailures > 0 && cursor.lastError && (
            <Alert severity="error" sx={{ mt: 0.5 }}>
              <strong>Último erro:</strong> {cursor.lastError}
            </Alert>
          )}
        </>
      )}

      {/* Sem marca, o ponto de partida decide de onde a primeira busca começa; com marca, ele não vale mais. */}
      {!cursor?.watermark &&
        (data.startFrom ? (
          <Typography variant="body2">A primeira busca começa em {formatInstant(data.startFrom)}.</Typography>
        ) : (
          <Alert severity="warning" sx={{ mt: 0.5 }}>
            A primeira busca começa no momento em que rodar. Notas alteradas antes disso não serão buscadas.
          </Alert>
        ))}

      {/* Buscar de novo só existe depois da primeira busca (D7). */}
      {cursor?.watermark && <RewindMark watermark={cursor.watermark} />}
    </Frame>
  );
}

function Frame({ children }: { children: ReactNode }) {
  return (
    <Box
      sx={{
        border: 1,
        borderColor: 'divider',
        borderRadius: 2,
        p: 2,
        display: 'flex',
        flexDirection: 'column',
        gap: 0.75,
      }}
    >
      <Typography variant="subtitle2">Situação</Typography>
      {children}
    </Box>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Typography variant="body2">
      <Box component="span" sx={{ color: 'text.secondary', mr: 1 }}>
        {label}:
      </Box>
      {children}
    </Typography>
  );
}
