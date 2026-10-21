<!-- generated-by: gsd-doc-writer -->
# Desenvolvimento

## Preparar o ambiente

Windows, Git, PowerShell e SDK .NET 10 compatível com `global.json` (atualmente 10.0.401, latestPatch). Visual Studio com suporte à versão do SDK é opcional; a CLI basta. Use o source ZIP ou, após criação do repositório, `git clone <URL-real-do-repositorio>` — nenhuma URL pública foi inventada.

```powershell
dotnet restore OrbLink.slnx
dotnet build OrbLink.slnx -c Release
dotnet test OrbLink.slnx -c Release
dotnet run --project src/AMS.Transfer.App/AMS.Transfer.App.csproj
```

O comando normal de execução usa seus dados reais e mutex da sessão Windows. Para testes de startup, use o diagnóstico isolado documentado em [configuração](CONFIGURATION.md); não use a pasta pessoal nesse modo.

## Estrutura e convenções

`App`: WPF e serviços UI; `Core`: modelos/nomes/geometria; `Storage`: SQLite/DPAPI; `Network`: HTTPS/UDP/pareamento/presença. `tests/AMS.Transfer.Tests` contém xUnit. Namespaces `AMS.Transfer.*` permanecem por compatibilidade. `OrbLink.slnx` é a entrada principal; solução antiga é alias, não outra implementação.

Use nullable, async/cancelamento, buffers limitados e chamadas SQL parametrizadas. Preserve comparação de tokens em tempo constante, pinning e DeviceId. Não introduza monitoramento global de clipboard, execução automática de recebidos ou reset de identidade para esconder falhas. Estilos/resources WPF ficam em Themes; use os serviços existentes de clipboard/ícones/drag.

Não há formatter/linter obrigatório configurado na CI. O gate é auditoria de source, restore, Release build e testes. Mudanças de comportamento precisam de regressão; hardware/DPI/apps externos devem ser marcados como pendentes quando não testados.

## Build e pacote

```powershell
.\scripts\build.ps1
.\scripts\package.ps1
.\scripts\audit-source.ps1
```

`build.ps1` verifica códigos de saída e executa etapas serialmente. `-SkipPublish` é usado pela CI. `publish.ps1` é um wrapper de compatibilidade. Saídas e relatórios TRX ficam em `outputs/`, ignorados pelo Git. Nunca execute publish em paralelo com build/test da mesma árvore: compartilham intermediários.

Versões centralizadas em `Directory.Build.props`; atualizar também manifest, Sobre e nomes de distribuição ao preparar outra versão. Deterministic/PathMap removem caminhos locais dos binários gerenciados; uma receita reproduzível não é promessa de ZIP bit-a-bit idêntico com SDK/runtime/SO diferentes. O package fixa ordem/timestamp das entradas ZIP.

## Segredos e colaboração

Não comite banco, PFX, chaves, logs, capturas pessoais ou recebidos. `.gitignore` e gate de pacote ajudam, mas não provam ausência de todos os segredos. Use testes sintéticos, veja [CONTRIBUTING](../CONTRIBUTING.md), [testes](TESTING.md) e [auditoria](SECURITY-AUDIT.md).
