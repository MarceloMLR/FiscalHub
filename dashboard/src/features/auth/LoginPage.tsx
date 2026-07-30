import { useState, type FormEvent, type InputHTMLAttributes, type ReactNode } from 'react';
import MailOutlineIcon from '@mui/icons-material/MailOutline';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import VisibilityOutlinedIcon from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffOutlinedIcon from '@mui/icons-material/VisibilityOffOutlined';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import { useAuth } from './AuthContext';

// Cores fixas do login (composição de marca, independente do tema claro/escuro do app).
const C = {
  brandBg: '#0e2a35',
  brandText: '#e8eef1',
  brandMuted: '#93a8b2',
  brandFaint: '#6f8892',
  cardBorder: 'rgba(255,255,255,0.12)',
  cardBg: 'rgba(255,255,255,0.035)',
  page: '#f5f7f9',
  ink: '#0b1220',
  text: '#33415c',
  muted: '#6b7788',
  faint: '#9aa5b4',
  border: '#cfd7e0',
  accent: '#0b5c7a',
  accentHover: '#094a63',
  ring: '#e6f1f6',
  errText: '#c0342e',
  errBg: '#fdecea',
  errBorder: '#f5cdc9',
};

export function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [show, setShow] = useState(false);
  const [remember, setRemember] = useState(true);
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
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      {/* ── Painel de marca (esquerda) ── */}
      <div
        className="fh-login-brand"
        style={{
          width: '44%',
          maxWidth: 620,
          background: C.brandBg,
          color: C.brandText,
          display: 'flex',
          flexDirection: 'column',
          padding: '40px 48px',
          boxSizing: 'border-box',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ width: 32, height: 32, borderRadius: 9, background: '#2f7f97', color: '#fff', display: 'grid', placeItems: 'center', fontSize: 15, fontWeight: 700 }}>
            F
          </div>
          <div style={{ fontSize: 17, fontWeight: 700, letterSpacing: '-0.018em' }}>FiscalHub</div>
        </div>

        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center', gap: 22, maxWidth: 460 }}>
          <div style={{ fontSize: 30, fontWeight: 700, lineHeight: 1.2, letterSpacing: '-0.02em' }}>
            Do ERP ao compliance,<br />com rastreabilidade em cada nota.
          </div>
          <div style={{ fontSize: 14, lineHeight: 1.6, color: C.brandMuted, maxWidth: 400 }}>
            O conector recebe as notas do seu ERP, despacha para a plataforma fiscal e devolve o status — sem planilha no
            meio do caminho.
          </div>

          <div style={{ display: 'flex', alignItems: 'stretch', gap: 10, marginTop: 6 }}>
            <StepCard label="Entrada" value="ERP" />
            <Dash />
            <StepCard label="Conector" value="FiscalHub" />
            <Dash />
            <StepCard label="Saída" value="Plataforma Compliance" />
          </div>
        </div>

        <div style={{ fontSize: 12, color: C.brandFaint }}>© 2026 FiscalHub · Middleware de integração fiscal</div>
      </div>

      {/* ── Formulário (direita) ── */}
      <div style={{ flex: 1, background: C.page, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32, boxSizing: 'border-box' }}>
        <form onSubmit={submit} style={{ width: '100%', maxWidth: 372, display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
            <div style={{ fontSize: 23, fontWeight: 700, letterSpacing: '-0.02em', color: C.ink }}>Entrar no painel</div>
            <div style={{ fontSize: 13, color: C.muted }}>Use sua conta para acompanhar as integrações.</div>
          </div>

          <Field label="E-mail" htmlFor="fh-email">
            <IconInput
              id="fh-email"
              type="email"
              autoFocus
              placeholder="voce@empresa.com.br"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              left={<MailOutlineIcon sx={{ fontSize: 18 }} />}
            />
          </Field>

          <Field label="Senha" htmlFor="fh-pass">
            <IconInput
              id="fh-pass"
              type={show ? 'text' : 'password'}
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              left={<LockOutlinedIcon sx={{ fontSize: 18 }} />}
              right={
                <button
                  type="button"
                  onClick={() => setShow((v) => !v)}
                  aria-label={show ? 'Ocultar senha' : 'Mostrar senha'}
                  style={{ display: 'grid', placeItems: 'center', width: 28, height: 28, border: 'none', background: 'transparent', color: C.faint, cursor: 'pointer' }}
                >
                  {show ? <VisibilityOffOutlinedIcon sx={{ fontSize: 18 }} /> : <VisibilityOutlinedIcon sx={{ fontSize: 18 }} />}
                </button>
              }
            />
          </Field>

          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, color: C.text, cursor: 'pointer' }}>
              <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} style={{ width: 16, height: 16, accentColor: C.accent, cursor: 'pointer' }} />
              Manter conectado
            </label>
            <a href="#" onClick={(e) => e.preventDefault()} style={{ fontSize: 12.5, fontWeight: 500, color: C.accent, textDecoration: 'none' }}>
              Esqueci a senha
            </a>
          </div>

          {error && (
            <div style={{ border: `1px solid ${C.errBorder}`, background: C.errBg, borderRadius: 8, padding: '10px 12px', display: 'flex', gap: 9, alignItems: 'flex-start' }}>
              <ErrorOutlineIcon sx={{ fontSize: 16, color: C.errText, flexShrink: 0, mt: '1px' }} />
              <div style={{ fontSize: 12.5, lineHeight: 1.45, color: C.errText }}>{error}</div>
            </div>
          )}

          <button
            type="submit"
            disabled={busy || email === '' || password === ''}
            style={{
              height: 44,
              fontSize: 14,
              fontWeight: 600,
              borderRadius: 8,
              background: C.accent,
              color: '#fff',
              border: 'none',
              cursor: busy ? 'default' : 'pointer',
              opacity: busy || email === '' || password === '' ? 0.55 : 1,
            }}
            onMouseEnter={(e) => { if (!(busy || email === '' || password === '')) e.currentTarget.style.background = C.accentHover; }}
            onMouseLeave={(e) => (e.currentTarget.style.background = C.accent)}
          >
            {busy ? 'Entrando…' : 'Entrar'}
          </button>
        </form>
      </div>
    </div>
  );
}

