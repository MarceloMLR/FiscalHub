import { useState, type CSSProperties, type FormEvent, type InputHTMLAttributes, type ReactNode } from 'react';
import MailOutlineIcon from '@mui/icons-material/MailOutline';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import VisibilityOutlinedIcon from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffOutlinedIcon from '@mui/icons-material/VisibilityOffOutlined';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import { api } from '../../api/client';
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
  okText: '#17864a',
  okBg: '#e7f5ec',
  okBorder: '#c3e5d1',
  errText: '#c0342e',
  errBg: '#fdecea',
  errBorder: '#f5cdc9',
};

type Mode = 'login' | 'forgot' | 'reset';

const initialToken = () => new URLSearchParams(window.location.search).get('reset') ?? '';

export function LoginPage() {
  const { login } = useAuth();
  const [mode, setMode] = useState<Mode>(() => (initialToken() ? 'reset' : 'login'));
  const [resetToken, setResetToken] = useState(initialToken);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [show, setShow] = useState(false);
  const [remember, setRemember] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [devToken, setDevToken] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const reset = () => { setError(null); setInfo(null); setDevToken(null); };
  const go = (m: Mode) => { reset(); setMode(m); };

  const run = async (fn: () => Promise<void>) => {
    reset();
    setBusy(true);
    try {
      await fn();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const submitLogin = (e: FormEvent) => { e.preventDefault(); run(() => login(email, password, remember)); };

  const submitForgot = (e: FormEvent) => {
    e.preventDefault();
    run(async () => {
      const r = await api.forgotPassword(email);
      setInfo(r.message);
      setDevToken(r.devToken ?? null);
    });
  };

  const submitReset = (e: FormEvent) => {
    e.preventDefault();
    run(async () => {
      await api.resetPassword(resetToken, newPassword);
      setMode('login');
      setInfo('Senha redefinida. Você já pode entrar.');
    });
  };

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      {/* ── Painel de marca ── */}
      <div
        className="fh-login-brand"
        style={{ width: '44%', maxWidth: 620, background: C.brandBg, color: C.brandText, display: 'flex', flexDirection: 'column', padding: '40px 48px', boxSizing: 'border-box' }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ width: 32, height: 32, borderRadius: 9, background: '#2f7f97', color: '#fff', display: 'grid', placeItems: 'center', fontSize: 15, fontWeight: 700 }}>F</div>
          <div style={{ fontSize: 17, fontWeight: 700, letterSpacing: '-0.018em' }}>FiscalHub</div>
        </div>

        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center', gap: 22, maxWidth: 460 }}>
          <div style={{ fontSize: 30, fontWeight: 700, lineHeight: 1.2, letterSpacing: '-0.02em' }}>
            Do ERP ao compliance,<br />com rastreabilidade em cada nota.
          </div>
          <div style={{ fontSize: 14, lineHeight: 1.6, color: C.brandMuted, maxWidth: 400 }}>
            O conector recebe as notas do seu ERP, despacha para a plataforma fiscal e devolve o status — sem planilha no meio do caminho.
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

      {/* ── Formulário ── */}
      <div style={{ flex: 1, background: C.page, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32, boxSizing: 'border-box' }}>
        <div style={{ width: '100%', maxWidth: 372, display: 'flex', flexDirection: 'column', gap: 18 }}>
          {info && (
            <Banner tone="ok" icon={<CheckCircleOutlineIcon sx={{ fontSize: 16, color: C.okText }} />}>
              {info}
              {devToken && (
                <>
                  {' '}
                  <button
                    type="button"
                    onClick={() => { setResetToken(devToken); go('reset'); }}
                    style={{ ...linkBtn, fontWeight: 600 }}
                  >
                    Redefinir agora
                  </button>{' '}
                  <span style={{ color: C.muted }}>(atalho de dev — em produção iria por e-mail)</span>
                </>
              )}
            </Banner>
          )}

          {mode === 'login' && (
            <form onSubmit={submitLogin} style={formStyle}>
              <Head title="Entrar no painel" subtitle="Use sua conta para acompanhar as integrações." />
              <Field label="E-mail" htmlFor="fh-email">
                <IconInput id="fh-email" type="email" autoFocus placeholder="voce@empresa.com.br" value={email} onChange={(e) => setEmail(e.target.value)} left={<MailOutlineIcon sx={{ fontSize: 18 }} />} />
              </Field>
              <Field label="Senha" htmlFor="fh-pass">
                <IconInput
                  id="fh-pass"
                  type={show ? 'text' : 'password'}
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  left={<LockOutlinedIcon sx={{ fontSize: 18 }} />}
                  right={<EyeToggle show={show} onToggle={() => setShow((v) => !v)} />}
                />
              </Field>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, color: C.text, cursor: 'pointer' }}>
                  <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} style={{ width: 16, height: 16, accentColor: C.accent, cursor: 'pointer' }} />
                  Manter conectado
                </label>
                <button type="button" onClick={() => go('forgot')} style={linkBtn}>Esqueci a senha</button>
              </div>
              {error && <ErrorBanner>{error}</ErrorBanner>}
              <PrimaryButton disabled={busy || email === '' || password === ''}>{busy ? 'Entrando…' : 'Entrar'}</PrimaryButton>
            </form>
          )}

          {mode === 'forgot' && (
            <form onSubmit={submitForgot} style={formStyle}>
              <Head title="Redefinir senha" subtitle="Informe seu e-mail e enviaremos as instruções para redefinir." />
              <Field label="E-mail" htmlFor="fh-fp-email">
                <IconInput id="fh-fp-email" type="email" autoFocus placeholder="voce@empresa.com.br" value={email} onChange={(e) => setEmail(e.target.value)} left={<MailOutlineIcon sx={{ fontSize: 18 }} />} />
              </Field>
              {error && <ErrorBanner>{error}</ErrorBanner>}
              <PrimaryButton disabled={busy || email === ''}>{busy ? 'Enviando…' : 'Enviar instruções'}</PrimaryButton>
              <BackLink onClick={() => go('login')} />
            </form>
          )}

          {mode === 'reset' && (
            <form onSubmit={submitReset} style={formStyle}>
              <Head title="Nova senha" subtitle="Escolha uma nova senha para a sua conta." />
              <Field label="Nova senha" htmlFor="fh-new-pass">
                <IconInput
                  id="fh-new-pass"
                  type={show ? 'text' : 'password'}
                  autoFocus
                  placeholder="••••••••"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  left={<LockOutlinedIcon sx={{ fontSize: 18 }} />}
                  right={<EyeToggle show={show} onToggle={() => setShow((v) => !v)} />}
                />
              </Field>
              {error && <ErrorBanner>{error}</ErrorBanner>}
              <PrimaryButton disabled={busy || newPassword.length < 6}>{busy ? 'Salvando…' : 'Redefinir senha'}</PrimaryButton>
              <BackLink onClick={() => go('login')} />
            </form>
          )}
        </div>
      </div>
    </div>
  );
}

