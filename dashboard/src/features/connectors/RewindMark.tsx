import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Alert from '@mui/material/Alert';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { Modal } from '../../components/Modal';

// Buscar de novo desde uma data (design D7 e D8): no código, é voltar a marca d'água. Só para trás, e só depois da
// primeira busca. A tela escolhe data e hora no fuso do navegador e manda o instante em ISO; o servidor grava em UTC,
// sob o lease do coletor.

// O valor de um <input type="datetime-local"> no fuso do navegador: AAAA-MM-DDTHH:mm.
function toLocalInput(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function formatChoice(local: string): string {
  return new Date(local).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

// O texto da confirmação mora aqui, e só aqui (D8). Curto, mas cada frase tem de continuar verdadeira:
// - "alteradas": a busca filtra pelo SysModifiedDateTime, e não pela data de emissão;
// - "já enviadas e sem alteração": o AlreadyProcessedAsync barra só Submitted e Confirmed com o mesmo hash;
// - "processadas de novo": recusadas, com erro e ignoradas não são barradas (a NFS-e ignorada volta a ser ignorada);
// - "pelo menos 4 consultas": o FetchAsync do D365 faz 4 GETs fixos (mais o do voucher, às vezes), e a busca vem antes da
//   idempotência. A NFS-e e o CT-e são ignorados antes da busca.
// Quem mudar a idempotência ou a busca revê estas frases.
function RewindConfirmation() {
  return (
    <ul style={{ margin: 0, paddingLeft: 20, display: 'flex', flexDirection: 'column', gap: 6, fontSize: 14, lineHeight: 1.5 }}>
      <li>As notas alteradas no ERP desde essa data serão lidas de novo.</li>
      <li>Notas já enviadas e sem alteração não serão reenviadas.</li>
      <li>Notas recusadas, com erro ou ignoradas serão processadas de novo.</li>
      <li>Cada NF-e lida gera pelo menos 4 consultas ao ERP.</li>
    </ul>
  );
}

export function RewindMark({ watermark }: { watermark: string }) {
  const qc = useQueryClient();
  const [choice, setChoice] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [done, setDone] = useState<string | null>(null);

  const rewind = useMutation({
    mutationFn: () => api.rewindAutomatic(new Date(choice).toISOString()),
    onSuccess: () => {
      setConfirming(false);
      setDone(formatChoice(choice));
      qc.invalidateQueries({ queryKey: ['automatic-panel'] });
    },
    onError: () => setConfirming(false),
  });

  const max = toLocalInput(watermark);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, mt: 1 }}>
      <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <TextField
          type="datetime-local"
          size="small"
          label="Buscar novamente desde"
          value={choice}
          onChange={(e) => setChoice(e.target.value)}
          InputLabelProps={{ shrink: true }}
          inputProps={{ max }}
        />
        <Button
          variant="outlined"
          disabled={!choice || choice >= max || rewind.isPending}
          onClick={() => {
            rewind.reset();
            setDone(null);
            setConfirming(true);
          }}
        >
          Buscar novamente
        </Button>
      </Box>

      {done && <Alert severity="success">A próxima busca começa em {done}.</Alert>}
      {rewind.isError && <Alert severity="error">{(rewind.error as Error).message}</Alert>}

      {confirming && (
        <Modal
          title={`Buscar novamente desde ${formatChoice(choice)}?`}
          onClose={() => setConfirming(false)}
          maxWidth={520}
          footer={
            <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end' }}>
              <Button onClick={() => setConfirming(false)}>Cancelar</Button>
              <Button variant="contained" disableElevation disabled={rewind.isPending} onClick={() => rewind.mutate()}>
                Buscar novamente
              </Button>
            </Box>
          }
        >
          <RewindConfirmation />
        </Modal>
      )}
    </Box>
  );
}
