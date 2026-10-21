<!-- generated-by: gsd-doc-writer -->
# Contribuir com OrbLink

## Começar

Windows, Git, PowerShell e SDK .NET 10 de `global.json`. Após o repositório real existir, clone sua URL; por enquanto use o source ZIP. Não há URL pública fictícia.

```powershell
git clone <URL-real-do-repositorio>
cd <pasta-do-repositorio>
dotnet restore OrbLink.slnx
dotnet build OrbLink.slnx -c Release
dotnet test OrbLink.slnx -c Release
```

Veja [DEVELOPMENT](docs/DEVELOPMENT.md), [TESTING](docs/TESTING.md) e [GETTING-STARTED](docs/GETTING-STARTED.md).

## Código e PRs

- Prefira branches descritivas, como `codex/correcao-nomes`, e commits pequenos com motivo claro.
- Preserve funções existentes e dados de pares; evite refatorações sem relação com a alteração.
- Siga C# nullable/async/cancelamento e padrões WPF existentes. Nenhum formatter obrigatório adicional foi imposto.
- Cubra a regressão com teste, rode build Release/testes e informe limitações de testes físicos.
- Use o template de PR: alteração, verificação, dados preservados e documentação atualizada.
- Não comite tokens, PFX, chaves, banco, logs privados, clipboard ou capturas/arquivos pessoais. Rode `scripts/audit-source.ps1` e revise o diff; assinatura conhecida não detecta todo segredo.

## Bugs e ideias

Use os templates de issue quando o repositório estiver disponível: versão/Windows, passos mínimos, resultado esperado/observado e contexto anonimizado de rede/DPI. Para vulnerabilidades sensíveis, siga [SECURITY.md](SECURITY.md), não uma issue pública.

O projeto usa MIT; o titular do copyright ainda precisa ser confirmado antes da publicação. Contribuições devem ter origem/licença compatível e não incluir dados de terceiros.
