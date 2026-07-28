import { useState } from 'react';
import Box from '@mui/material/Box';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import ReplayIcon from '@mui/icons-material/Replay';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useTrace } from './useTrace';
import { isFailure } from './StatusChip';
import type { DocumentSummary } from '../../types';

function Code({ children }: { children: string }) {
  return (
    <Box
      component="pre"
      sx={{
        m: 0,
        p: 2,
        bgcolor: 'grey.900',
        color: 'grey.100',
        borderRadius: 1,
        fontSize: 13,
        overflow: 'auto',
        maxHeight: 460,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
      }}
    >
      {children}
    </Box>
  );
}

const pretty = (v: unknown) => (typeof v === 'string' ? v : JSON.stringify(v, null, 2));

export function DocumentDetail({ doc }: { doc: DocumentSummary }) {
  const { data, isLoading, isError } = useTrace(doc.tenantId, doc.naturalKey);
  const [tab, setTab] = useState(0);
  const qc = useQueryClient();

  // Reprocessar: entrega o id ao adapter de entrada, que rebusca na origem e reintegra.
  const reprocess = useMutation({
    mutationFn: () => api.reprocess(doc.tenantId, doc.naturalKey),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['groups'] });
      qc.invalidateQueries({ queryKey: ['groupDocuments'] });
      qc.invalidateQueries({ queryKey: ['documents'] });
    },
  });

  return (
    <Box>
      <Box sx={{ px: 2, pt: 2, pb: 1, display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 2 }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle1" className="fh-mono" sx={{ fontWeight: 700, wordBreak: 'break-all' }}>
            {doc.naturalKey}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Rastreabilidade: origem &rarr; domínio &rarr; destino
          </Typography>
        </Box>
        {isFailure(doc.status) && (
          <button
            type="button"
            className="fh-btn"
            onClick={() => reprocess.mutate()}
            disabled={reprocess.isPending || reprocess.isSuccess}
            style={{ height: 32, flexShrink: 0 }}
          >
            <ReplayIcon sx={{ fontSize: 16 }} />
            {reprocess.isPending ? 'Reprocessando…' : reprocess.isSuccess ? 'Reenviado' : 'Reprocessar'}
          </button>
        )}
      </Box>

      {reprocess.isSuccess && (
        <Box sx={{ mx: 2, mb: 1, border: '1px solid var(--ok-border)', background: 'var(--ok-bg)', color: 'var(--ok-text)', borderRadius: 2, px: 1.5, py: 1, fontSize: 13 }}>
          Nota reenviada à origem para reprocessar. O status atualiza em instantes.
        </Box>
      )}
      {reprocess.isError && (
        <Box sx={{ mx: 2, mb: 1, border: '1px solid var(--error-border)', background: 'var(--error-bg)', color: 'var(--error-text)', borderRadius: 2, px: 1.5, py: 1, fontSize: 13 }}>
          Não foi possível reprocessar: {(reprocess.error as Error)?.message}.
        </Box>
      )}

      {isLoading && (
        <Box sx={{ p: 3 }}>
          <CircularProgress size={24} />
        </Box>
      )}

      {(isError || (!isLoading && !data)) && (
        <Typography sx={{ p: 3 }} color="text.secondary">
          Sem fotos para este documento ainda.
        </Typography>
      )}

      {data && (
        <>
          <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="fullWidth">
            <Tab label="Origem" />
            <Tab label="Domínio" />
            <Tab label="Destino" />
          </Tabs>
          <Box sx={{ p: 2 }}>
            {tab === 0 &&
              (data.source ? <Code>{data.source}</Code> : <Empty />)}
            {tab === 1 &&
              (data.domain !== undefined ? <Code>{pretty(data.domain)}</Code> : <Empty />)}
            {tab === 2 &&
              (data.destination ? <Code>{pretty(data.destination.payload)}</Code> : <Empty />)}
          </Box>
        </>
      )}
    </Box>
  );
}

function Empty() {
  return <Typography color="text.secondary">Sem esta foto.</Typography>;
}
