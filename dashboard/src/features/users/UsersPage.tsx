import { useEffect, useState, type CSSProperties, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { GridColDef } from '@mui/x-data-grid';
import PersonAddAlt1OutlinedIcon from '@mui/icons-material/PersonAddAlt1Outlined';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import LockResetOutlinedIcon from '@mui/icons-material/LockResetOutlined';
import BlockOutlinedIcon from '@mui/icons-material/BlockOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import BusinessOutlinedIcon from '@mui/icons-material/BusinessOutlined';
import { api } from '../../api/client';
import { FhDataGrid } from '../../components/FhDataGrid';
import { Modal } from '../../components/Modal';
import { useAuth } from '../auth/AuthContext';
import type { AdminUser, TenantInfo, UserRole } from '../../types';

type ModalState =
  | { type: 'new' }
  | { type: 'edit'; user: AdminUser }
  | { type: 'reset'; user: AdminUser }
  | null;

export function UsersPage() {
  const { user: me } = useAuth();
  const qc = useQueryClient();
  const [modal, setModal] = useState<ModalState>(null);

  const users = useQuery({ queryKey: ['adminUsers'], queryFn: api.users });
  const tenant = useQuery({ queryKey: ['tenant'], queryFn: api.tenant });

  const toggleActive = useMutation({
    mutationFn: (u: AdminUser) => api.updateUser(u.id, { active: !u.active }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminUsers'] }),
  });

  const columns: GridColDef<AdminUser>[] = [
    {
      field: 'name',
      headerName: 'Nome',
      flex: 1.3,
      minWidth: 160,
      cellClassName: 'fhFirstCol',
      renderCell: (p) => <span style={{ fontWeight: 600, color: 'var(--ink)' }}>{p.value as string}</span>,
    },
    {
      field: 'email',
      headerName: 'E-mail',
      flex: 1.6,
      minWidth: 200,
      renderCell: (p) => <span style={{ color: 'var(--muted)' }}>{p.value as string}</span>,
    },
    {
      field: 'role',
      headerName: 'Papel',
      width: 130,
      renderCell: (p) => <RoleChip role={p.value as UserRole} />,
    },
    {
      field: 'active',
      headerName: 'Status',
      width: 120,
      renderCell: (p) => <ActiveChip active={p.value as boolean} />,
    },
    {
      field: 'actions',
      headerName: 'Ações',
      width: 210,
      sortable: false,
      filterable: false,
      renderCell: (p) => {
        const u = p.row;
        const isSelf = u.email === me?.email;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 4, height: '100%' }}>
            <IconAction title="Editar" onClick={() => setModal({ type: 'edit', user: u })}>
              <EditOutlinedIcon sx={{ fontSize: 16 }} />
            </IconAction>
            <IconAction title="Redefinir senha" onClick={() => setModal({ type: 'reset', user: u })}>
              <LockResetOutlinedIcon sx={{ fontSize: 16 }} />
            </IconAction>
            <IconAction
              title={isSelf ? 'Você não pode desativar a própria conta' : u.active ? 'Desativar' : 'Ativar'}
              disabled={isSelf || toggleActive.isPending}
              tone={u.active ? 'danger' : 'ok'}
              onClick={() => toggleActive.mutate(u)}
            >
              {u.active ? <BlockOutlinedIcon sx={{ fontSize: 16 }} /> : <CheckCircleOutlineIcon sx={{ fontSize: 16 }} />}
            </IconAction>
          </div>
        );
      },
    },
  ];

  const rows = users.data ?? [];

  return (
    <div style={{ padding: '20px 28px', display: 'flex', flexDirection: 'column', gap: 18, flex: 1, minHeight: 0 }}>
      <TenantCard tenant={tenant.data} />

      {/* Cabeçalho da seção de usuários */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16 }}>
        <div>
          <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--ink)' }}>Usuários</div>
          <div style={{ fontSize: 12.5, color: 'var(--muted)', marginTop: 1 }}>
            {rows.length} {rows.length === 1 ? 'pessoa com acesso' : 'pessoas com acesso'} a este tenant
          </div>
        </div>
        <button type="button" className="fh-btn" onClick={() => setModal({ type: 'new' })} style={{ height: 36 }}>
          <PersonAddAlt1OutlinedIcon sx={{ fontSize: 17 }} />
          Novo usuário
        </button>
      </div>

      {/* Tabela */}
      <div
        style={{
          flex: 1,
          minHeight: 320,
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: '10px',
          overflow: 'hidden',
        }}
      >
        <FhDataGrid
          rows={rows}
          columns={columns}
          getRowId={(r) => r.id}
          loading={users.isLoading}
        />
      </div>

      {modal?.type === 'new' && <UserFormModal onClose={() => setModal(null)} />}
      {modal?.type === 'edit' && <UserFormModal user={modal.user} onClose={() => setModal(null)} />}
      {modal?.type === 'reset' && <ResetPasswordModal user={modal.user} onClose={() => setModal(null)} />}
    </div>
  );
}

