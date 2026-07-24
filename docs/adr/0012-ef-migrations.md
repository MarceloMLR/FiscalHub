# ADR-0012: Migrations de schema (EF Core) no lugar de EnsureCreated

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

O host criava o schema com `EnsureCreated`, que cria o banco do zero se não existe mas **nunca
evolui** um schema já existente. Toda vez que o modelo ganhou uma coluna (`Attempts`, depois
`CompanyCode`/`BranchCode`/`ReferenceDate`), foi preciso **dropar o banco** ou rodar `ALTER TABLE`
na mão — e um documento em processamento durante a transição chegou a ir pra dead-letter. Não é
sustentável conforme o modelo evolui.

## Decisão

- Adotar **EF Core Migrations**: cada mudança de modelo vira um arquivo de migração versionado,
  aplicável incrementalmente. `dotnet-ef` fica num **tool manifest local** (versionado no repo);
  `Microsoft.EntityFrameworkCore.Design` entra no Infrastructure.
- Uma **fábrica de design-time** (`IDesignTimeDbContextFactory`) deixa a CLI construir o contexto
  (SQL Server) sem subir o host.
- O host troca `EnsureCreated` por **`Database.MigrateAsync`** no startup — aplica as migrations
  pendentes automaticamente.
- Os **testes ficam no SQLite in-memory com `EnsureCreated`** próprio (schema fresco por run): não
  dependem de migrations, que são específicas do provider (SQL Server).

## Alternativas consideradas

- **Continuar com EnsureCreated + drop/ALTER manual** — frágil, propenso a erro e perde dados a cada
  mudança.
- **Migrations aplicadas por pipeline de deploy (não no startup)** — mais controlado para produção,
  mas overkill para o escopo de demo; `MigrateAsync` no startup é suficiente e simples aqui. Fica
  anotado que, em produção séria, migrations costumam rodar num passo de deploy separado.

## Consequências

- Mudou o modelo? `dotnet ef migrations add <Nome>` → commita o arquivo → o `Migrate` aplica no
  próximo start. **Fim do drop de banco.**
- **Transição única:** o banco de dev atual foi criado por `EnsureCreated` (sem a tabela
  `__EFMigrationsHistory`), então aplicar a `InitialCreate` nele conflita. Dropar o banco de dev uma
  última vez resolve; daí em diante é incremental.
- Migrations são por provider — o SQLite dos testes segue com `EnsureCreated`, sem impacto.
