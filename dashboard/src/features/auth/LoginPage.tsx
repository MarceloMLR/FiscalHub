import { useState, type FormEvent } from 'react';
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
        padding: 16,
      }}
    >
      <form
        onSubmit={submit}
        style={{
          width: '100%',
          maxWidth: 384,
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 12,
          boxShadow: 'var(--shadow-card)',
          padding: 28,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 6 }}>
          <div
            style={{
              width: 30,
              height: 30,
              borderRadius: 8,
              background: 'var(--accent)',
              color: '#fff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontWeight: 700,
              fontSize: 15,
            }}
          >
            F
          </div>
          <span style={{ fontSize: 20, fontWeight: 700, color: 'var(--ink)' }}>FiscalHub</span>
        </div>
        <p style={{ fontSize: 13.5, color: 'var(--muted)', margin: '0 0 22px' }}>
          Entre com sua conta para acessar o painel.
        </p>

        <label className="fh-label" style={{ display: 'block', marginBottom: 6 }}>
          E-mail
        </label>
        <input
          className="fh-input"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoFocus
          placeholder="voce@empresa.com"
          style={{ marginBottom: 16 }}
        />

        <label className="fh-label" style={{ display: 'block', marginBottom: 6 }}>
          Senha
        </label>
        <input
          className="fh-input"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="••••••••"
        />

        {error && (
          <div
            style={{
              marginTop: 16,
              fontSize: 13,
              color: 'var(--error-text)',
              background: 'var(--error-bg)',
              border: '1px solid var(--error-border)',
              borderRadius: 8,
              padding: '8px 10px',
            }}
          >
            {error}
          </div>
        )}

        <button
          className="fh-btn"
          type="submit"
          disabled={busy || email === '' || password === ''}
          style={{ width: '100%', marginTop: 22 }}
        >
          {busy ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </div>
  );
}
