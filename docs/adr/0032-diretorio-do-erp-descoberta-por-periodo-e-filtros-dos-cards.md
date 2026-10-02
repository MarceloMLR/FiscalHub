# ADR-0032: O diretório vem do ERP, a descoberta por período lê o D365, o CNPJ guarda as letras, e os cards ganham período e modelo

- **Status:** Aceito
- **Data:** 2026-10-02
- **Revisa:**
  - **ADR-0013:** a porta do diretório recebe o tenant, e a primeira fonte real é o cadastro do ERP, e não a Avalara. O
    `companies.json` passa a ser o fallback de desenvolvimento.
  - **ADR-0014:** a integração manual e a agendada descobrem pela implementação do adapter de entrada do perfil. O D365
    ganha a dele, e o catálogo local passa a existir só em Development. As referências vão para a fila de descoberta.
  - **ADR-0028:** fecha o item "o diretório de empresas não é por tenant".
  - **ADR-0030:** a empresa do grupo é o CNPJ sem a pontuação e com as letras, e não o "CNPJ de 14 dígitos".
- **Change OpenSpec:** `openspec/changes/erp-company-directory-and-card-filters`. As capacidades são `company-directory`,
  `period-discovery`, `tax-identifier-normalization`, `document-grouping`, `d365-change-feed`, `d365-document-assembly`,
  `avalara-document-contract` e `inbound-source-resolution`.
- **Verificado contra o fiscosysdev em 2026-10-01:**
  - a `FiscalEstablishments`, com os quatro estabelecimentos da `brmf`;
  - o filtro por dia fiscal da `FSFiscalDocumentBRs`, com as duas NFS-e da `SP-01` em 2026-08-07.

## Contexto

A integração manual e o agendamento ofereciam empresas que não existem, e descobriam notas que não são do tenant:

- **O diretório:** o `companies.json` é um mock (`12345678`, "Empresa Emitente LTDA"). O host o registrava em qualquer
  ambiente, igual para todo tenant.
- **A descoberta:** o catálogo fixo dos XMLs de exemplo era a única implementação. Um agendamento para o
  `44278225000260`/`SP-01` acharia 0 notas: só a integração automática lia o 365.
- **O CNPJ:** perdia as letras em seis lugares (o grupo do feed, a montagem, a tradução e o parceiro da Avalara, a
  derivação do XML e a máscara da tela). O CNPJ alfanumérico, válido para inscrições novas desde julho de 2026, cairia
  numa empresa que não existe: `12ABC34501DE35` virava `123450135`.
- **Os cards:** só contavam o dia de hoje, somando no navegador os 200 grupos mais recentes.
- **A fila:** a descoberta por período publicava na fila de entrada, e o coletor na de descoberta. Cada fila consome uma
  mensagem por vez, mas cada uma por conta própria.

## Decisão

**O diretório e a descoberta por período são os do ERP do tenant, escolhidos pelo adapter de entrada do perfil, e o mock
só existe em Development. O CNPJ é o mesmo texto em todo o hub, sem a pontuação e com as letras. Os cards contam no
servidor, pela janela e pelo modelo.**

1. **O diretório lê a entidade padrão da Microsoft.**
   - **A fonte:** a `FiscalEstablishments`, entre empresas e filtrada pelas `companies` do perfil, com o token e as
     settings do coletor.
   - **A chave:** a empresa é o CNPJ do estabelecimento, normalizado (5). A filial é o `FiscalEstablishmentId`. É a mesma
     chave dos grupos, sem tradução.
   - **O cadastro, e não as notas:** um estabelecimento sem nota aparece, como o `RJ-01`.
   - **Por que não uma `FS*`:** as nossas existem porque a Microsoft não publica entidade sobre a `FiscalDocument_BR`
     (ADR-0022). Esta ela publica, com nome fixo. A role do pacote passa a referenciar o privilégio padrão
     `FiscalEstablishmentEntityView`, que exige build e deploy, e não sync.
   - **A falha:** vira um motivo curto e seguro. O 403 cita a role e o privilégio. Nunca o token, o segredo, o corpo nem
     um cabeçalho.
2. **A porta recebe o tenant, e a implementação é a do adapter de entrada.**
   - **A regra:** a comparação exata da origem, como na resolução do source (ADR-0025). Ela vale para o diretório e para
     a descoberta por período.
   - **Sem implementação:** o diretório responde "este ERP não tem diretório" (404), e a execução manual responde que o
     ERP não tem descoberta (409). Nada é inventado.
3. **O mock só em Development, no desenho do Azure CLI.**
   - **O registro:** o `companies.json` e o catálogo local entram por dois métodos explícitos, só sob `IsDevelopment()`,
     como serviço keyed com a chave do fallback. Eles não entram na lista das implementações.
   - **Quem eles atendem:** só o tenant cujo ERP não tem implementação. Nunca no lugar de uma que existe.
   - **Fora de Development:** não existem. É o vazamento que o ADR-0028 registrou, e esta decisão o fecha.
