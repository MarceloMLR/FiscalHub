import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import CircularProgress from '@mui/material/CircularProgress';
import Table from '@mui/material/Table';
import TableHead from '@mui/material/TableHead';
import TableBody from '@mui/material/TableBody';
import TableRow from '@mui/material/TableRow';
import TableCell from '@mui/material/TableCell';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import AddOutlinedIcon from '@mui/icons-material/AddOutlined';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useCompanies, useBranches } from '../manual/useDirectory';
import { useSchedules, useExecutions } from '../schedules/useScheduling';
import type { CreateScheduleRequest, IntegrationModeName } from '../../types';

const ALL_BRANCHES = '__all__';

const MODE_LABEL: Record<IntegrationModeName, string> = {
  Manual: 'Imediata',
  ScheduledDaily: 'Diária (D-1)',
  ScheduledOnce: 'Agendada',
};

type Mode = 'now' | 'daily' | 'once';

function fmt(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

function firstOfPreviousMonth(): string {
  const n = new Date();
  return fmt(new Date(n.getFullYear(), n.getMonth() - 1, 1));
}

function dateTime(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

export function IntegrationsPage() {
  const qc = useQueryClient();
  const companies = useCompanies();
  const schedules = useSchedules();
  const executions = useExecutions();

  const [mode, setMode] = useState<Mode>('now');
  const [scope, setScope] = useState<'period' | 'note'>('period');
  const [company, setCompany] = useState('');
  const [branch, setBranch] = useState(ALL_BRANCHES);
  const [documentNumber, setDocumentNumber] = useState('');
  const [start, setStart] = useState(firstOfPreviousMonth());
  const [end, setEnd] = useState(fmt(new Date()));
  const [timeOfDay, setTimeOfDay] = useState('06:00');
  const [runAt, setRunAt] = useState('');
  const branches = useBranches(company);

  const branchCode = () => (branch === ALL_BRANCHES ? null : branch);

  const runNow = useMutation({
    mutationFn: () =>
      api.runManualIntegration({
        companyCode: company,
        branchCode: branchCode(),
        documentNumber: scope === 'note' ? documentNumber : null,
        periodStart: `${start}T00:00:00-03:00`,
        periodEnd: `${end}T23:59:59-03:00`,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['executions'] }),
  });

  const createSchedule = useMutation({
    mutationFn: () => {
      const body: CreateScheduleRequest =
        mode === 'daily'
          ? { mode: 'ScheduledDaily', companyCode: company, branchCode: branchCode(), timeOfDay }
          : {
              mode: 'ScheduledOnce',
              companyCode: company,
              branchCode: branchCode(),
              runAt: `${runAt}:00-03:00`,
              periodStart: `${start}T00:00:00-03:00`,
              periodEnd: `${end}T23:59:59-03:00`,
            };
      return api.createSchedule(body);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schedules'] }),
  });

  const deactivate = useMutation({
    mutationFn: (id: number) => api.deactivateSchedule(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schedules'] }),
  });

  const onModeChange = (m: Mode) => {
    setMode(m);
    runNow.reset();
    createSchedule.reset();
  };

  const onCompanyChange = (code: string) => {
    setCompany(code);
    setBranch(ALL_BRANCHES);
    runNow.reset();
    createSchedule.reset();
  };

  const periodOk = start !== '' && end !== '' && start <= end;
  const pending = runNow.isPending || createSchedule.isPending;
  const canSubmit =
    company !== '' &&
    !pending &&
    (mode === 'now'
      ? periodOk && (scope === 'period' || documentNumber.trim() !== '')
      : mode === 'daily'
        ? timeOfDay !== ''
        : runAt !== '' && periodOk);

  const submit = () => (mode === 'now' ? runNow.mutate() : createSchedule.mutate());

  return (
    <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2.5, maxWidth: 1040, mx: 'auto' }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="h6" sx={{ mb: 0.5 }}>
          Nova integração
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          <strong>Imediata</strong> dispara agora — um período inteiro ou uma nota pelo número.
          <strong> Diária (D-1)</strong> roda todo dia processando o dia anterior.
          <strong> Agendada</strong> roda uma vez, na data/hora marcada.
        </Typography>

        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField select label="Modo" size="small" value={mode} onChange={(e) => onModeChange(e.target.value as Mode)}>
            <MenuItem value="now">Imediata (agora)</MenuItem>
            <MenuItem value="daily">Agendada — diária (D-1)</MenuItem>
            <MenuItem value="once">Agendada — única</MenuItem>
          </TextField>

          <TextField
            select
            label="Empresa"
            size="small"
            value={company}
            onChange={(e) => onCompanyChange(e.target.value)}
            disabled={companies.isLoading}
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
            size="small"
            value={branch}
            onChange={(e) => setBranch(e.target.value)}
            disabled={company === '' || branches.isLoading}
          >
            <MenuItem value={ALL_BRANCHES}>Todas as filiais</MenuItem>
            {(branches.data ?? []).map((b) => (
              <MenuItem key={b.code} value={b.code}>
                {b.name} · {b.code}
              </MenuItem>
            ))}
          </TextField>

          {mode === 'now' && (
            <TextField select label="Escopo" size="small" value={scope} onChange={(e) => setScope(e.target.value as 'period' | 'note')}>
              <MenuItem value="period">Período inteiro</MenuItem>
              <MenuItem value="note">Nota específica</MenuItem>
            </TextField>
          )}

          {mode === 'now' && scope === 'note' && (
            <TextField
              label="Número da nota (nNF)"
              size="small"
              value={documentNumber}
              onChange={(e) => setDocumentNumber(e.target.value)}
              placeholder="ex.: 456"
            />
          )}

          {mode === 'daily' && (
            <TextField
              type="time"
              label="Horário"
              size="small"
              value={timeOfDay}
              onChange={(e) => setTimeOfDay(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          )}

          {mode === 'once' && (
            <TextField
              type="datetime-local"
              label="Disparar em"
              size="small"
              value={runAt}
              onChange={(e) => setRunAt(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          )}

          {mode !== 'daily' && (
            <>
              <TextField
                type="date"
                label="Período — início"
                size="small"
                value={start}
                onChange={(e) => setStart(e.target.value)}
                InputLabelProps={{ shrink: true }}
                error={!periodOk}
              />
              <TextField
                type="date"
                label="Período — fim"
                size="small"
                value={end}
                onChange={(e) => setEnd(e.target.value)}
                InputLabelProps={{ shrink: true }}
                error={!periodOk}
                helperText={!periodOk ? 'O início deve ser anterior ao fim.' : ' '}
              />
            </>
          )}
        </Box>

        <Box sx={{ mt: 1.5 }}>
          <Button
            variant="contained"
            disableElevation
            startIcon={
              pending ? (
                <CircularProgress size={16} color="inherit" />
              ) : mode === 'now' ? (
                <BoltOutlinedIcon />
              ) : (
                <AddOutlinedIcon />
              )
            }
            disabled={!canSubmit}
            onClick={submit}
          >
            {mode === 'now' ? 'Integrar agora' : 'Criar agendamento'}
          </Button>
        </Box>

        {runNow.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            Falha ao integrar: {(runNow.error as Error)?.message}.
          </Alert>
        )}
        {runNow.isSuccess && (
          <Alert severity={runNow.data.discovered > 0 ? 'success' : 'info'} sx={{ mt: 2 }}>
            <AlertTitle>
              {runNow.data.discovered > 0 ? `${runNow.data.discovered} nota(s) enfileirada(s)` : 'Nenhuma nota encontrada'}
            </AlertTitle>
            {runNow.data.discovered > 0 ? 'Acompanhe em Documentos.' : 'Ajuste empresa, período ou número.'}
          </Alert>
        )}
        {createSchedule.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            Falha ao agendar: {(createSchedule.error as Error)?.message}.
          </Alert>
        )}
        {createSchedule.isSuccess && (
          <Alert severity="success" sx={{ mt: 2 }}>
            Agendamento criado. O host executa no horário; acompanhe nas execuções abaixo.
          </Alert>
        )}
      </Paper>

      <Paper elevation={0} sx={{ borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
        <Typography variant="subtitle1" sx={{ p: 2, pb: 1.5 }}>
          Agendamentos
        </Typography>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { color: 'text.secondary', fontWeight: 600, bgcolor: '#fafbfc' } }}>
              <TableCell>Tipo</TableCell>
              <TableCell>Empresa</TableCell>
              <TableCell>Filial</TableCell>
              <TableCell>Próximo disparo</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Ação</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(schedules.data ?? []).map((s) => (
              <TableRow key={s.id} hover>
                <TableCell>{MODE_LABEL[s.mode]}</TableCell>
                <TableCell sx={{ fontFamily: 'ui-monospace, monospace' }}>{s.companyCode}</TableCell>
                <TableCell>{s.branchCode ?? 'Todas'}</TableCell>
                <TableCell>{dateTime(s.nextRunAt)}</TableCell>
                <TableCell>
                  <Chip
                    size="small"
                    label={s.active ? 'Ativo' : 'Inativo'}
                    sx={{ bgcolor: s.active ? '#e7f6ec' : '#f1f2f4', color: s.active ? '#15803d' : '#6b7280', fontWeight: 600 }}
                  />
                </TableCell>
                <TableCell align="right">
                  {s.active && (
                    <Button size="small" color="inherit" onClick={() => deactivate.mutate(s.id)} disabled={deactivate.isPending}>
                      Desativar
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {(schedules.data?.length ?? 0) === 0 && (
              <TableRow>
                <TableCell colSpan={6} sx={{ color: 'text.secondary', py: 3, textAlign: 'center' }}>
                  Nenhum agendamento ainda.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Paper>

      <Paper elevation={0} sx={{ borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
        <Typography variant="subtitle1" sx={{ p: 2, pb: 1.5 }}>
          Execuções recentes
        </Typography>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { color: 'text.secondary', fontWeight: 600, bgcolor: '#fafbfc' } }}>
              <TableCell>Modo</TableCell>
              <TableCell>Empresa</TableCell>
              <TableCell>Filial</TableCell>
              <TableCell>Período</TableCell>
              <TableCell align="right">Notas</TableCell>
              <TableCell>Quando</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(executions.data ?? []).map((e) => (
              <TableRow key={e.id} hover>
                <TableCell>{MODE_LABEL[e.mode]}</TableCell>
                <TableCell sx={{ fontFamily: 'ui-monospace, monospace' }}>{e.companyCode}</TableCell>
                <TableCell>{e.branchCode ?? 'Todas'}</TableCell>
                <TableCell>
                  {e.periodStart} → {e.periodEnd}
                </TableCell>
                <TableCell align="right">{e.discoveredCount}</TableCell>
                <TableCell>{dateTime(e.runAt)}</TableCell>
              </TableRow>
            ))}
            {(executions.data?.length ?? 0) === 0 && (
              <TableRow>
                <TableCell colSpan={6} sx={{ color: 'text.secondary', py: 3, textAlign: 'center' }}>
                  Nenhuma execução ainda.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Paper>
    </Box>
  );
}
