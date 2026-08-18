# Fase 0 — Preparar o Visual Studio e conectar ao ambiente (UDE)

No modelo novo (Unified Developer Experience), o Visual Studio roda **local na sua máquina** e se
**conecta ao ambiente de dev na nuvem**. A ordem é **conectar primeiro, depois criar model/projeto** —
você não cria um projeto solto e depois "liga" no ambiente. Seus dados de acesso entram **no login da
conexão**, não num projeto.

> Faça tudo apontando pro ambiente de **DEV**. ⚠️ CONFIRMAR = depende do seu ambiente.

---

## 1. Confirmar que o ambiente é um "developer environment" (UDE)

Dev no UDE exige um **environment de desenvolvedor** provisionado no **Power Platform Admin Center
(PPAC)** com o template de Finance/ERP. Um sandbox comum de F&O **não** serve para desenvolver.

- No **PPAC** (admin.powerplatform.microsoft.com), veja se você tem um environment do tipo
  **Developer** com finance and operations habilitado. (**⚠️ CONFIRMAR**; se não tiver, provisiona um —
  posso te guiar nisso também.)
- Anote a **URL do ambiente** (`https://{seu-ambiente}.operations.dynamics.com`).

---

## 2. Instalar o Visual Studio 2022 e os componentes

1. Instale o **Visual Studio 2022** (Community/Professional/Enterprise — qualquer um serve).
2. **Tools → Get Tools and Features → Individual Components** → procure e instale **Modeling SDK**
   (e **DGML editor**, se listar).
3. **Extensions → Manage Extensions** → procure **Power Platform Tools** → instale → reinicie o VS.

---

## 3. Conectar ao ambiente (aqui entram seus dados de acesso)

1. Com o **Power Platform Tools** instalado, faça **login** com a conta que tem acesso ao ambiente de
   dev (é o "dado de acesso" — um sign-in, não um projeto).
2. Selecione o seu **environment** (o developer environment do passo 1).
3. Na **primeira conexão**, o VS oferece **baixar a extensão de Finance & Operations + o metadado** do
   ambiente. Aceite e aguarde (pode demorar — ele traz o catálogo de tabelas/entidades padrão, incluindo
   a `FiscalDocument_BR`).

Quando terminar, aparecem o menu **Dynamics 365** e o **Application Explorer** no VS. É o sinal de que
está conectado e pronto.

---

## 4. Conferir que está tudo certo

- Menu **Dynamics 365** visível no topo do VS.
- **Dynamics 365 → Application Explorer** abre e você consegue buscar `FiscalDocument` (Data Model →
  Tables). Se acha a tabela, o metadado veio e você está conectado ao ambiente certo.

---

## Pronto?

Com o VS conectado e o Application Explorer abrindo, siga para a
[**Fase 1 — Criar e publicar a data entity**](01-criar-e-publicar-data-entity.md).

E já aproveite pra anotar, do Application Explorer, os três nomes que vou precisar: **tabela**
(ex.: `FiscalDocument_BR`), **campo-chave** e **campo de status**.

---

### Referências

- Unified developer experience (F&O) — Microsoft Learn: <https://learn.microsoft.com/en-us/power-platform/developer/unified-experience/finance-operations-dev-overview>
- Setup do UDE (visão prática): <https://medium.com/@rkmramisetty/goodbye-lcs-setting-up-the-unified-developer-experience-3d2f73114801>
