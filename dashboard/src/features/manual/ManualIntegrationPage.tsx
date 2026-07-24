import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import CircularProgress from '@mui/material/CircularProgress';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import { useMutation } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useCompanies, useBranches } from './useDirectory';

const ALL_BRANCHES = '__all__';

// yyyy-MM-dd no fuso local (evita o -1 dia que toISOString causa).
function fmt(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

function firstOfPreviousMonth(): string {
  const n = new Date();
  return fmt(new Date(n.getFullYear(), n.getMonth() - 1, 1));
}

export function ManualIntegrationPage() {
  const companies = useCompanies();
  const [company, setCompany] = useState('');
  const [branch, setBranch] = useState(ALL_BRANCHES);
  const [start, setStart] = useState(firstOfPreviousMonth());
  const [end, setEnd] = useState(fmt(new Date()));
  const branches = useBranches(company);

  const mutation = useMutation({
    mutationFn: () =>
      api.runManualIntegration({
        companyCode: company,
        branchCode: branch === ALL_BRANCHES ? null : branch,
        periodStart: `${start}T00:00:00-03:00`,
        periodEnd: `${end}T23:59:59-03:00`,
      }),
  });

  const periodInvalid = start !== '' && end !== '' && start > end;
  const canSubmit = company !== '' && start !== '' && end !== '' && !periodInvalid && !mutation.isPending;

  function onCompanyChange(code: string) {
    setCompany(code);
    setBranch(ALL_BRANCHES); // filial pertence a empresa; troca zera pra "Todas"
    mutation.reset();
  }

  return (
    <Box sx={{ p: 3, maxWidth: 720 }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="subtitle1" sx={{ mb: 0.5 }}>
          Carregar um período
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          Escolha a empresa, a filial e o intervalo. As notas do período são buscadas na origem e
          enfileiradas para integração. Recarregar o mesmo período reprocessa as notas — inclusive as
          já finalizadas.
        </Typography>

        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField
            select
            label="Empresa"
            value={company}
            onChange={(e) => onCompanyChange(e.target.value)}
            size="small"
            fullWidth
            disabled={companies.isLoading}
            helperText={companies.isError ? 'Falha ao carregar empresas.' : ' '}
            error={companies.isError}
            sx={{ gridColumn: '1 / -1' }}
          >
            {(companies.data ?? []).map((c) => (
              <MenuItem key={c.code} value={c.code}>
                {c.name} · {c.code}
              </MenuItem>
            ))}
          </TextField>

          <TextField
            select
            label="Filial"
            value={branch}
            onChange={(e) => setBranch(e.target.value)}
            size="small"
            fullWidth
            disabled={company === '' || branches.isLoading}
            helperText=" "
          >
            <MenuItem value={ALL_BRANCHES}>Todas as filiais</MenuItem>
            {(branches.data ?? []).map((b) => (
              <MenuItem key={b.code} value={b.code}>
                {b.name} · {b.code}
              </MenuItem>
            ))}
          </TextField>

          <Box />

          <TextField
            type="date"
            label="Início"
            value={start}
            onChange={(e) => setStart(e.target.value)}
            size="small"
            fullWidth
            InputLabelProps={{ shrink: true }}
            error={periodInvalid}
            helperText=" "
          />
          <TextField
            type="date"
            label="Fim"
            value={end}
            onChange={(e) => setEnd(e.target.value)}
            size="small"
            fullWidth
            InputLabelProps={{ shrink: true }}
            error={periodInvalid}
            helperText={periodInvalid ? 'O início deve ser anterior ao fim.' : ' '}
          />
        </Box>

        <Box sx={{ mt: 1.5 }}>
          <Button
            variant="contained"
            disableElevation
            startIcon={
              mutation.isPending ? <CircularProgress size={16} color="inherit" /> : <BoltOutlinedIcon />
            }
            disabled={!canSubmit}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? 'Integrando…' : 'Integrar período'}
          </Button>
        </Box>

        {mutation.isError && (
          <Alert severity="error" sx={{ mt: 2.5 }}>
            Falha ao disparar: {(mutation.error as Error)?.message}. O host está rodando na 5200?
          </Alert>
        )}

        {mutation.isSuccess && (
          <Alert severity={mutation.data.discovered > 0 ? 'success' : 'info'} sx={{ mt: 2.5 }}>
            <AlertTitle>
              {mutation.data.discovered > 0
                ? `${mutation.data.discovered} nota(s) enfileirada(s)`
                : 'Nenhuma nota no período'}
            </AlertTitle>
            {mutation.data.discovered > 0
              ? 'Acompanhe o processamento na tela de Documentos.'
              : 'Ajuste o período ou a empresa e tente de novo.'}
          </Alert>
        )}
      </Paper>
    </Box>
  );
}
