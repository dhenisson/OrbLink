<!-- generated-by: gsd-doc-writer -->
# OrbLink

Arraste. Solte. Receba.

OrbLink é um aplicativo open source para Windows para transferir arquivos, imagens e textos diretamente entre computadores. Uma orb flutuante aproxima o envio e o recebimento do lugar onde você já trabalha, sem nuvem obrigatória.

**0.3.0 · primeira beta pública · Windows x64 · MIT**

Esta é uma candidata preparada para revisão; o repositório e a release pública ainda não foram publicados. Compatibilidade em dois PCs reais e aplicativos externos continua no checklist de validação.

## Como funciona

PC A: **arquivo → orb**

PC B: **orb → arrastar arquivo para qualquer lugar**

O receptor mantém uma cópia local real. Abra o painel e arraste para Explorer, Desktop ou um aplicativo que aceite arquivos via drag-and-drop do Windows. Para imagens, copie uma captura e pressione **Ctrl+V no painel ativo**; no receptor, use **Copiar imagem** e cole no seu aplicativo.

## Recursos

- Transferência LAN direta por HTTPS, pareamento com aprovação e histórico local.
- Discovery e probe direto dos PCs pareados, sem depender só de broadcast.
- Orb com progresso, indicação online/offline, badge, movimento livre, snap e semiocultação.
- Drag-and-drop nativo OLE/CF_HDROP, Ctrl+V de PNG/bitmap e envio de texto.
- Personalização PNG/ICO, transparência, preview, cores, tamanho e restauração do padrão.
- Posição persistida, suporte multi-monitor/DPI e recuperação de monitor ausente.
- Streaming com buffer de 128 KiB, confirmação atômica e proteção contra sobrescrita.

![Painel do OrbLink com arquivos de demonstração](docs/images/painel.png)

Captura WPF com dados sintéticos; [política de screenshots](docs/images/README.md).

## Instalação

1. Feche a versão antiga em **Sair**, na bandeja do Windows.
2. Extraia `OrbLink-win-x64-v0.3.0.zip` em uma pasta estável e abra `OrbLink.exe`. O runtime está incluído; não exige SDK.
3. Atualize os dois PCs para 0.3.0. Se o Windows solicitar acesso à rede, autorize somente a rede privada em que você confia.
4. Confira **Configurações → Sobre → Versão 0.3.0**.

O build não tem assinatura Authenticode e pode gerar aviso SmartScreen. Não desative proteções: verifique origem e SHA-256; se houver dúvida, não execute. O ZIP é a distribuição principal, não há instalador validado nesta beta.

## Primeiros passos

Abra **Computadores**, encontre o outro PC e solicite pareamento. O receptor precisa aceitar. Já pareados na 0.2.1 não precisam parear novamente; mantenha `%LOCALAPPDATA%\AMS Transfer`, que contém os dados existentes. Novo pareamento com receptor 0.3.0 requer cliente 0.3.0.

Envie arrastando um arquivo para a orb, usando Ctrl+V de imagem no painel ativo ou Enviar texto. **Computadores → Atualizar** verifica os peers diretamente. Iniciar com Windows é opcional em **Configurações → Geral**; ao executar uma nova cópia, uma entrada já habilitada é atualizada para seu caminho.

Com vários pares online, o envio ainda escolhe o primeiro ordenado por nome. Não há observador global de clipboard, nuvem, retomada automática de transferências ou atualização automática.

Veja [guia completo](docs/GETTING-STARTED.md) e [checklist manual](CHECKLIST-TESTES.md).

## Segurança

Certificado TLS fixado, DeviceId esperado, tokens por direção e DPAPI CurrentUser. Recebidos nunca são executados automaticamente. Tamanho e SHA-256 (quando enviado) são verificados antes de confirmar o arquivo; nomes inválidos/reservados são tratados e colisões recebem `(1)`, `(2)`… sem sobrescrita.

O primeiro pareamento ainda depende da confiança na LAN e na aprovação humana, sem código de comparação fora de banda. Recebidos ficam em claro no perfil Windows; use apenas PCs confiáveis e não exponha o serviço na internet. Leia [SECURITY.md](SECURITY.md) e a [auditoria](docs/SECURITY-AUDIT.md).

## Desenvolvimento

Windows com SDK .NET 10 indicado em `global.json`, PowerShell e Git. A solução principal é `OrbLink.slnx`; o alias `AMS.Transfer.slnx` e namespaces internos foram mantidos para reduzir risco de migração.

```powershell
dotnet restore OrbLink.slnx
dotnet build OrbLink.slnx -c Release
dotnet test OrbLink.slnx -c Release
dotnet run --project src/AMS.Transfer.App/AMS.Transfer.App.csproj
```

Veja [arquitetura](docs/ARCHITECTURE.md), [desenvolvimento](docs/DEVELOPMENT.md), [configuração](docs/CONFIGURATION.md), [testes](docs/TESTING.md) e [protocolo](docs/API.md).

## Build

```powershell
.\scripts\build.ps1
.\scripts\package.ps1
```

Restore → Release build → testes → publish win-x64, em sequência. Os ZIPs binário/source e `SHA256SUMS-v0.3.0.txt` ficam em `outputs/`. Source obedece `.gitignore`, inclusive para arquivos rastreados; o gate de auditoria não substitui revisão humana. [Distribuição e instalador futuro](docs/DEPLOYMENT.md).

## Roadmap

- Validar o checklist nos dois PCs reais, repetidor, escalas DPI e aplicativos externos.
- Assinatura digital Authenticode e instalador por usuário validado.
- Confirmação por código no primeiro pareamento.
- Escolha explícita de destino quando vários peers estiverem online.
- Retomada/fila e política de retenção de recebidos, sem apagar itens durante arrasto.

## Como contribuir

Leia [CONTRIBUTING.md](CONTRIBUTING.md). Use dados sintéticos, testes de regressão e PRs pequenos. Templates de issues/PR e CI Windows estão preparados; nenhuma publicação automática de release está habilitada.

## Licença

[MIT](LICENSE), 2026. O titular do copyright precisa ser confirmado antes da publicação; não foi inventado um proprietário jurídico. Dependências preservam [licenças/avisos próprios](THIRD-PARTY-NOTICES.md). Consulte também [CHANGELOG.md](CHANGELOG.md).
