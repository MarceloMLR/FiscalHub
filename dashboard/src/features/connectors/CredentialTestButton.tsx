import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import { useMutation } from '@tanstack/react-query';
import { api } from '../../api/client';

// O botão "Testar credencial" (design D9): testa a credencial GRAVADA, lida do cofre no servidor, no ambiente da instância
// — que não é escolha do botão (change platform-account-and-connectors, D6). Com uma edição pendente
// na tela, ele não chama o teste e pede para salvar antes, porque a resposta seria sobre outra credencial. A mensagem é a
// curta que o servidor manda; o detalhe do motivo fica no log do host (revisão de 2026-10-01).

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

// `blocked`, quando vem, diz o que falta na credencial gravada: o botão fica desabilitado, e a tela diz o porquê.
export function CredentialTestButton({
  side,
  pending,
  blocked,
}: {
  side: 'inbound' | 'platform';
  pending: boolean;
  blocked?: string | null;
}) {
  const [askedToSave, setAskedToSave] = useState(false);
  const test = useMutation({ mutationFn: () => api.testConnector(side) });

  const onClick = () => {
    if (pending) {
      test.reset();
      setAskedToSave(true);
      return;
    }
    setAskedToSave(false);
    test.mutate();
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, alignItems: 'flex-start' }}>
      <Button
        variant="outlined"
        size="small"
        onClick={onClick}
        disabled={test.isPending || !!blocked}
        startIcon={test.isPending ? <CircularProgress size={14} color="inherit" /> : undefined}
      >
        Testar credencial
      </Button>

      {blocked && <Alert severity="warning">{blocked}</Alert>}
      {askedToSave && pending && (
        <Alert severity="warning">Salve as alterações antes de testar.</Alert>
      )}
      {test.isError && <Alert severity="error">{(test.error as Error).message}</Alert>}
      {test.isSuccess && (
        <Alert severity={test.data.worked ? 'success' : 'error'}>
          {test.data.reason}
          {/* Com o freio (D12), a tela diz como sair dele: salvar desfaz o bloqueio na hora. */}
          {test.data.retryAt && <>. Corrija e salve para testar de novo, ou aguarde até {formatTime(test.data.retryAt)}.</>}
        </Alert>
      )}
    </Box>
  );
}
