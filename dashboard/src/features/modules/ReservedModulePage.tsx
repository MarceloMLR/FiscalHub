// O lugar reservado de Contábil e Inventário (design D1). Não é um filtro dos documentos fiscais: é outro domínio, com
// outro modelo, outras portas e outros adapters, e cada um vira uma fatia própria. Por isso esta tela não mostra
// documento, card nem contagem nenhuma. Marcar o módulo em Configurações não entrega integração.
export function ReservedModulePage({ label }: { label: string }) {
  return (
    <div
      style={{
        flex: 1,
        padding: '28px',
        maxWidth: 1040,
        width: '100%',
        margin: '0 auto',
        boxSizing: 'border-box',
      }}
    >
      <div
        style={{
          border: '1px dashed var(--border)',
          borderRadius: 10,
          padding: '28px 24px',
          background: 'var(--surface)',
          display: 'flex',
          flexDirection: 'column',
          gap: 8,
        }}
      >
        <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--ink)' }}>{label}</div>
        <div style={{ fontSize: 13.5, color: 'var(--text)', lineHeight: 1.55 }}>Este módulo ainda não está disponível.</div>
      </div>
    </div>
  );
}
