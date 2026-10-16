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
import { AutomaticIntegrationPanel } from './AutomaticIntegrationPanel';
import { CredentialTestButton } from './CredentialTestButton';
import { ConnectorPanel } from './ConnectorPanel';
import { connectorList, selectedConnector } from './connectorPanelRows';
import {
  ACCOUNT_FIELDS,
  CONNECTOR_ADAPTERS,
  INBOUND_ADAPTERS,
  CREDENTIAL_TEST_ADAPTERS,
  SCANNING_INBOUND_ADAPTERS,
  fieldPlaceholder,
  type AdapterField,
} from './adapterSchemas';
import { useInfo } from '../useInfo';
import type { SecretStatus } from '../../types';
import {
  SECRET_MASK,
  accountSecretPrefix,
  asObj,
  buildConnectorPayload,
  connectorSecretPrefix,
  formFromProfile,
  hasPendingEdit,
  fieldPending,
  getPath,
  parseObj,
  platformTestBlocker,
  setPath,
  shownAccount,
  withAccount,
  withConnector,
  type AccountForm,
  type ConnectorEntry,
  type ConnectorForm,
  type Json,
  type Typed,
} from './connectorPayload';

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
  prefix: string; // caminho do segredo no mapa `secrets` (ex.: 'platform.Avalara.')
  secrets: Record<string, SecretStatus>;
  typed: Typed;
  onType: (t: Typed) => void;
  environment: string | undefined; // o ambiente da instância (/info), para os exemplos que dependem dele
}

// Renderiza os campos de um adapter num grid. Segredo é campo de senha, sem preenchimento: mostra se
// está configurado e quando, e só manda um valor novo quando digitado. A máscara do segredo configurado é PLACEHOLDER, e
// nunca valor: o valor é só o digitado, e o payload só o leva quando não é vazio (connectorPayload, D3).
function Fields({ schema, values, onChange, prefix, secrets, typed, onType, environment }: FieldsProps) {
  return (
    // Duas colunas a partir de 900px; abaixo, uma: um GUID ou uma URL cortados no meio não se leem.
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
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
            placeholder={fieldPlaceholder(f, environment)}
            // O obrigatório vazio tem a marca de pendência, como o segredo não configurado e a integração sem caminho.
            helperText={fieldPending(f, values) ?? undefined}
            FormHelperTextProps={{ sx: { color: 'warning.main' } }}
            sx={{ gridColumn: f.wide ? '1 / -1' : undefined }}
          />
        ),
      )}
    </Box>
  );
}

