import { useState } from 'react';
import Box from '@mui/material/Box';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import { useTrace } from './useTrace';
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

  return (
    <Box>
      <Typography variant="subtitle1" sx={{ px: 2, pt: 2, fontWeight: 700 }}>
        {doc.naturalKey}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ px: 2, pb: 1 }}>
        Rastreabilidade: fonte &rarr; domínio &rarr; destino
      </Typography>

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
            <Tab label="Fonte (XML)" />
            <Tab label="Domínio" />
            <Tab label={data.destination ? `Destino (${data.destination.name})` : 'Destino'} />
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
