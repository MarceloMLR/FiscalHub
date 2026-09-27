# integration-validation Specification

## Purpose

Definir o que o hub se permite julgar antes de enviar um documento: só o que impede a requisição de
existir. O conteúdo fiscal (CST, base, alíquota, grupo de tributos ausente) é julgado pela plataforma de
compliance, que responde, e o hub mostra a resposta.

## Requirements

### Requirement: Rejeição do lado do hub só por motivo estrutural

Antes da resposta da plataforma, o hub MUST rejeitar um documento só por um destes três motivos:

1. **Documento sem item.** É conferido pela validação de integração.
2. **Configuração do tenant que falta para montar a requisição.** Exemplo: os códigos da empresa na
   plataforma. É conferida pelo adapter de saída.
3. **Dado que o contrato do destino não consegue representar.** Exemplos: um campo que o contrato leva
   como número e não é número (CFOP, CST), ou dois tributos onde o contrato leva um só. É conferido pelo
   adapter de saída.

Qualquer outro caso MUST seguir para a plataforma. O motivo registrado MUST deixar claro qual dos três
casos aconteceu.

#### Scenario: Conteúdo fiscal estranho segue para a plataforma
- **WHEN** um item traz alíquota de ICMS 99 e CST `XX`
- **THEN** o documento não é rejeitado pelo hub e é enviado à plataforma com esses valores

#### Scenario: Motivo identifica a categoria
- **WHEN** um documento é rejeitado pelo hub porque o tenant não tem os códigos da empresa configurados
- **THEN** o motivo registrado diz que se trata de configuração do conector, e não de conteúdo da nota

### Requirement: Validação de integração só estrutural

A validação de integração da NF-e de mercadoria MUST rejeitar só o documento sem nenhum item, com o motivo
"A nota não possui itens.".

Ela MUST NOT rejeitar por conteúdo fiscal nem por formato de conteúdo. Formato, obrigatoriedade e
coerência desses campos são regra fiscal, e julgá-los é validar a nota, o que cabe à plataforma. Isso
inclui:

- chave de acesso vazia ou com número de dígitos diferente de 44;
- CFOP com número de dígitos diferente de 4;
- NCM ausente;
- grupo IBS/CBS ausente no item;
- CST ou `cClassTrib` ausentes no grupo IBS/CBS.

Documentos nessas condições MUST seguir para o envio.

#### Scenario: Item sem grupo da Reforma segue para o envio
- **WHEN** a esteira processa uma nota cujo item não tem o grupo IBS/CBS
- **THEN** a nota não é rejeitada na validação e é entregue ao envio

#### Scenario: Documento sem item continua rejeitado
- **WHEN** a esteira processa uma nota sem nenhum item
- **THEN** a nota é registrada como rejeitada, com o motivo "A nota não possui itens."
- **AND** nada é enviado à plataforma

#### Scenario: Chave de acesso vazia não bloqueia
- **WHEN** uma nota chega com a chave de acesso vazia
- **THEN** a validação não a rejeita e ela segue para o envio

#### Scenario: Chave de acesso fora do formato não bloqueia
- **WHEN** uma nota chega com uma chave de acesso de 43 dígitos
- **THEN** a validação não a rejeita e ela segue para o envio

#### Scenario: Grupo sem classificação não bloqueia
- **WHEN** um item tem o grupo IBS/CBS com `cClassTrib` vazio
- **THEN** a validação não o rejeita

#### Scenario: NCM ausente não bloqueia
- **WHEN** um item chega sem NCM
- **THEN** a validação não o rejeita

### Requirement: Grupo da Reforma ausente em qualquer caminho de entrada

Uma nota sem o grupo IBS/CBS MUST ser lida com o grupo ausente no item, e não zerado. Vale para todo
adapter de entrada, e a leitura MUST NOT falhar por isso. Um grupo presente, mas incompleto na estrutura do
documento de origem, continua sendo falha de leitura.

#### Scenario: NF-e em XML sem o grupo IBS/CBS
- **WHEN** chega uma NF-e em XML cujo item não tem o elemento `IBSCBS`
- **THEN** a nota é lida com o item sem o grupo IBS/CBS
- **AND** segue a esteira até o envio

#### Scenario: NF-e em XML com o grupo incompleto
- **WHEN** chega uma NF-e em XML com o elemento `IBSCBS` e sem o `gIBSCBS`
- **THEN** a leitura falha com erro que cita o elemento ausente