/* ── peças ── */

const formStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 18 };
const linkBtn: CSSProperties = { fontSize: 12.5, fontWeight: 500, color: C.accent, background: 'none', border: 'none', padding: 0, cursor: 'pointer' };

function Head({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
      <div style={{ fontSize: 23, fontWeight: 700, letterSpacing: '-0.02em', color: C.ink }}>{title}</div>
      <div style={{ fontSize: 13, color: C.muted, lineHeight: 1.5 }}>{subtitle}</div>
    </div>
  );
}

function Banner({ tone, icon, children }: { tone: 'ok' | 'err'; icon: ReactNode; children: ReactNode }) {
  const t = tone === 'ok' ? { bg: C.okBg, border: C.okBorder, fg: C.okText } : { bg: C.errBg, border: C.errBorder, fg: C.errText };
  return (
    <div style={{ border: `1px solid ${t.border}`, background: t.bg, borderRadius: 8, padding: '10px 12px', display: 'flex', gap: 9, alignItems: 'flex-start' }}>
      <span style={{ flexShrink: 0, marginTop: 1, display: 'grid', placeItems: 'center' }}>{icon}</span>
      <div style={{ fontSize: 12.5, lineHeight: 1.45, color: t.fg }}>{children}</div>
    </div>
  );
}

function ErrorBanner({ children }: { children: ReactNode }) {
  return <Banner tone="err" icon={<ErrorOutlineIcon sx={{ fontSize: 16, color: C.errText }} />}>{children}</Banner>;
}

function BackLink({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} style={{ ...linkBtn, display: 'inline-flex', alignItems: 'center', gap: 6, alignSelf: 'center' }}>
      <ArrowBackIcon sx={{ fontSize: 15 }} /> Voltar ao login
    </button>
  );
}

function EyeToggle({ show, onToggle }: { show: boolean; onToggle: () => void }) {
  return (
    <button type="button" onClick={onToggle} aria-label={show ? 'Ocultar senha' : 'Mostrar senha'} style={{ display: 'grid', placeItems: 'center', width: 28, height: 28, border: 'none', background: 'transparent', color: C.faint, cursor: 'pointer' }}>
      {show ? <VisibilityOffOutlinedIcon sx={{ fontSize: 18 }} /> : <VisibilityOutlinedIcon sx={{ fontSize: 18 }} />}
    </button>
  );
}

function PrimaryButton({ disabled, children }: { disabled?: boolean; children: ReactNode }) {
  return (
    <button
      type="submit"
      disabled={disabled}
      style={{ height: 44, fontSize: 14, fontWeight: 600, borderRadius: 8, background: C.accent, color: '#fff', border: 'none', cursor: disabled ? 'default' : 'pointer', opacity: disabled ? 0.55 : 1 }}
      onMouseEnter={(e) => { if (!disabled) e.currentTarget.style.background = C.accentHover; }}
      onMouseLeave={(e) => (e.currentTarget.style.background = C.accent)}
    >
      {children}
    </button>
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