export function ConnectorsPage() {
  const qc = useQueryClient();
  const { data, isLoading, isError } = useConnector();
  const { data: info } = useInfo();

  const [tab, setTab] = useState(0);
  const [inboundAdapter, setInboundAdapter] = useState('Dynamics365');
  const [inboundValues, setInboundValues] = useState<Json>({});
  // A saída é a conta de plataforma e os conectores sob ela (change platform-account-and-connectors). A tela mostra a conta
  // da plataforma escolhida e o conector do adapter de mesmo nome; as outras contas e os outros conectores, e os campos
  // que a tela não mostra (establishments…), ficam aqui e voltam intactos. O ambiente é da instância (D6).
  const [platform, setPlatform] = useState('Avalara');
  const [accounts, setAccounts] = useState<AccountForm[]>([]);
  // A chave de envio ao conector de saída (ADR-0041). É do tenant e vale para toda a saída, e não de um conector: por
  // isso fica fora de `accounts`, e muda com o formulário — só o salvar a grava.
  const [outboundDispatch, setOutboundDispatch] = useState(true);
  // O conector escolhido na lista do painel (grupo 7). Nulo: o do adapter de mesmo nome da plataforma.
  const [selectedAdapter, setSelectedAdapter] = useState<string | null>(null);
  const [typed, setTyped] = useState<Typed>({});

  useEffect(() => {
    if (!data) {
      return;
    }
    const loaded = formFromProfile(data);
    setInboundAdapter(loaded.inboundAdapter);
    setInboundValues(loaded.inboundValues);
    setPlatform(loaded.platform);
    setAccounts(loaded.accounts);
    setOutboundDispatch(loaded.outboundDispatch);
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
  // Integração automática = poll.enabled das settings de entrada, e em nenhum outro lugar (ADR-0029). Grava um booleano
  // JSON (e não texto, como o setPath) e preserva o resto da seção: intervalo, sobreposição e startFrom.
  const scans = SCANNING_INBOUND_ADAPTERS.has(inboundAdapter);
  const automatic = asObj(inboundValues.poll).enabled === true;
  const setAutomatic = (on: boolean) =>
    setInboundValues((v) => ({ ...v, poll: { ...asObj(v.poll), enabled: on } }));

  const form: ConnectorForm = {
    inboundAdapter,
    inboundValues,
    platform,
    accounts,
    outboundDispatch,
  };

  // Trocar de plataforma mostra a conta dela, e não apaga a da anterior: um tenant tem uma conta por plataforma. A conta
  // nova só entra no payload na primeira edição, e o segredo digitado fica no caminho da própria plataforma.
  const account = shownAccount(form);
  const connectors = connectorList(account);
  const connector = selectedConnector(account, selectedAdapter);
  const setAccountValues = (values: Json) => setAccounts(withAccount(form, { ...account, values }).accounts);
  const setConnectorValues = (values: Json) =>
    setAccounts(withAccount(form, withConnector(account, { ...connector, values })).accounts);
  const setConnector = (entry: ConnectorEntry) => setAccounts(withAccount(form, withConnector(account, entry)).accounts);

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

  // As opções da integração automática aparecem assim que o interruptor é ligado na tela, sem esperar o salvar: ligar,
  // salvar e só então ver o que ajustar seria contraintuitivo (revisão de 2026-10-01, D6).
  const showPanel = scans && automatic;
  // O teste usa a credencial gravada: com o formulário diferente do perfil carregado, o botão pede para salvar antes (D9).
  const pending = hasPendingEdit(form, typed, data);

  return (
    <Box sx={{ p: 3, maxWidth: 900, mx: 'auto' }}>
      <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: 1, borderColor: 'divider' }}>
        <Typography variant="h6" sx={{ mb: 0.5, fontFamily: 'var(--font-display)' }}>
          Perfil de conector
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          Como este tenant integra. Escolha os adapters e preencha os campos de cada um. Segredos são
          digitados aqui e vão direto para o <strong>cofre</strong>: a tela nunca os mostra de volta, só se
          estão configurados e quando.
        </Typography>

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
              environment={info?.environment}
            />
            {CREDENTIAL_TEST_ADAPTERS.inbound.has(inboundAdapter) && <CredentialTestButton side="inbound" pending={pending} />}
          </Box>
        )}

        {tab === 1 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {/* A chave é do tenant e vale para toda a saída, e por isso vem antes da plataforma: ela não é de um
                conector. O rótulo diz o eixo ("ao conector de saída") para não se confundir com "Integração
                automática", que é da aba de entrada. */}
            <Box>
              <FormControlLabel
                control={<Switch checked={outboundDispatch} onChange={(e) => setOutboundDispatch(e.target.checked)} />}
                label="Enviar ao conector de saída"
              />
              <Typography variant="body2" color="text.secondary">
                Desligado, o hub continua capturando e gravando o JSON que iria à plataforma, e não envia: a nota fica
                em "Salva", e o reprocesso fica indisponível. É do tenant, e vale para todos os conectores de saída.
              </Typography>
            </Box>

            <TextField
              select
              label="Plataforma"
              size="small"
              value={platform}
              onChange={(e) => setPlatform(e.target.value)}
              sx={{ minWidth: 220 }}
            >
              {Object.keys(CONNECTOR_ADAPTERS).map((name) => (
                <MenuItem key={name} value={name}>
                  {name}
                </MenuItem>
              ))}
            </TextField>

            <Typography variant="subtitle2">Conta de plataforma</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: -1.5 }}>
              A credencial é da conta, e vale para todos os conectores dela. O endpoint de token é obrigatório.
            </Typography>
            <Fields
              schema={ACCOUNT_FIELDS}
              values={account.values}
              onChange={setAccountValues}
              prefix={accountSecretPrefix(platform)}
              secrets={data.secrets ?? {}}
              typed={typed}
              onType={setTyped}
              environment={info?.environment}
            />
            {CREDENTIAL_TEST_ADAPTERS.platform.has(platform) && (
              <CredentialTestButton side="platform" pending={pending} blocked={platformTestBlocker(data, platform)} />
            )}

            <Typography variant="subtitle2" sx={{ mt: 1 }}>
              Conectores
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: -1.5 }}>
              Os conectores da conta. Os módulos ligados aparecem no menu Integrações. Cada integração tem o caminho do
              endpoint, relativo à URL base da versão que ela usa; sem caminho, ela fica inativa.
            </Typography>
            <ConnectorPanel
              connectors={connectors}
              selected={connector}
              onSelect={setSelectedAdapter}
              onChange={setConnector}
              connection={
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                  <Typography variant="body2" color="text.secondary">
                    As URLs base, por versão da API. A versão escolhe a base, e não entra na URL.
                  </Typography>
                  <Fields
                    schema={CONNECTOR_ADAPTERS[connector.adapter] ?? []}
                    values={connector.values}
                    onChange={setConnectorValues}
                    prefix={connectorSecretPrefix(connector.adapter)}
                    secrets={data.secrets ?? {}}
                    typed={typed}
                    onType={setTyped}
                    environment={info?.environment}
                  />
                </Box>
              }
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
            Configurações salvas com sucesso
          </Alert>
        )}
      </Paper>
    </Box>
  );
}
