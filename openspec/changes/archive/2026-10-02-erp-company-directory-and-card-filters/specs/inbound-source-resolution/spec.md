## MODIFIED Requirements

### Requirement: Gatilhos de XML publicam com origem Xml

Todo gatilho que publica referência para um XML no Blob MUST preencher a origem `Xml`. São eles:

- a ingestão por drop;
- o endpoint de ingestão;
- a descoberta local por período. Ela só existe em Development, e lá atende a integração manual e a agendada do tenant
  cujo ERP não tem descoberta própria (`period-discovery`);
- a busca por chave do catálogo local, usada no reprocesso em Development quando a descoberta do ERP do tenant não acha
  a nota.

#### Scenario: Drop no Blob
- **WHEN** um XML é solto na zona de drop do tenant-a
- **THEN** a referência publicada tem origem `Xml`

#### Scenario: Endpoint de ingestão
- **WHEN** o endpoint de ingestão recebe tenant, chave e locator
- **THEN** a referência publicada tem origem `Xml`

#### Scenario: Reprocesso de nota do catálogo local
- **WHEN** o usuário pede, em Development, o reprocesso da nota 123 do tenant-a, cujo ERP é o `Dynamics365`
- **THEN** a referência reenfileirada tem origem `Xml` e gatilho `Manual`

#### Scenario: Integração manual pelo catálogo local
- **WHEN** o tenant-b, cujo ERP `iScala` não tem descoberta própria, dispara em Development uma integração manual que o
  catálogo local atende
- **THEN** cada referência publicada tem origem `Xml`
