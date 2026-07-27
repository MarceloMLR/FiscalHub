import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import FormControlLabel from '@mui/material/FormControlLabel';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useConnector } from './useConnector';
import type { ConnectorProfile } from '../../types';

export function ConnectorsPage() {
  const qc = useQueryClient();
  const { data, isLoading, isError } = useConnector();
  const [form, setForm] = useState<ConnectorProfile | null>(null);

  useEffect(() => {
    if (data) {
      setForm(data);
    }
  }, [data]);

  const save = useMutation({
    mutationFn: () =>
      api.saveConnector({
        environment: form!.environment,
        realtime: form!.realtime,
        inboundAdapter: form!.inboundAdapter,
        inboundSettings: form!.inboundSettings,
        outboundAdapter: form!.outboundAdapter,
        outboundSettings: form!.outboundSettings,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['connector'] });
      qc.invalidateQueries({ queryKey: ['info'] }); // o ambiente do badge vem do perfil
    },
  });

  if (isError) {
    return (
      <Box sx={{ p: 3 }}>
        <Alert severity="error">Falha ao carregar o perfil de conector.</Alert>
      </Box>
    );
  }

  if (isLoading || !form) {
    return (
      <Box sx={{ p: 3, display: 'flex', justifyContent: 'center' }}>
        <CircularProgress />
      </Box>
    );
  }

  const set = (patch: Partial<ConnectorProfile>) => setForm({ ...form, ...patch });

  return (
    <Box sx={{ p: 3, maxWidth: 900, mx: 'auto' }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="h6" sx={{ mb: 0.5 }}>
          Perfil de conector
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          Como este tenant integra: adapters de entrada (ERP) e saída (compliance), ambiente e settings.
          Segredos entram como <strong>referência</strong> (<code>kv:...</code>), nunca o valor — resolvidos
          no Key Vault.
        </Typography>

        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
          <TextField
            select
            label="Ambiente"
            size="small"
            value={form.environment}
            onChange={(e) => set({ environment: e.target.value })}
          >
            <MenuItem value="Sandbox">Sandbox</MenuItem>
            <MenuItem value="Production">Production</MenuItem>
          </TextField>

          <FormControlLabel
            control={<Switch checked={form.realtime} onChange={(e) => set({ realtime: e.target.checked })} />}
            label="Integração em tempo real"
            sx={{ ml: 0.5 }}
          />

          <TextField
            label="Adapter de entrada (ERP)"
            size="small"
            value={form.inboundAdapter}
            onChange={(e) => set({ inboundAdapter: e.target.value })}
            placeholder="Dynamics365, iScala…"
          />
          <TextField
            label="Adapter de saída (compliance)"
            size="small"
            value={form.outboundAdapter}
            onChange={(e) => set({ outboundAdapter: e.target.value })}
            placeholder="Avalara, ThomsonReuters…"
          />

          <TextField
            label="Settings da entrada (JSON)"
            size="small"
            value={form.inboundSettings}
            onChange={(e) => set({ inboundSettings: e.target.value })}
            multiline
            minRows={4}
            sx={{ gridColumn: '1 / -1', '& textarea': { fontFamily: 'ui-monospace, monospace', fontSize: 13 } }}
          />
          <TextField
            label="Settings da saída (JSON — por ambiente)"
            size="small"
            value={form.outboundSettings}
            onChange={(e) => set({ outboundSettings: e.target.value })}
            multiline
            minRows={5}
            sx={{ gridColumn: '1 / -1', '& textarea': { fontFamily: 'ui-monospace, monospace', fontSize: 13 } }}
          />
        </Box>

        <Box sx={{ mt: 2 }}>
          <Button
            variant="contained"
            disableElevation
            startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : <SaveOutlinedIcon />}
            disabled={save.isPending}
            onClick={() => save.mutate()}
          >
            Salvar
          </Button>
        </Box>

        {save.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            Falha ao salvar: {(save.error as Error)?.message}.
          </Alert>
        )}
        {save.isSuccess && (
          <Alert severity="success" sx={{ mt: 2 }}>
            Perfil salvo. Novas integrações deste tenant já usam esta config.
          </Alert>
        )}
      </Paper>
    </Box>
  );
}
