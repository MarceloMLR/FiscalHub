import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import { useMutation } from '@tanstack/react-query';
import { api } from '../../api/client';

// O botão "Testar credencial" (design D9): testa a credencial GRAVADA, lida do cofre no servidor. Com uma edição pendente
// na tela, ele não chama o teste e pede para salvar antes, porque a resposta seria sobre outra credencial. A mensagem é a
// curta que o servidor manda; o detalhe do motivo fica no log do host (revisão de 2026-10-01).

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

export function CredentialTestButton({
  side,
  environment,
  pending,
}: {
  side: 'inbound' | 'outbound';
  environment?: 'Sandbox' | 'Production';
  pending: boolean;
}) {
  const [askedToSave, setAskedToSave] = useState(false);
  const test = useMutation({ mutationFn: () => api.testConnector(side, environment) });

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
        disabled={test.isPending}
        startIcon={test.isPending ? <CircularProgress size={14} color="inherit" /> : undefined}
      >
        Testar credencial
      </Button>

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
