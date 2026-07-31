# ADR-0021: Adapter de chamados de suporte (Freshdesk) — porta + anexos zipados por nota

- **Status:** Aceito
- **Data:** 2026-07-31

## Contexto

Quando uma nota falha na integração, o usuário hoje sairia do FiscalHub pra abrir um chamado no
suporte (Freshdesk, Dynamics 365 Customer Service…) e ainda teria que juntar na mão os logs e o
contexto. Isso agrega muito valor ao conector: **abrir o chamado direto da tela**, já com as infos de
integração e os arquivos de rastreabilidade anexados. Freshdesk é o primeiro provider.

Requisitos levantados: o usuário digita **título e descrição** num modal; o chamado carrega
**número da nota, status, motivo do erro e demais infos de integração**; dá pra **selecionar várias
notas** no mesmo chamado (caso comum: várias notas com o mesmo problema); e como o Freshdesk limita o
anexo (**"The total size of these attachments cannot exceed 20MB"**), os logs vão **zipados por nota**
pra reduzir tamanho. Tem que ser interativo, não um "abre e pronto".

## Decisão

- **Porta `ISupportTicketGateway`** (provider-agnóstica): abre um chamado a partir de assunto,
  descrição e anexos, devolvendo id + URL. Freshdesk é **um adapter**; D365 Customer Service e outros
  entram depois sem tocar no núcleo.
- **`SupportTicketService` (Application)** orquestra: recebe o tenant, a lista de notas, o assunto e a
  descrição do usuário; para cada nota **busca os arquivos de rastreabilidade** (origem/domínio/
  destino) e monta **um zip por nota** (`{naturalKey}.zip`); compõe uma **descrição estruturada** (um
  bloco por nota: número, status, motivo, external id, timestamps) + a descrição do usuário; respeita
  o **teto de 20MB** e chama o gateway.
- **Gatilho manual, multi-nota.** Botão no detalhe da nota (uma nota) e seleção múltipla no modal do
  grupo (várias notas num só chamado).
- **Config por tenant no perfil de conector** (ADR-0019): `SupportAdapter` + `SupportSettings` (domínio
  Freshdesk + `apiKeyRef` como referência `kv:`, requester padrão, prioridade). Segredo **nunca** em
  claro — resolvido no Key Vault. Adapter escolhido em runtime, igual entrada/saída.
- **Adapter Freshdesk real**: `POST /api/v2/tickets` em `multipart/form-data` (campos subject,
  description, email/requester, priority, status + `attachments[]`), **Basic auth** com a API key
  (`apiKey:X` em base64). Devolve o id e monta a URL `https://{dominio}/a/tickets/{id}`.
- **Adapter mock local** (dev/testes): não exige conta Freshdesk; guarda o "chamado" e devolve um id
  fake — permite exercitar todo o fluxo (porta, zip, UI) sem credenciais.

## Consequências

- **Abrir chamado é ação de qualquer usuário autenticado** (não só Admin) — é operação do dia a dia.
  O endpoint é escopado ao tenant (ninguém abre chamado com notas de outro tenant).
- **Zip por nota** cabe melhor no teto de 20MB do que os arquivos crus, e mantém os logs de cada nota
  agrupados/legíveis dentro do chamado.
- **Multi-nota** junta o problema comum num único chamado, evitando enxurrada de tickets.
- Novo provider (D365) = **novo adapter** implementando a mesma porta; o service, a UI e o endpoint
  não mudam.

## Alternativas consideradas

- **Gatilho automático no dead-letter** — adiado (evolução). Exige idempotência por nota pra não abrir
  chamado duplicado a cada retry; o manual já exercita todo o caminho.
- **Anexar arquivos crus (sem zip)** — recusado: estoura o teto de 20MB mais rápido e polui o chamado
  com muitos anexos soltos.
- **Tabela própria de "chamados"** no banco — desnecessária agora; o Freshdesk é a fonte da verdade do
  ticket. Se precisarmos de histórico local depois, entra como evolução.
