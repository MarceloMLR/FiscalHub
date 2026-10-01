import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import MenuItem from '@mui/material/MenuItem';
import Button from '@mui/material/Button';
import Switch from '@mui/material/Switch';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import Checkbox from '@mui/material/Checkbox';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useConnector } from './useConnector';
import { AutomaticIntegrationPanel } from './AutomaticIntegrationPanel';
import { CredentialTestButton } from './CredentialTestButton';
import {
  INBOUND_ADAPTERS,
  OUTBOUND_ADAPTERS,
  ENVIRONMENTS,
  CREDENTIAL_TEST_ADAPTERS,
  SCANNING_INBOUND_ADAPTERS,
  type AdapterField,
} from './adapterSchemas';
import type { ModuleName, SecretStatus } from '../../types';
import {
  SECRET_MASK,
  asObj,
  buildConnectorPayload,
  formFromProfile,
  hasPendingEdit,
  getPath,
  parseObj,
  setPath,
  splitOutbound,
  type ConnectorForm,
  type Json,
  type Typed,
} from './connectorPayload';
import { DEFAULT_MODULES, MODULE_VIEWS } from '../modules/modules';

// Os módulos que o Admin marca, na ordem da barra lateral (o Agendamento não é módulo: aparece sempre).
const MODULE_CHOICES = MODULE_VIEWS.flatMap((m) => (m.module ? [{ module: m.module, label: m.label }] : []));

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
// está configurado e quando, e só manda um valor novo quando digitado. A máscara do segredo configurado é PLACEHOLDER, e
// nunca valor: o valor é só o digitado, e o payload só o leva quando não é vazio (connectorPayload, D3).
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
            placeholder={secrets[prefix + f.key]?.configured ? SECRET_MASK : 'digite o valor'}
            helperText={secretHelp(secrets[prefix + f.key])}
            // O MUI esconde o placeholder enquanto o rótulo está dentro do campo: sem isso, a máscara só aparece com o
            // campo em foco, e o segredo gravado parece ter sumido (prova manual, 2026-10-01).
            InputLabelProps={{ shrink: true }}
            // A máscara de um segredo configurado tem a cara de campo preenchido, com a cor do texto e não a cinza do
            // placeholder. Continua sendo placeholder: o valor do campo fica vazio, e o salvar sem digitar não a envia.
            sx={
              secrets[prefix + f.key]?.configured
                ? { '& .MuiInputBase-input::placeholder': { color: 'text.primary', opacity: 1 } }
                : undefined
            }
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
  const [inboundAdapter, setInboundAdapter] = useState('Dynamics365');
  const [outboundAdapter, setOutboundAdapter] = useState('Avalara');
  const [inboundValues, setInboundValues] = useState<Json>({});
  const [outboundRest, setOutboundRest] = useState<Json>({});
  const [sandboxValues, setSandboxValues] = useState<Json>({});
  const [productionValues, setProductionValues] = useState<Json>({});
  const [typed, setTyped] = useState<Typed>({});
  const [modules, setModules] = useState<ModuleName[]>(DEFAULT_MODULES);

  const loadOutbound = (json: string) => {
    const { outboundRest: rest, sandboxValues: sandbox, productionValues: production } = splitOutbound(json);
    setOutboundRest(rest);
    setSandboxValues(sandbox);
    setProductionValues(production);
  };

  useEffect(() => {
    if (!data) {
      return;
    }
    const loaded = formFromProfile(data);
    setEnvironment(loaded.environment);
    setInboundAdapter(loaded.inboundAdapter);
    setOutboundAdapter(loaded.outboundAdapter);
    setInboundValues(loaded.inboundValues);
    setOutboundRest(loaded.outboundRest);
    setSandboxValues(loaded.sandboxValues);
    setProductionValues(loaded.productionValues);
    setTyped({});
    setModules(loaded.modules);
  }, [data]);

  // Marcar e desmarcar mantém a ordem da barra lateral. O último marcado não se desmarca: o servidor recusa a lista
  // vazia, porque a ausência já quer dizer "só o Fiscal" (D2).
  const toggleModule = (module: ModuleName, on: boolean) =>
    setModules((current) => MODULE_CHOICES.map((c) => c.module).filter((m) => (m === module ? on : current.includes(m))));

  // Trocar de adapter começa de settings vazias: as do adapter anterior são de outro schema. Voltar ao
  // adapter gravado recupera as settings gravadas.
  const dropTyped = (kind: string) =>
    setTyped((t) => Object.fromEntries(Object.entries(t).filter(([k]) => !k.startsWith(kind))));
  const changeInbound = (name: string) => {
    setInboundAdapter(name);
    setInboundValues(name === data?.inboundAdapter ? parseObj(data.inboundSettings) : {});
    dropTyped('inbound.');
  };
  // Integração automática = poll.enabled das settings de entrada, e em nenhum outro lugar (ADR-0029). Grava um booleano
  // JSON (e não texto, como o setPath) e preserva o resto da seção: intervalo, sobreposição e startFrom.
  const scans = SCANNING_INBOUND_ADAPTERS.has(inboundAdapter);
  const automatic = asObj(inboundValues.poll).enabled === true;
  const setAutomatic = (on: boolean) =>
    setInboundValues((v) => ({ ...v, poll: { ...asObj(v.poll), enabled: on } }));

  const changeOutbound = (name: string) => {
    setOutboundAdapter(name);
    loadOutbound(name === data?.outboundAdapter ? data.outboundSettings : '{}');
    dropTyped('outbound.');
  };

  const form: ConnectorForm = {
    environment,
    inboundAdapter,
    inboundValues,
    outboundAdapter,
    outboundRest,
    sandboxValues,
    productionValues,
    modules,
  };

  const save = useMutation({
    mutationFn: () => api.saveConnector(buildConnectorPayload(form, typed)),
    onSuccess: () => {
      setTyped({}); // o valor digitado não fica na tela depois de gravado
      qc.invalidateQueries({ queryKey: ['connector'] });
      qc.invalidateQueries({ queryKey: ['info'] });
      qc.invalidateQueries({ queryKey: ['automatic-panel'] });
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
  // As opções da integração automática aparecem assim que o interruptor é ligado na tela, sem esperar o salvar: ligar,
  // salvar e só então ver o que ajustar seria contraintuitivo (revisão de 2026-10-01, D6).
  const showPanel = scans && automatic;
  // O teste usa a credencial gravada: com o formulário diferente do perfil carregado, o botão pede para salvar antes (D9).
  const pending = hasPendingEdit(form, typed, data);

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
          <Tab label="Módulos" />
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
              {scans && (
                <FormControlLabel
                  control={<Switch checked={automatic} onChange={(e) => setAutomatic(e.target.checked)} />}
                  label="Integração automática"
                />
              )}
            </Box>
            {showPanel && <AutomaticIntegrationPanel />}
            <Fields
              schema={INBOUND_ADAPTERS[inboundAdapter] ?? []}
              values={inboundValues}
              onChange={setInboundValues}
              prefix="inbound."
              secrets={data.secrets ?? {}}
              typed={typed}
              onType={setTyped}
            />
            {CREDENTIAL_TEST_ADAPTERS.inbound.has(inboundAdapter) && <CredentialTestButton side="inbound" pending={pending} />}
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
            {CREDENTIAL_TEST_ADAPTERS.outbound.has(outboundAdapter) && (
              <CredentialTestButton side="outbound" environment="Sandbox" pending={pending} />
            )}

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
            {CREDENTIAL_TEST_ADAPTERS.outbound.has(outboundAdapter) && (
              <CredentialTestButton side="outbound" environment="Production" pending={pending} />
            )}
          </Box>
        )}

        {tab === 2 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Typography variant="body2" color="text.secondary">
              Escolha os módulos que aparecem no menu Integrações.
            </Typography>
            <FormGroup>
              {MODULE_CHOICES.map((c) => {
                const checked = modules.includes(c.module);
                return (
                  <FormControlLabel
                    key={c.module}
                    control={
                      <Checkbox
                        checked={checked}
                        disabled={checked && modules.length === 1}
                        onChange={(e) => toggleModule(c.module, e.target.checked)}
                      />
                    }
                    label={c.label}
                  />
                );
              })}
            </FormGroup>
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
            Configurações salvas com sucesso
          </Alert>
        )}
      </Paper>
    </Box>
  );
}
