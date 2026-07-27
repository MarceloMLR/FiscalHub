import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import FormControlLabel from '@mui/material/FormControlLabel';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useConnector } from './useConnector';
import { INBOUND_ADAPTERS, OUTBOUND_ADAPTERS, ENVIRONMENTS, type AdapterField } from './adapterSchemas';

type Values = Record<string, string>;

function parseObj(json: string): Record<string, unknown> {
  try {
    return JSON.parse(json || '{}') as Record<string, unknown>;
  } catch {
    return {};
  }
}

function pick(schema: AdapterField[], values: Values): Values {
  return Object.fromEntries(schema.map((f) => [f.key, values[f.key] ?? '']));
}

// Renderiza os campos de um adapter num grid, lendo/escrevendo num objeto de valores.
function Fields({ schema, values, onChange }: { schema: AdapterField[]; values: Values; onChange: (v: Values) => void }) {
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
      {schema.map((f) => (
        <TextField
          key={f.key}
          label={f.label}
          size="small"
          value={values[f.key] ?? ''}
          onChange={(e) => onChange({ ...values, [f.key]: e.target.value })}
          placeholder={f.placeholder}
          sx={{ gridColumn: f.key === 'baseUrl' || f.key === 'url' ? '1 / -1' : undefined }}
        />
      ))}
    </Box>
  );
}

export function ConnectorsPage() {
  const qc = useQueryClient();
  const { data, isLoading, isError } = useConnector();

  const [tab, setTab] = useState(0);
  const [environment, setEnvironment] = useState('Sandbox');
  const [realtime, setRealtime] = useState(false);
  const [inboundAdapter, setInboundAdapter] = useState('Dynamics365');
  const [outboundAdapter, setOutboundAdapter] = useState('Avalara');
  const [inboundValues, setInboundValues] = useState<Values>({});
  const [sandboxValues, setSandboxValues] = useState<Values>({});
  const [productionValues, setProductionValues] = useState<Values>({});

  useEffect(() => {
    if (!data) {
      return;
    }
    setEnvironment(data.environment);
    setRealtime(data.realtime);
    setInboundAdapter(data.inboundAdapter in INBOUND_ADAPTERS ? data.inboundAdapter : 'Dynamics365');
    setOutboundAdapter(data.outboundAdapter in OUTBOUND_ADAPTERS ? data.outboundAdapter : 'Avalara');
    setInboundValues(parseObj(data.inboundSettings) as Values);
    const out = parseObj(data.outboundSettings);
    setSandboxValues((out.sandbox as Values) ?? {});
    setProductionValues((out.production as Values) ?? {});
  }, [data]);

  const save = useMutation({
    mutationFn: () => {
      const inSchema = INBOUND_ADAPTERS[inboundAdapter] ?? [];
      const outSchema = OUTBOUND_ADAPTERS[outboundAdapter] ?? [];
      return api.saveConnector({
        environment,
        realtime,
        inboundAdapter,
        inboundSettings: JSON.stringify(pick(inSchema, inboundValues)),
        outboundAdapter,
        outboundSettings: JSON.stringify({
          sandbox: pick(outSchema, sandboxValues),
          production: pick(outSchema, productionValues),
        }),
      });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['connector'] });
      qc.invalidateQueries({ queryKey: ['info'] });
    },
  });

  if (isError) {
    return (
      <Box sx={{ p: 3 }}>
        <Alert severity="error">Falha ao carregar o perfil de conector.</Alert>
      </Box>
    );
  }
  if (isLoading || !data) {
    return (
      <Box sx={{ p: 3, display: 'flex', justifyContent: 'center' }}>
        <CircularProgress />
      </Box>
    );
  }

  const outSchema = OUTBOUND_ADAPTERS[outboundAdapter] ?? [];

  return (
    <Box sx={{ p: 3, maxWidth: 900, mx: 'auto' }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="h6" sx={{ mb: 0.5 }}>
          Perfil de conector
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          Como este tenant integra. Escolha os adapters e preencha os campos de cada um. Segredos entram
          como <strong>referência</strong> (<code>kv:...</code>), nunca o valor — resolvidos no Key Vault.
        </Typography>

        <TextField
          select
          label="Ambiente ativo"
          size="small"
          value={environment}
          onChange={(e) => setEnvironment(e.target.value)}
          sx={{ minWidth: 220, mb: 2 }}
        >
          {ENVIRONMENTS.map((e) => (
            <MenuItem key={e} value={e}>
              {e}
            </MenuItem>
          ))}
        </TextField>

        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: 1, borderColor: 'divider', mb: 2.5 }}>
          <Tab label="Entrada (ERP)" />
          <Tab label="Saída (compliance)" />
        </Tabs>

        {tab === 0 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
              <TextField
                select
                label="ERP"
                size="small"
                value={inboundAdapter}
                onChange={(e) => setInboundAdapter(e.target.value)}
                sx={{ minWidth: 220 }}
              >
                {Object.keys(INBOUND_ADAPTERS).map((name) => (
                  <MenuItem key={name} value={name}>
                    {name}
                  </MenuItem>
                ))}
              </TextField>
              <FormControlLabel
                control={<Switch checked={realtime} onChange={(e) => setRealtime(e.target.checked)} />}
                label="Integração em tempo real"
              />
            </Box>
            <Fields schema={INBOUND_ADAPTERS[inboundAdapter] ?? []} values={inboundValues} onChange={setInboundValues} />
          </Box>
        )}

        {tab === 1 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <TextField
              select
              label="Plataforma"
              size="small"
              value={outboundAdapter}
              onChange={(e) => setOutboundAdapter(e.target.value)}
              sx={{ minWidth: 220 }}
            >
              {Object.keys(OUTBOUND_ADAPTERS).map((name) => (
                <MenuItem key={name} value={name}>
                  {name}
                </MenuItem>
              ))}
            </TextField>

            <Typography variant="subtitle2" color="text.secondary">
              Sandbox
            </Typography>
            <Fields schema={outSchema} values={sandboxValues} onChange={setSandboxValues} />

            <Typography variant="subtitle2" color="text.secondary" sx={{ mt: 1 }}>
              Produção
            </Typography>
            <Fields schema={outSchema} values={productionValues} onChange={setProductionValues} />
          </Box>
        )}

        <Box sx={{ mt: 3 }}>
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
