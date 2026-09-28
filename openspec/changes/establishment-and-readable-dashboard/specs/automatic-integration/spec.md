## MODIFIED Requirements

### Requirement: O estado mostrado deriva do que o coletor fará

O endpoint de informações do tenant (`/info`) MUST responder dois valores derivados do perfil:

- **`inboundScans`:** verdadeiro quando o adapter de entrada do tenant varre, ou seja, tem um feed de mudanças
  registrado no host. É falso sem perfil.
- **`automaticIntegration`:** verdadeiro só quando o adapter de entrada varre e o `poll.enabled` das settings dele está
  ligado. Nos demais casos, a resposta MUST ser falsa:
  - o tenant não tem perfil;
  - o poll está desligado;
  - a seção `poll` está ausente;
  - o adapter de entrada não varre;
  - as settings de entrada não se deixam ler.

Settings ilegíveis MUST NOT fazer o endpoint falhar. A resposta MUST NOT trazer mais o campo `realtime`.

O selo da barra lateral mostra o estado, e não só o ligado:

- **Adapter que varre, integração ligada:** o selo MUST aparecer verde, com "Integração automática ligada".
- **Adapter que varre, integração desligada:** o selo MUST aparecer vermelho, com "Integração automática desligada".
  Isso inclui as settings ilegíveis.
- **Adapter que não varre:** o selo MUST NOT aparecer.
- **Enquanto a resposta não chegou, ou quando a leitura falhou:** o selo MUST NOT aparecer, nem verde nem vermelho.

#### Scenario: Ligado
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled = true`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = true`
- **AND** a barra lateral mostra, em verde, "Integração automática ligada"

#### Scenario: Desligado
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled = false`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = false`
- **AND** a barra lateral mostra, em vermelho, "Integração automática desligada"

#### Scenario: Seção poll ausente
- **WHEN** as settings de entrada do tenant-a não têm a seção `poll`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = false`
- **AND** a barra lateral mostra o selo vermelho

#### Scenario: Adapter que não varre, com poll ligado por SQL
- **WHEN** o tenant-b tem adapter de entrada `iScala` e as settings dele têm `poll.enabled = true`
- **THEN** o `/info` responde `inboundScans = false` e `automaticIntegration = false`
- **AND** a barra lateral não mostra selo nenhum

#### Scenario: Settings ilegíveis
- **WHEN** as settings de entrada do tenant-a não são um JSON válido
- **THEN** o `/info` responde normalmente, com `inboundScans = true` e `automaticIntegration = false`

#### Scenario: Enquanto carrega
- **WHEN** o dashboard abre e o `/info` ainda não respondeu
- **THEN** a barra lateral não mostra o selo, nem verde nem vermelho

#### Scenario: Salvar atualiza o selo
- **WHEN** o Admin desliga o interruptor e salva
- **THEN** o selo da barra lateral passa de verde a vermelho, sem recarregar a página
