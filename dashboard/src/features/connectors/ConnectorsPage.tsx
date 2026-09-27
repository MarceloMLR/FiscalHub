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
import type { SecretStatus } from '../../types';

// Settings como vieram do servidor (sem segredos e sem referências). Os campos que a tela não mostra
// (establishments, companies, poll…) ficam aqui e voltam intactos ao salvar.
type Json = Record<string, unknown>;
// Segredos digitados nesta edição, por caminho (`outbound.sandbox.clientSecret`). Nunca vêm do servidor.
type Typed = Record<string, string>;

function parseObj(json: string): Json {
  try {
    const value = JSON.parse(json || '{}') as unknown;
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Json) : {};
  } catch {
    return {};
  }
}

function asObj(value: unknown): Json {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Json) : {};
}

function getPath(obj: Json, path: string): string {
  const value = path.split('.').reduce<unknown>((cur, k) => asObj(cur)[k], obj);
  return typeof value === 'string' || typeof value === 'number' ? String(value) : '';
}

function setPath(obj: Json, path: string, value: string): Json {
  const [head, ...rest] = path.split('.');
  if (rest.length === 0) {
    return { ...obj, [head]: value };
  }
  return { ...obj, [head]: setPath(asObj(obj[head]), rest.join('.'), value) };
}

// Aplica os segredos digitados: só o que foi digitado vai; o resto o servidor mantém como estava.
function withTyped(schema: AdapterField[], values: Json, prefix: string, typed: Typed): Json {
  return schema
    .filter((f) => f.secret && typed[prefix + f.key])
    .reduce((acc, f) => setPath(acc, f.key, typed[prefix + f.key]), values);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

function secretHelp(status: SecretStatus | undefined): string {
  if (!status?.configured) {
    return 'não configurado';
  }
  return status.updatedOn ? `configurado em ${formatDate(status.updatedOn)}` : 'configurado';
}

interface FieldsProps {
  schema: AdapterField[];
  values: Json;
  onChange: (v: Json) => void;
  prefix: string; // caminho do segredo no mapa `secrets` (ex.: 'outbound.sandbox.')
  secrets: Record<string, SecretStatus>;
  typed: Typed;
  onType: (t: Typed) => void;
}

// Renderiza os campos de um adapter num grid. Segredo é campo de senha, sem preenchimento: mostra se
// está configurado e quando, e só manda um valor novo quando digitado.
function Fields({ schema, values, onChange, prefix, secrets, typed, onType }: FieldsProps) {
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
      {schema.map((f) =>
        f.secret ? (
          <TextField
            key={f.key}
            label={f.label}
            size="small"
            type="password"
            autoComplete="new-password"
            value={typed[prefix + f.key] ?? ''}
            onChange={(e) => onType({ ...typed, [prefix + f.key]: e.target.value })}
            placeholder={secrets[prefix + f.key]?.configured ? 'digite para trocar' : 'digite o valor'}
            helperText={secretHelp(secrets[prefix + f.key])}
          />
        ) : (
          <TextField
            key={f.key}
            label={f.label}
            size="small"
            value={getPath(values, f.key)}
            onChange={(e) => onChange(setPath(values, f.key, e.target.value))}
            placeholder={f.placeholder}
            sx={{ gridColumn: f.key === 'baseUrl' || f.key === 'url' ? '1 / -1' : undefined }}
          />
        ),
      )}
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
  const [inboundValues, setInboundValues] = useState<Json>({});
  const [outboundRest, setOutboundRest] = useState<Json>({});
  const [sandboxValues, setSandboxValues] = useState<Json>({});
  const [productionValues, setProductionValues] = useState<Json>({});
  const [typed, setTyped] = useState<Typed>({});

  const loadOutbound = (json: string) => {
    const { sandbox, production, ...rest } = parseObj(json);
    setOutboundRest(rest);
    setSandboxValues(asObj(sandbox));
    setProductionValues(asObj(production));
  };

  useEffect(() => {
    if (!data) {
      return;
    }
    setEnvironment(data.environment);
    setRealtime(data.realtime);
    setInboundAdapter(data.inboundAdapter in INBOUND_ADAPTERS ? data.inboundAdapter : 'Dynamics365');
    setOutboundAdapter(data.outboundAdapter in OUTBOUND_ADAPTERS ? data.outboundAdapter : 'Avalara');
    setInboundValues(parseObj(data.inboundSettings));
    loadOutbound(data.outboundSettings);
    setTyped({});
  }, [data]);

  // Trocar de adapter começa de settings vazias: as do adapter anterior são de outro schema. Voltar ao
  // adapter gravado recupera as settings gravadas.
  const dropTyped = (kind: string) =>
    setTyped((t) => Object.fromEntries(Object.entries(t).filter(([k]) => !k.startsWith(kind))));
  const changeInbound = (name: string) => {
    setInboundAdapter(name);
    setInboundValues(name === data?.inboundAdapter ? parseObj(data.inboundSettings) : {});
    dropTyped('inbound.');
  };
  const changeOutbound = (name: string) => {
    setOutboundAdapter(name);
    loadOutbound(name === data?.outboundAdapter ? data.outboundSettings : '{}');
    dropTyped('outbound.');
  };

  const save = useMutation({
    mutationFn: () => {
      const inSchema = INBOUND_ADAPTERS[inboundAdapter] ?? [];
      const outSchema = OUTBOUND_ADAPTERS[outboundAdapter] ?? [];
      return api.saveConnector({
        environment,
        realtime,
        inboundAdapter,
        inboundSettings: JSON.stringify(withTyped(inSchema, inboundValues, 'inbound.', typed)),
        outboundAdapter,
        outboundSettings: JSON.stringify({
          ...outboundRest,
          sandbox: withTyped(outSchema, sandboxValues, 'outbound.sandbox.', typed),
          production: withTyped(outSchema, productionValues, 'outbound.production.', typed),
        }),
      });
    },
    onSuccess: () => {
      setTyped({}); // o valor digitado não fica na tela depois de gravado
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
          Como este tenant integra. Escolha os adapters e preencha os campos de cada um. Segredos são
          digitados aqui e vão direto para o <strong>cofre</strong>: a tela nunca os mostra de volta, só se
          estão configurados e quando.
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
                onChange={(e) => changeInbound(e.target.value)}
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
            <Fields
              schema={INBOUND_ADAPTERS[inboundAdapter] ?? []}
              values={inboundValues}
              onChange={setInboundValues}
              prefix="inbound."
              secrets={data.secrets ?? {}}
              typed={typed}
              onType={setTyped}
            />
          </Box>
        )}

        {tab === 1 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <TextField
              select
              label="Plataforma"
              size="small"
              value={outboundAdapter}
              onChange={(e) => changeOutbound(e.target.value)}
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
            <Fields
              schema={outSchema}
              values={sandboxValues}
              onChange={setSandboxValues}
              prefix="outbound.sandbox."
              secrets={data.secrets ?? {}}
              typed={typed}
              onType={setTyped}
            />

            <Typography variant="subtitle2" color="text.secondary" sx={{ mt: 1 }}>
              Produção
            </Typography>
            <Fields
              schema={outSchema}
              values={productionValues}
              onChange={setProductionValues}
              prefix="outbound.production."
              secrets={data.secrets ?? {}}
              typed={typed}
              onType={setTyped}
            />
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
            Falha ao salvar: {(save.error as Error)?.message}
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
