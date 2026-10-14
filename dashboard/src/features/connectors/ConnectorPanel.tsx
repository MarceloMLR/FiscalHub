import { useState, type ReactNode } from 'react';
import Box from '@mui/material/Box';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { setIntegration, toggleIntegration, toggleModule, type ConnectorEntry } from './connectorPayload';
import { connectorSummary, endpointRows, moduleRows } from './connectorPanelRows';

// A tela do canvas (change platform-account-and-connectors, grupo 7; ADR-0039): a lista de conectores da conta e o painel
// do conector escolhido, com três abas — Conexão (as URLs base por versão), Módulos e integrações, e Endpoints (o caminho
// e, onde a API aceita, o lote). O dado é o mesmo da tela do grupo 6: o payload sai do mesmo montador.
export function ConnectorPanel({
  connectors,
  selected,
  onSelect,
  onChange,
  connection,
}: {
  connectors: ConnectorEntry[];
  selected: ConnectorEntry;
  onSelect: (adapter: string) => void;
  onChange: (entry: ConnectorEntry) => void;
  // Os campos da aba Conexão, que a página monta: são os mesmos campos de settings, com a regra dos segredos.
  connection: ReactNode;
}) {
  const [tab, setTab] = useState(0);
  const modules = moduleRows(selected);
  const endpoints = endpointRows(selected);

  return (
    // Lado a lado só a partir de 1200px, quando a página já está na largura máxima. Abaixo disso, com a barra lateral de
    // 232px, as abas e a URL base não cabem ao lado da lista: entre 900 e 1000px, "Endpoints" sumia atrás da seta, e a URL
    // aparecia cortada (conferido pelo Marcelo na tela).
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '240px 1fr' }, gap: 2, alignItems: 'start' }}>
      <List dense disablePadding sx={{ border: 1, borderColor: 'divider', borderRadius: 2 }}>
        {connectors.map((c) => (
          <ListItemButton key={c.adapter} selected={c.adapter === selected.adapter} onClick={() => onSelect(c.adapter)}>
            <ListItemText primary={c.adapter} secondary={connectorSummary(c)} />
          </ListItemButton>
        ))}
      </List>

      <Box sx={{ minWidth: 0 }}>
        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: 1, borderColor: 'divider', mb: 2 }} variant="scrollable">
          <Tab label="Conexão" />
          <Tab label="Módulos e integrações" />
          <Tab label="Endpoints" />
        </Tabs>

        {tab === 0 && connection}

        {tab === 1 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            {modules.map((m) => (
              <Box key={m.module}>
                <FormControlLabel
                  control={
                    <Checkbox
                      checked={m.on}
                      disabled={!m.declared}
                      onChange={(e) => onChange(toggleModule(selected, m.module, e.target.checked))}
                    />
                  }
                  label={m.label}
                />
                {m.reason && (
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block', pl: 4, mt: -0.5 }}>
                    {m.reason}
                  </Typography>
                )}
                {m.on && (
                  <Box sx={{ pl: 4, display: 'flex', flexDirection: 'column' }}>
                    {m.integrations.map((i) => (
                      <FormControlLabel
                        key={i.key}
                        control={
                          <Checkbox size="small" checked={i.on} onChange={(e) => onChange(toggleIntegration(selected, i.key, e.target.checked))} />
                        }
                        label={i.label}
                      />
                    ))}
                  </Box>
                )}
              </Box>
            ))}
          </Box>
        )}

        {tab === 2 &&
          (endpoints.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              Nenhuma integração ligada. Ligue-as na aba Módulos e integrações.
            </Typography>
          ) : (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
              {endpoints.map((r) => (
                // Três colunas a partir de 900px; abaixo, empilha: o rótulo em cima, o caminho e o lote embaixo, na largura
                // inteira.
                <Box
                  key={r.key}
                  sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '180px 1fr 120px' }, gap: 1, alignItems: 'start' }}
                >
                  <Typography variant="body2" sx={{ pt: 1 }}>
                    {r.label}
                  </Typography>
                  <TextField
                    size="small"
                    label="Caminho"
                    value={r.endpoint ?? ''}
                    placeholder="ex.: taxcompliance/v2/fiscal/dfe"
                    onChange={(e) => onChange(setIntegration(selected, r.key, { endpoint: e.target.value }))}
                    helperText={r.pending ?? ' '}
                    FormHelperTextProps={{ sx: { color: r.pending ? 'warning.main' : undefined } }}
                  />
                  {r.acceptsBatch ? (
                    <TextField
                      size="small"
                      label="Lote"
                      type="number"
                      value={r.batchSize}
                      onChange={(e) => onChange(setIntegration(selected, r.key, { batchSize: e.target.value }))}
                    />
                  ) : (
                    <Box sx={{ display: { xs: 'none', md: 'block' } }} />
                  )}
                </Box>
              ))}
            </Box>
          ))}
      </Box>
    </Box>
  );
}
