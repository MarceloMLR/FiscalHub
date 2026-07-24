import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import Table from '@mui/material/Table';
import TableHead from '@mui/material/TableHead';
import TableBody from '@mui/material/TableBody';
import TableRow from '@mui/material/TableRow';
import TableCell from '@mui/material/TableCell';
import AddOutlinedIcon from '@mui/icons-material/AddOutlined';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useCompanies, useBranches } from '../manual/useDirectory';
import { useSchedules, useExecutions } from './useScheduling';
import type { CreateScheduleRequest, IntegrationModeName } from '../../types';

const ALL_BRANCHES = '__all__';

const MODE_LABEL: Record<IntegrationModeName, string> = {
  Manual: 'Manual',
  ScheduledDaily: 'Diária (D-1)',
  ScheduledOnce: 'Agendada',
};

function fmt(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

function dateTime(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

export function SchedulesPage() {
  const qc = useQueryClient();
  const companies = useCompanies();
  const schedules = useSchedules();
  const executions = useExecutions();

  const [mode, setMode] = useState<'ScheduledDaily' | 'ScheduledOnce'>('ScheduledDaily');
  const [company, setCompany] = useState('');
  const [branch, setBranch] = useState(ALL_BRANCHES);
  const [timeOfDay, setTimeOfDay] = useState('06:00');
  const [runAt, setRunAt] = useState('');
  const [start, setStart] = useState(fmt(new Date()));
  const [end, setEnd] = useState(fmt(new Date()));
  const branches = useBranches(company);

  const create = useMutation({
    mutationFn: () => {
      const branchCode = branch === ALL_BRANCHES ? null : branch;
      const body: CreateScheduleRequest =
        mode === 'ScheduledDaily'
          ? { mode, companyCode: company, branchCode, timeOfDay }
          : {
              mode,
              companyCode: company,
              branchCode,
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

  const onCompanyChange = (code: string) => {
    setCompany(code);
    setBranch(ALL_BRANCHES);
    create.reset();
  };

  const canSubmit =
    company !== '' &&
    (mode === 'ScheduledDaily' ? timeOfDay !== '' : runAt !== '' && start !== '' && end !== '' && start <= end) &&
    !create.isPending;

  return (
    <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 2.5, maxWidth: 1040, mx: 'auto' }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="h6" sx={{ mb: 0.5 }}>
          Novo agendamento
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          A <strong>diária (D-1)</strong> roda todo dia no horário escolhido, processando as notas do dia
          anterior. A <strong>agendada</strong> roda uma vez, na data/hora marcada, sobre o período informado.
        </Typography>

        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField select label="Tipo" size="small" value={mode} onChange={(e) => setMode(e.target.value as typeof mode)}>
            <MenuItem value="ScheduledDaily">Diária (D-1)</MenuItem>
            <MenuItem value="ScheduledOnce">Agendada (única)</MenuItem>
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

          {mode === 'ScheduledDaily' ? (
            <TextField
              type="time"
              label="Horário"
              size="small"
              value={timeOfDay}
              onChange={(e) => setTimeOfDay(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          ) : (
            <>
              <TextField
                type="datetime-local"
                label="Disparar em"
                size="small"
                value={runAt}
                onChange={(e) => setRunAt(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
              <TextField
                type="date"
                label="Período — início"
                size="small"
                value={start}
                onChange={(e) => setStart(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
              <TextField
                type="date"
                label="Período — fim"
                size="small"
                value={end}
                onChange={(e) => setEnd(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </>
          )}
        </Box>

        <Box sx={{ mt: 2 }}>
          <Button
            variant="contained"
            disableElevation
            startIcon={create.isPending ? <CircularProgress size={16} color="inherit" /> : <AddOutlinedIcon />}
            disabled={!canSubmit}
            onClick={() => create.mutate()}
          >
            Criar agendamento
          </Button>
        </Box>

        {create.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            Falha ao criar: {(create.error as Error)?.message}.
          </Alert>
        )}
        {create.isSuccess && (
          <Alert severity="success" sx={{ mt: 2 }}>
            Agendamento criado. O host executa no horário; acompanhe nas execuções abaixo.
          </Alert>
        )}
      </Paper>

      <Paper elevation={0} sx={{ p: 0, borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
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
                    sx={{
                      bgcolor: s.active ? '#e7f6ec' : '#f1f2f4',
                      color: s.active ? '#15803d' : '#6b7280',
                      fontWeight: 600,
                    }}
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

      <Paper elevation={0} sx={{ p: 0, borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
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