function StepCard({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ flex: 1, border: `1px solid ${C.cardBorder}`, background: C.cardBg, borderRadius: 10, padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 5, minWidth: 0 }}>
      <div style={{ fontSize: 10, fontWeight: 700, letterSpacing: '0.09em', textTransform: 'uppercase', color: C.brandFaint }}>{label}</div>
      <div style={{ fontSize: 13.5, fontWeight: 600, color: '#fff', lineHeight: 1.25 }}>{value}</div>
    </div>
  );
}

function Dash() {
  return <div style={{ alignSelf: 'center', width: 16, borderTop: `1px dashed ${C.brandFaint}`, flexShrink: 0 }} />;
}

function Field({ label, htmlFor, children }: { label: string; htmlFor: string; children: ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <label htmlFor={htmlFor} style={{ fontSize: 12.5, fontWeight: 600, color: C.text }}>{label}</label>
      {children}
    </div>
  );
}

// Campo com ícone à esquerda (e opcional à direita), com anel de foco no acento.
function IconInput({ left, right, ...props }: InputHTMLAttributes<HTMLInputElement> & { left: ReactNode; right?: ReactNode }) {
  const [focused, setFocused] = useState(false);
  return (
    <div style={{ position: 'relative', display: 'flex', alignItems: 'center' }}>
      <span style={{ position: 'absolute', left: 11, display: 'grid', placeItems: 'center', color: C.faint, pointerEvents: 'none' }}>{left}</span>
      <input
        {...props}
        onFocus={(e) => { setFocused(true); props.onFocus?.(e); }}
        onBlur={(e) => { setFocused(false); props.onBlur?.(e); }}
        style={{
          height: 44,
          width: '100%',
          boxSizing: 'border-box',
          padding: right ? '0 40px 0 38px' : '0 12px 0 38px',
          fontSize: 13.5,
          color: C.ink,
          background: '#fff',
          border: `1px solid ${focused ? C.accent : C.border}`,
          borderRadius: 8,
          outline: 'none',
          boxShadow: focused ? `0 0 0 3px ${C.ring}` : 'none',
        }}
      />
      {right && <span style={{ position: 'absolute', right: 6, display: 'grid', placeItems: 'center' }}>{right}</span>}
    </div>
  );
}
