import { useState, type CSSProperties, type FormEvent, type InputHTMLAttributes } from 'react';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import { useAuth } from './AuthContext';

export function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(email, password);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--page)',
        padding: 32,
        boxSizing: 'border-box',
      }}
    >
      <div style={{ width: '100%', maxWidth: 392, display: 'flex', flexDirection: 'column', gap: 20 }}>
        {/* Marca — acima do cartão */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: 9,
              background: 'var(--accent)',
              color: '#fff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 15,
              fontWeight: 700,
            }}
          >
            F
          </div>
          <div style={{ fontSize: 17, fontWeight: 700, letterSpacing: '-0.018em', color: 'var(--ink)' }}>FiscalHub</div>
        </div>

        {/* Cartão */}
        <form
          onSubmit={submit}
          style={{
            background: 'var(--surface)',
            border: '1px solid var(--border)',
            borderRadius: 12,
            boxShadow: 'var(--shadow-card)',
            padding: 28,
            display: 'flex',
            flexDirection: 'column',
            gap: 20,
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
            <div style={{ fontSize: 19, fontWeight: 700, letterSpacing: '-0.02em', color: 'var(--ink)' }}>Entrar no painel</div>
            <div style={{ fontSize: 13, lineHeight: 1.55, color: 'var(--text-secondary)' }}>
              Use a conta do seu tenant para acompanhar as integrações.
            </div>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label htmlFor="fh-email" style={labelStyle}>E-mail</label>
              <TextInput
                id="fh-email"
                type="email"
                autoFocus
                placeholder="voce@empresa.com.br"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
                <label htmlFor="fh-pass" style={labelStyle}>Senha</label>
                <a
                  href="#"
                  onClick={(e) => e.preventDefault()}
                  style={{ fontSize: 12, fontWeight: 500, color: 'var(--accent)', textDecoration: 'none' }}
                >
                  Esqueci a senha
                </a>
              </div>
              <TextInput
                id="fh-pass"
                type="password"
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
          </div>

          {error && (
            <div
              style={{
                border: '1px solid var(--error-border)',
                background: 'var(--error-bg)',
                borderRadius: 8,
                padding: '10px 12px',
                display: 'flex',
                gap: 9,
                alignItems: 'flex-start',
              }}
            >
              <ErrorOutlineIcon sx={{ fontSize: 16, color: 'var(--error-text)', flexShrink: 0, mt: '1px' }} />
              <div style={{ fontSize: 12.5, lineHeight: 1.45, color: 'var(--error-text)' }}>{error}</div>
            </div>
          )}

          <button
            type="submit"
            disabled={busy || email === '' || password === ''}
            className="fh-btn"
            style={{ width: '100%', height: 38 }}
          >
            {busy ? 'Entrando…' : 'Entrar'}
          </button>
        </form>

        <div style={{ fontSize: 12, color: 'var(--muted)', textAlign: 'center', lineHeight: 1.5 }}>
          Acesso restrito à conta do seu tenant.
        </div>
      </div>
    </div>
  );
}

const labelStyle: CSSProperties = { fontSize: 12.5, fontWeight: 600, color: 'var(--text)' };

// Campo com anel de foco (o design usa 36px, raio 7, borda forte, foco no acento).
function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  const [focused, setFocused] = useState(false);
  return (
    <input
      {...props}
      onFocus={(e) => {
        setFocused(true);
        props.onFocus?.(e);
      }}
      onBlur={(e) => {
        setFocused(false);
        props.onBlur?.(e);
      }}
      style={{
        height: 36,
        padding: '0 11px',
        fontSize: 13.5,
        color: 'var(--ink)',
        background: 'var(--surface)',
        border: `1px solid ${focused ? 'var(--accent)' : 'var(--border-strong)'}`,
        borderRadius: 7,
        outline: 'none',
        width: '100%',
        boxSizing: 'border-box',
        boxShadow: focused ? '0 0 0 3px var(--accent-ring)' : 'none',
      }}
    />
  );
}