// ─────────────────────────── Card do tenant ───────────────────────────
function TenantCard({ tenant }: { tenant?: TenantInfo }) {
  const qc = useQueryClient();
  const [name, setName] = useState('');
  const [cnpj, setCnpj] = useState('');

  useEffect(() => {
    if (tenant) {
      setName(tenant.name ?? '');
      setCnpj(tenant.cnpj ?? '');
    }
  }, [tenant]);

  const save = useMutation({
    mutationFn: () => api.saveTenant({ name: name.trim(), cnpj: cnpj.trim() || null }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tenant'] }),
  });

  const dirty = tenant ? name.trim() !== (tenant.name ?? '') || cnpj.trim() !== (tenant.cnpj ?? '') : false;

  return (
    <div
      style={{
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: '10px',
        padding: '16px 18px',
        display: 'flex',
        flexDirection: 'column',
        gap: 14,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <div
          style={{
            width: 30,
            height: 30,
            borderRadius: 8,
            background: 'var(--accent-tint)',
            color: 'var(--accent)',
            display: 'grid',
            placeItems: 'center',
            flexShrink: 0,
          }}
        >
          <BusinessOutlinedIcon sx={{ fontSize: 17 }} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--ink)' }}>Dados do tenant</div>
          <div className="fh-mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
            {tenant?.tenantId ?? '—'}
          </div>
        </div>
        {tenant && <ActiveChip active={tenant.active} />}
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1.6fr) minmax(0,1fr) auto', gap: 12, alignItems: 'flex-end' }}>
        <Field label="Razão social">
          <input style={inputStyle} value={name} onChange={(e) => setName(e.target.value)} placeholder="Nome do cliente" />
        </Field>
        <Field label="CNPJ">
          <input style={inputStyle} value={cnpj} onChange={(e) => setCnpj(e.target.value)} placeholder="00.000.000/0001-00" />
        </Field>
        <button
          type="button"
          className="fh-btn"
          onClick={() => save.mutate()}
          disabled={!dirty || !name.trim() || save.isPending}
          style={{ height: 38 }}
        >
          {save.isPending ? 'Salvando…' : 'Salvar'}
        </button>
      </div>

      {save.isError && <ErrorLine>{(save.error as Error).message}</ErrorLine>}
    </div>
  );
}

// ─────────────────────────── Modal: novo / editar usuário ───────────────────────────
function UserFormModal({ user, onClose }: { user?: AdminUser; onClose: () => void }) {
  const qc = useQueryClient();
  const editing = Boolean(user);
  const [email, setEmail] = useState(user?.email ?? '');
  const [name, setName] = useState(user?.name ?? '');
  const [role, setRole] = useState<UserRole>(user?.role ?? 'Viewer');
  const [password, setPassword] = useState('');

  const mutation = useMutation({
    mutationFn: () =>
      editing
        ? api.updateUser(user!.id, { name: name.trim(), role })
        : api.createUser({ email: email.trim(), name: name.trim(), role, password }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['adminUsers'] });
      onClose();
    },
  });

  const valid = editing
    ? name.trim().length > 0
    : email.includes('@') && name.trim().length > 0 && password.length >= 6;

  return (
    <Modal
      title={editing ? 'Editar usuário' : 'Novo usuário'}
      subtitle={editing ? user!.email : 'Cria o acesso dentro deste tenant'}
      onClose={onClose}
      maxWidth={460}
      footer={
        <>
          <button type="button" className="fh-btn fh-btn-secondary" onClick={onClose} style={{ height: 34 }}>
            Cancelar
          </button>
          <button type="button" className="fh-btn" onClick={() => mutation.mutate()} disabled={!valid || mutation.isPending} style={{ height: 34 }}>
            {mutation.isPending ? 'Salvando…' : editing ? 'Salvar' : 'Criar usuário'}
          </button>
        </>
      }
    >
      <div style={{ padding: '18px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        {!editing && (
          <Field label="E-mail">
            <input style={inputStyle} value={email} onChange={(e) => setEmail(e.target.value)} placeholder="pessoa@empresa.com" autoFocus />
          </Field>
        )}
        <Field label="Nome">
          <input style={inputStyle} value={name} onChange={(e) => setName(e.target.value)} placeholder="Nome completo" autoFocus={editing} />
        </Field>
        <Field label="Papel">
          <select style={inputStyle} value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
            <option value="Viewer">Viewer — só leitura</option>
            <option value="Admin">Admin — gerencia o tenant</option>
          </select>
        </Field>
        {!editing && (
          <Field label="Senha inicial" hint="Mínimo de 6 caracteres. A pessoa pode trocar depois.">
            <input style={inputStyle} type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••" />
          </Field>
        )}
        {mutation.isError && <ErrorLine>{(mutation.error as Error).message}</ErrorLine>}
      </div>
    </Modal>
  );
}

