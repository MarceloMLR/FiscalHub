import type { ConnectorInfo, ModuleName } from '../../types';

// O modelo de navegação da barra lateral (spec module-navigation; ADR-0039 §1 e §2; design D10 e D11). O eixo é o
// **conector**, com os módulos ligados nele abaixo, em dois níveis: a integração não é item de menu — ela é coluna e
// filtro dentro da página do módulo (D9).
//
// O descuido que o ADR-0039 avisa é um item apontando para a tela que não é a dele, e ele era possível porque o item, o
// título e a tela eram três tabelas separadas. Aqui **cada item carrega a identidade, o rótulo, o título, o subtítulo e
// a tela que abre**: o App.tsx desenha o que o modelo diz e abre o que o item diz.

export type ModuleView = 'documents' | 'accounting' | 'inventory' | 'integrations';

export interface ModuleViewDefinition {
  view: ModuleView;
  // O módulo do hub que o item representa; `null` é o Agendamento, que não é módulo.
  module: ModuleName | null;
  label: string;
  title: string;
  subtitle: string;
}

// A tabela dos módulos do hub, na ordem da barra lateral — Fiscal, Contábil, Inventário, e o Agendamento depois deles.
// Ela não serve só à navegação: o `features/connectors/connectorPanelRows.ts` a importa para montar as linhas de módulo
// do painel do conector, em Configurações. Os rótulos de módulo são um lugar só, lido pelos dois.
export const MODULE_VIEWS: ModuleViewDefinition[] = [
  { view: 'documents', module: 'Fiscal', label: 'Fiscal', title: 'Fiscal', subtitle: 'Notas fiscais integradas e seus status' },
  { view: 'accounting', module: 'Contabil', label: 'Contábil', title: 'Contábil', subtitle: 'Módulo reservado' },
  { view: 'inventory', module: 'Inventario', label: 'Inventário', title: 'Inventário', subtitle: 'Módulo reservado' },
  {
    view: 'integrations',
    module: null,
    label: 'Agendamento',
    title: 'Agendamento',
    subtitle: 'Dispare agora ou agende, e acompanhe as execuções',
  },
];

// Um item da barra lateral. A identidade é o par **(conector, módulo)**, e não o módulo sozinho: com dois conectores
// existem dois Fiscais, e eles abrem páginas diferentes. Hoje a tela é escolhida pelo módulo, mas a chave já nasce
// completa, para que o segundo conector não precise reabrir o modelo (D10).
export interface NavItem {
  key: string;
  // O adapter do conector; `null` só no grupo que nomeia a ausência.
  connector: string | null;
  module: ModuleName | null;
  // A tela que o item abre — declarada aqui, e não escolhida por um ternário sobre o nome dela.
  view: ModuleView;
  label: string;
  title: string;
  subtitle: string;
}

// Um grupo da barra lateral: um conector do tenant, rotulado pelo rótulo que o adapter dele declara — nunca pela
// plataforma nem pela chave do adapter.
export interface NavGroup {
  key: string;
  label: string;
  items: NavItem[];
}

// O grupo do tenant cujo conector nenhum produz item (D5). O rótulo **nomeia a ausência**, em vez de inventar um
// conector: para o Admin, o conserto está em Configurações, no bloco logo abaixo; para os demais papéis, é a informação
// de que a configuração ainda não chegou.
export const NO_CONNECTOR_LABEL = 'Sem conector configurado';

const NO_CONNECTOR_KEY = '';

// A árvore que a barra lateral desenha, a partir dos conectores do /info, na ordem gravada no perfil. Um conector que
// não produz item nenhum — sem módulo ligado e sem agendamento — não vira grupo: cabeçalho sem item é ruído. Nenhum
// grupo sobrando, entra o que nomeia a ausência: a estrutura é **uma só**, para qualquer cliente, e a barra lateral
// nunca fica sem grupo nem com itens soltos.
//
// **O nulo não é o vazio.** `undefined` é "o /info ainda não respondeu", e aí não há árvore: a barra lateral não
// desenha grupo nenhum e **não afirma ausência de conector**, porque ainda não sabe. A árvore **vazia** é uma
// resposta — essa, sim, nomeia a ausência. É o mesmo cuidado que o D7 tem com os botões do JSON e o selo de ambiente
// tem com o rodapé: não afirmar nada sobre a configuração do cliente antes de saber.
export function navigationGroups(connectors: ConnectorInfo[] | undefined): NavGroup[] {
  if (connectors === undefined) return [];
  const groups = connectors.map(groupOf).filter((g) => g.items.length > 0);
  return groups.length > 0 ? groups : [noConnectorGroup()];
}

// Os itens de um conector: os módulos ligados nele, na ordem da tabela (e não na do perfil), seguidos do Agendamento,
// quando o adapter daquele conector declara que ele agenda. Não existe agendamento para todo conector: cálculo e
// emissão são disparados pelo ERP, e não por horário (ADR-0039 §2).
function groupOf(connector: ConnectorInfo): NavGroup {
  const views = MODULE_VIEWS.filter((v) => (v.module === null ? connector.scheduling : connector.modules.includes(v.module)));
  return {
    key: connector.adapter,
    label: connector.label,
    items: views.map((v) => itemOf(connector.adapter, v)),
  };
}

// O mesmo conjunto de antes desta mudança — Fiscal e Agendamento —, na mesma ordem e abrindo as mesmas telas: o tenant
// não provisionado continua entrando no produto e vendo o que via.
function noConnectorGroup(): NavGroup {
  const views = MODULE_VIEWS.filter((v) => v.module === 'Fiscal' || v.module === null);
  return {
    key: NO_CONNECTOR_KEY,
    label: NO_CONNECTOR_LABEL,
    items: views.map((v) => itemOf(null, v)),
  };
}

function itemOf(connector: string | null, v: ModuleViewDefinition): NavItem {
  return {
    key: `${connector ?? NO_CONNECTOR_KEY}/${v.view}`,
    connector,
    module: v.module,
    view: v.view,
    label: v.label,
    title: v.title,
    subtitle: v.subtitle,
  };
}

// A árvore achatada, na ordem em que a barra lateral a desenha.
export function navigationItems(groups: NavGroup[]): NavItem[] {
  return groups.flatMap((g) => g.items);
}

// A tela de entrada: o primeiro item do primeiro grupo (D11). Existe sempre que **houve resposta** — todo grupo da
// árvore tem item, e a árvore vazia vira o grupo que nomeia a ausência. A árvore do "ainda não respondeu" não tem
// grupo nenhum, e quem a recebe não chama isto: espera a resposta.
export function entryItem(groups: NavGroup[]): NavItem {
  return groups[0].items[0];
}

// O item mostrado. O selecionado que deixa de existir — o módulo que o Admin acabou de desligar, o conector que saiu —
// cai na tela de entrada (D11).
export function shownItem(groups: NavGroup[], key: string | null): NavItem {
  return navigationItems(groups).find((i) => i.key === key) ?? entryItem(groups);
}
