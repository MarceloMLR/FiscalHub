import CloudOffOutlinedIcon from '@mui/icons-material/CloudOffOutlined';
import RefreshOutlinedIcon from '@mui/icons-material/RefreshOutlined';
import LogoutOutlinedIcon from '@mui/icons-material/LogoutOutlined';

// A tela de quando a casca não teve resposta (spec module-navigation, "Sem saber quem é o usuário e o que o tenant
// tem, o dashboard não desenha o painel"). Ela ocupa a área inteira, como o "Carregando…", e **não** é meia casca:
// sem barra lateral, sem topbar e sem menu do usuário (design D11).
//
// **É uma tela só, para as duas portas do portão** (D16): a checagem de sessão que não respondeu e o `/info` que não
// respondeu caem aqui, com a mesma mensagem e os mesmos dois botões. O usuário não precisa saber qual chamada falhou
// — para ele, o que há é um painel que não carregou. O que muda entre as duas é só o que o `onRetry` refaz.
//
// **O que a mensagem não pode dizer** (D7): nada sobre a configuração do cliente — nem conector, nem módulo, nem
// agendamento, nem ambiente, nem permissão, e nem para negar. Ela fala da falha de **carregar o painel**. Afirmar
// ausência a partir de uma falha é o defeito que esta fatia veio apagar, e trocar "Sem conector configurado" por "não
// encontramos seus conectores" seria o mesmo defeito com outra fonte. A própria palavra "configuração" ficou fora da
// frase: ela é verdadeira nas duas portas — quando a checagem de sessão não responde, configuração nenhuma chegou a
// ser pedida — e não manda o usuário conferir as próprias configurações, que é o lugar errado.
//
// **A saída é um botão próprio** (D11): ficar preso numa falha persistente sem poder trocar de conta é armadilha, e
// sair é local — apaga o token e o estado de sessão —, então funciona justamente com o servidor fora, que é a situação
// desta tela. O que não há aqui é "entrar de novo" como conserto: sessão expirada é 401, e o 401 já volta ao login
// sozinho (D8).
export function ShellFailure({ onRetry, onLogout }: { onRetry: () => void; onLogout: () => void }) {
  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--page)',
        display: 'grid',
        placeItems: 'center',
        padding: 24,
        boxSizing: 'border-box',
      }}
    >
      <div
        style={{
          width: '100%',
          maxWidth: 420,
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 'var(--r-md)',
          boxShadow: 'var(--shadow-card)',
          padding: '28px 26px',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 16,
          textAlign: 'center',
        }}
      >
        <span
          style={{
            width: 44,
            height: 44,
            borderRadius: 'var(--r-full)',
            background: 'var(--error-bg)',
            border: '1px solid var(--error-border)',
            color: 'var(--error-text)',
            display: 'grid',
            placeItems: 'center',
          }}
        >
          <CloudOffOutlinedIcon sx={{ fontSize: 21 }} />
        </span>

        <div
          role="alert"
          style={{
            fontFamily: 'var(--font-display)',
            fontSize: 16.5,
            fontWeight: 600,
            lineHeight: 1.4,
            letterSpacing: '-0.01em',
            color: 'var(--ink)',
          }}
        >
          Não foi possível carregar o painel.
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 2 }}>
          <button
            type="button"
            onClick={onRetry}
            style={{
              height: 38,
              padding: '0 16px',
              display: 'inline-flex',
              alignItems: 'center',
              gap: 7,
              fontSize: 13.5,
              fontWeight: 600,
              borderRadius: 'var(--r)',
              background: 'var(--accent)',
              color: 'var(--accent-ink)',
              border: 'none',
              cursor: 'pointer',
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--accent-hover)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'var(--accent)')}
          >
            <RefreshOutlinedIcon sx={{ fontSize: 17 }} />
            Tentar de novo
          </button>

          <button
            type="button"
            onClick={onLogout}
            style={{
              height: 38,
              padding: '0 14px',
              display: 'inline-flex',
              alignItems: 'center',
              gap: 7,
              fontSize: 13.5,
              fontWeight: 500,
              borderRadius: 'var(--r)',
              background: 'var(--surface)',
              color: 'var(--text)',
              border: '1px solid var(--border)',
              cursor: 'pointer',
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--surface-2)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'var(--surface)')}
          >
            <LogoutOutlinedIcon sx={{ fontSize: 16, color: 'var(--muted)' }} />
            Sair
          </button>
        </div>
      </div>
    </div>
  );
}
