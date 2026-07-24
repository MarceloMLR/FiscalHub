import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import type { DocumentSummary, IntegrationStatus } from '../../types';

function count(docs: DocumentSummary[], status: IntegrationStatus) {
  return docs.filter((d) => d.status === status).length;
}

export function KpiCards({ docs }: { docs: DocumentSummary[] }) {
  const cards = [
    { label: 'Total', value: docs.length, color: 'text.primary' },
    { label: 'Finalizadas', value: count(docs, 'Confirmed'), color: 'success.main' },
    { label: 'Processando', value: count(docs, 'Submitted'), color: 'primary.main' },
    { label: 'Com erro', value: count(docs, 'IntegrationError') + count(docs, 'DeadLettered'), color: 'error.main' },
  ];

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.5, mb: 2.5 }}>
      {cards.map((c) => (
        <Paper key={c.label} elevation={0} sx={{ p: 2, borderRadius: 2.5, border: 1, borderColor: 'divider' }}>
          <Typography variant="body2" color="text.secondary">
            {c.label}
          </Typography>
          <Typography sx={{ fontSize: 26, fontWeight: 600, color: c.color, mt: 0.5, fontVariantNumeric: 'tabular-nums' }}>
            {c.value}
          </Typography>
        </Paper>
      ))}
    </Box>
  );
}