4. **A descoberta por período do D365 é a mesma referência do coletor.**
   - **O que ela lê:** o dia fiscal (`FiscalDocumentDate`) entre o dia do início e o do fim, cada um no próprio fuso. O
     estabelecimento vem do cadastro (1), pelo par (`dataAreaId`, código), e o CNPJ entra só numa guarda.
   - **A leitura:** keyset por `FiscalDocumentRecId`, pelo motivo do ADR-0024.
   - **A referência:** o mesmo código monta a do coletor e a da descoberta. A nota cai no mesmo registro e no mesmo grupo,
     sem linha duplicada.
   - **O reprocesso:** pergunta às descobertas do tenant, a do ERP primeiro e o catálogo local depois (em Development). As
     formas de chave são disjuntas, e o D365 recusa a outra sem rede. A nota do D365 passa a ser reprocessável.
   - **A fila:** a execução manual, a agendada e o reprocesso publicam na fila de descoberta, a do coletor. Em filas
     diferentes, duas cópias da mesma nota passariam juntas pela checagem de idempotência.
5. **O CNPJ e o CPF: sem ponto, barra, hífen e espaço, com as letras e a caixa como vieram.**
   - **Onde:** uma função pura no Domain, usada no grupo, na montagem, no diretório, na tradução, no parceiro e na
     derivação do XML.
   - **O que ela não faz:** não confere tamanho nem dígito verificador, e não converte a caixa (ADR-0026).
   - **O CNPJ numérico:** o resultado é o de antes, e não há o que migrar.
   - **A tela:** a máscara é pelo tamanho (14 caracteres). O NCM, o CFOP e o CEP continuam só com dígitos.
6. **Os cards contam no servidor, pela janela e pelo modelo.**
   - **A janela:** os últimos N dias são hoje e os N−1 anteriores, pelo dia do navegador. O "Dia" é o padrão, e há 7, 15
     e 30 dias.
   - **O modelo:** todos, ou um dos modelos da janela.
   - **A contagem:** o `GET /groups/totals` conta por modelo, sobre todas as notas da janela.
   - **A tabela:** o grupo ganha o modelo, e o modal lista exatamente a linha (tipo, modelo e modo). A tabela continua o
     histórico completo, sem os filtros dos cards.
7. **O `establishments` continua configuração digitada, e não é semeado pelo diretório.**
   - **Onde ele está:** não está na tela, só no seed e por SQL.
   - **Por que não semear:** os códigos são da plataforma e nunca vêm do ERP (ADR-0026 §3). O diretório daria só a chave,
     e uma entrada sem os códigos é rejeitada do mesmo jeito. Semear faria o hub escrever configuração que o Admin não
     digitou.
   - **O próximo passo:** a tela da tradução, com as linhas vindas do diretório.

## Alternativas consideradas

- **Uma `FS*` sobre a `FiscalEstablishment_BR`.** Descartada. É um contrato a mais para manter, quando a Microsoft já
  publica a entidade com nome fixo.
- **O diretório tirado das notas processadas.** Descartada pelo ADR-0013: o estabelecimento sem movimento some.
- **Apagar o `companies.json` e o catálogo local.** Descartada. O catálogo serve o reprocesso das notas de exemplo, e o par
  é o caminho de desenvolvimento do tenant sem adapter de ERP. O defeito era registrá-los em todo ambiente.
- **Um fallback silencioso para o mock em produção.** Descartada. É a lista de outro cliente no dropdown (ADR-0028).
- **Filtrar o CNPJ no servidor do F&O.** Descartada. O documento guarda o CNPJ formatado como o F&O formata, e o hub só
  conhece a forma normalizada. O código do estabelecimento é exato.
- **Guardar a origem no registro do documento, para o reprocesso.** Fica para quando houver um ERP cuja chave se confunda
  com a de outro. Hoje a ordem das descobertas basta.
- **A fila pela origem** (D365 na de descoberta, XML na de entrada). Descartada: é uma regra a mais, sem ganho.
- **Somar os cards no navegador, com um limite maior de grupos.** Descartada. Qualquer limite trunca em silêncio num
  tenant grande.
- **Semear o `establishments` pelo diretório.** Descartada (7).

## Consequências

- **Na tela:** o dropdown mostra o cadastro do ERP do tenant, com o CNPJ mascarado e o nome, e o agendamento acha as notas
  do 365. Os cards respondem a semana e o modelo.
- **A nota do D365 é reprocessável,** e o reprocesso é envio real, como o do XML.
- **O CNPJ alfanumérico** atravessa o hub com o mesmo valor. A caixa preservada faz `12abc…` não casar com `12ABC…`: é
  uma rejeição visível, e não uma junção silenciosa.
- **Limites conhecidos, no STATUS:**
  - o estabelecimento removido do cadastro não é achado pela descoberta por período;
  - o agendamento de um tenant sem descoberta retenta a cada passada;
  - o teste de credencial não lê a `FiscalEstablishments`;
  - a tela da tradução de estabelecimentos.
- **O pacote do D365** continua metadado puro: 22 entidades, 22 privilégios próprios, uma role e um privilégio padrão
  referenciado.