// ─────────────────────────── Modal: redefinir senha ───────────────────────────
function ResetPasswordModal({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [done, setDone] = useState(false);

  const mutation = useMutation({
    mutationFn: () => api.resetUserPassword(user.id, password),
    onSuccess: () => setDone(true),
  });

  return (
    <Modal
      title="Redefinir senha"
      subtitle={user.name}
      onClose={onClose}
      maxWidth={420}
      footer={
        done ? (
          <button type="button" className="fh-btn" onClick={onClose} style={{ height: 34 }}>
            Fechar
          </button>
        ) : (
          <>
            <button type="button" className="fh-btn fh-btn-secondary" onClick={onClose} style={{ height: 34 }}>
              Cancelar
            </button>
            <button type="button" className="fh-btn" onClick={() => mutation.mutate()} disabled={password.length < 6 || mutation.isPending} style={{ height: 34 }}>
              {mutation.isPending ? 'Aplicando…' : 'Redefinir'}
            </button>
          </>
        )
      }
    >
      <div style={{ padding: '18px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        {done ? (
          <div style={{ fontSize: 13.5, color: 'var(--text)', lineHeight: 1.5 }}>
            Senha de <strong>{user.name}</strong> redefinida. Repasse a nova senha com segurança — ela não fica
            visível aqui.
          </div>
        ) : (
          <Field label="Nova senha" hint="Mínimo de 6 caracteres.">
            <input style={inputStyle} type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••" autoFocus />
          </Field>
        )}
        {mutation.isError && <ErrorLine>{(mutation.error as Error).message}</ErrorLine>}
      </div>
    </Modal>
  );
}

// ─────────────────────────── Peças reutilizáveis ───────────────────────────
const inputStyle: CSSProperties = {
  width: '100%',
  height: 38,
  boxSizing: 'border-box',
  padding: '0 11px',
  borderRadius: 8,
  border: '1px solid var(--border)',
  background: 'var(--surface)',
  color: 'var(--text)',
  fontSize: 13.5,
  fontFamily: 'inherit',
  outline: 'none',
};

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <span className="fh-label" style={{ fontSize: 11, letterSpacing: '0.06em' }}>
        {label}
      </span>
      {children}
      {hint && <span style={{ fontSize: 11.5, color: 'var(--muted)' }}>{hint}</span>}
    </label>
  );
}

function ErrorLine({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        border: '1px solid var(--error-border)',
        background: 'var(--error-bg)',
        color: 'var(--error-text)',
        borderRadius: 8,
        padding: '9px 11px',
        fontSize: 12.5,
        lineHeight: 1.45,
      }}
    >
      {children}
    </div>
  );
}

function RoleChip({ role }: { role: UserRole }) {
  const admin = role === 'Admin';
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        fontSize: 12,
        fontWeight: 600,
        lineHeight: 1,
        padding: '4px 9px',
        borderRadius: 6,
        background: admin ? 'var(--accent-tint)' : 'var(--surface-2)',
        color: admin ? 'var(--accent)' : 'var(--muted)',
        border: `1px solid ${admin ? 'var(--accent-tint)' : 'var(--border)'}`,
      }}
    >
      {role}
    </span>
  );
}

function ActiveChip({ active }: { active: boolean }) {
  const t = active
    ? { bg: 'var(--ok-bg)', border: 'var(--ok-border)', fg: 'var(--ok-text)' }
    : { bg: 'var(--error-bg)', border: 'var(--error-border)', fg: 'var(--error-text)' };
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 12,
        fontWeight: 600,
        lineHeight: 1,
        padding: '4px 9px',
        borderRadius: 6,
        background: t.bg,
        border: `1px solid ${t.border}`,
        color: t.fg,
      }}
    >
      <span style={{ width: 5, height: 5, borderRadius: 999, background: t.fg }} />
      {active ? 'Ativo' : 'Inativo'}
    </span>
  );
}

function IconAction({
  title,
  onClick,
  disabled,
  tone,
  children,
}: {
  title: string;
  onClick: () => void;
  disabled?: boolean;
  tone?: 'danger' | 'ok';
  children: ReactNode;
}) {
  const color = tone === 'danger' ? 'var(--error-text)' : tone === 'ok' ? 'var(--ok-text)' : 'var(--muted)';
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      style={{
        width: 30,
        height: 30,
        display: 'grid',
        placeItems: 'center',
        borderRadius: 7,
        border: '1px solid var(--border)',
        background: 'var(--surface)',
        color,
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.4 : 1,
      }}
      onMouseEnter={(e) => {
        if (!disabled) e.currentTarget.style.background = 'var(--surface-2)';
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.background = 'var(--surface)';
      }}
    >
      {children}
    </button>
  );
}
