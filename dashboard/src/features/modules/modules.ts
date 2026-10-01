import type { ModuleName } from '../../types';

// Os sub-blocos do bloco "Integrações" da barra lateral, na ordem dela. Os três módulos aparecem pelos módulos do tenant
// (GET /info), e o Agendamento sempre: é por ele que as cargas manuais entram (D1). Esconder um módulo é apresentação, e
// não permissão: a API continua respondendo para ele (D2).
export type ModuleView = 'documents' | 'accounting' | 'inventory' | 'integrations';

export const MODULE_VIEWS: { view: ModuleView; module: ModuleName | null; label: string }[] = [
  { view: 'documents', module: 'Fiscal', label: 'Fiscal' },
  { view: 'accounting', module: 'Contabil', label: 'Contábil' },
  { view: 'inventory', module: 'Inventario', label: 'Inventário' },
  { view: 'integrations', module: null, label: 'Agendamento' },
];

// Enquanto o /info não chega, a barra mostra o que mostrava antes dos módulos: só o Fiscal.
export const DEFAULT_MODULES: ModuleName[] = ['Fiscal'];

export function visibleModuleViews(modules: ModuleName[]) {
  return MODULE_VIEWS.filter((m) => m.module === null || modules.includes(m.module));
}

// A tela de entrada: o Fiscal; sem ele, o primeiro módulo do tenant; sem módulo nenhum, o Agendamento.
export function entryView(modules: ModuleName[]): ModuleView {
  return visibleModuleViews(modules)[0].view;
}

export function isModuleView(view: string): view is ModuleView {
  return MODULE_VIEWS.some((m) => m.view === view);
}
