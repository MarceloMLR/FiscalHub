// O erro de uma resposta HTTP não-ok, **com o status junto** (design D13).
//
// Antes disto, o `getJson` lançava `Error` comum e o status ficava só dentro da string da mensagem. Quem recebia o
// erro não tinha como distinguir "a sua sessão não vale" (401) de "não consegui perguntar" (5xx, rede, timeout) — e o
// portão de sessão, que tratava os dois como o primeiro, apagava o token de quem só tinha pegado o servidor fora.
// Ler o status de volta por `parseInt` da mensagem seria contrato por acidente; ele vai no erro.
//
// **Mora em arquivo próprio, e não dentro do `client.ts`**: o `client.ts` resolve a URL base no import
// (`const BASE = resolveApiBase()`, que lê o `window`), e os testes do dashboard rodam no ambiente **node** do vitest
// — importar o cliente num teste quebraria no carregamento. Aqui o teste usa o erro de verdade, e não um dublê
// parecido com ele.
//
// É um `Error`: quem já fazia `catch` e mostrava `e.message` não percebe diferença nenhuma.
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}
