# OrbLink 0.2.0 — guia e alterações

Guia histórico da 0.2.0. Para instalar/testar a versão atual, consulte [0.3.0 — primeiros passos](GETTING-STARTED.md). Os scripts da árvore atual publicam 0.3.0, não recriam automaticamente o pacote histórico.

## Atualizar sem perder a V1

O OrbLink evolui a base AMS Transfer. Namespaces, banco, identidade, certificado, tokens, arquivos recebidos, histórico e posição foram preservados. Nome visual, executável e publicação agora usam **OrbLink**.

1. Encerre o **AMS Transfer** pelo menu **Sair** do ícone ao lado do relógio.
2. Extraia o novo ZIP em uma pasta própria e abra `OrbLink.exe`.
3. Faça isso nos dois PCs, na mesma rede local. **Não exclua `%LOCALAPPDATA%\AMS Transfer`.**
4. Espere alguns segundos para a descoberta. Abra **Computadores** e pareie, com confirmação no receptor, se não houver par autorizado.

A proteção de instância única é compartilhada com a V1. O aplicativo antigo não foi encerrado durante o desenvolvimento; o teste da publicação usou dados temporários e descoberta desativada. Se o Windows pedir acesso à rede, autorize somente na rede privada em que você confia.

Se a inicialização automática estava configurada na V1, abra **Configurações > Geral**, mantenha a opção marcada e salve. Isso atualiza a entrada para o executável OrbLink e remove somente a entrada legada de inicialização. Guarde a publicação em uma pasta estável antes de ativar essa opção.

## O que mudou

- Tema escuro, transparência discreta, cantos arredondados, sombras, glow azul e tipografia Segoe UI Variable.
- Orb com ícone próprio, hover sutil, online/offline, badge e progresso de envio/recebimento. Ela se revela ao receber e não se semioculta enquanto há itens novos ou recebimento ativo.
- Painel junto à orb, limitado à área útil do monitor atual. Fecha ao clicar fora, mas permanece vivo durante arrasto OLE, envio ou diálogo filho.
- Cards com miniatura/ícone, nome, origem, horário/data e tamanho; ações Copiar, Abrir e Arrastar por item.
- Ctrl+V somente no painel ativo, sem hotkey global ou observador do clipboard. Botão Colar imagem para captura explícita.
- Leitura de PNG (`PNG`/`image/png`) e bitmap nativo; conversão para PNG temporário e envio pela mesma transferência HTTPS da V1. Temporário removido após sucesso ou falha.
- Copiar imagem publica bitmap nativo e PNG. Arquivos recebidos continuam disponíveis como arquivos reais para CF_HDROP.
- PNG transparente/ICO personalizado de até 4 MB, preview, cópia gerenciada, persistência, restauração e fallback.
- Ajustes de tamanho da orb/ícone, destaque/fundo, borda, glow, opacidade, semiocultação, animações e som.
- Configurações: Geral, Aparência, Atalhos, Dispositivos e Sobre. Computadores, Histórico e Enviar texto também receberam tema e textos atualizados.
- Progresso real de recebimento. Limite do servidor segue o limite configurado de arquivos, corrigindo a restrição implícita de aproximadamente 30 MB do Kestrel.
- Publicação `OrbLink.exe`, Windows x64, portátil e autossuficiente. Sem instalador ou assinatura digital nesta etapa.

## Como testar

### 1. Personalizar o ícone

1. Clique na orb e abra **Configurações > Aparência**.
2. Use **Escolher PNG / ICO** com um PNG transparente ou ICO de até 4 MB.
3. Confira o preview, ajuste tamanho/cor/glow e clique **Salvar alterações**.
4. Saia pelo menu **Sair** e reabra `OrbLink.exe`: ícone e ajustes devem permanecer.
5. Mova/apague o PNG original: a cópia gerenciada deve continuar funcionando.
6. Teste **Restaurar padrão** e salve. Imagens inválidas ou cópias gerenciadas ausentes usam o ícone padrão.

### 2. Colar imagem

1. Abra OrbLink nos dois PCs e confirme o receptor online em **Computadores**.
2. Faça uma captura com **Win+Shift+S**, Print Screen ou copie uma imagem de Paint/navegador/Office.
3. Clique na orb para abrir o painel e dê **Ctrl+V**. Confira miniatura, progresso e **Imagem enviada**.
4. Teste também **Colar imagem**. Copie somente texto e tente: deve aparecer **Nenhuma imagem encontrada na área de transferência**.
5. Dê Ctrl+V em outro programa com o painel fechado/inativo: OrbLink não deve enviar nada.

O suporte é aos formatos comuns de imagem do Windows, não a uma integração especial para cada aplicativo. PNG/bitmap precisam estar realmente no clipboard. Sem par online, o painel informa isso e não envia.

### 3. Enviar e receber

1. Arraste um arquivo do Explorer sobre a orb; ela expande o alvo e mostra progresso.
2. No receptor, confira o indicador de recebimento e o badge. Abra o painel e confira o card.
3. Teste uma imagem por Ctrl+V e um arquivo maior que 32 MB; confira origem, tamanho e histórico nos dois PCs.
4. Use **Copiar imagem** e cole em Paint/Word/Outlook ou outro destino que aceite imagens.

Com vários pares online, mantém-se o envio ao primeiro par online ordenado por nome. Ainda não há seletor de destino.

### 4. Arrastar para fora

1. Clique e segure **Arrastar** no card, mova para Desktop/Explorer e solte em uma área que aceite arquivos.
2. Também é possível iniciar pelo corpo do card, fora dos outros botões.
3. Teste em uma mensagem do Outlook, em uma área que aceite anexos.
4. Confira o arquivo no destino. O painel só pode se fechar depois do fim do arrasto.

Aceitar drop/paste depende do programa e da área de destino. Diferenças de privilégios entre processos também podem bloquear drag & drop pelo Windows.

### 5. Orb e monitores

Arraste para bordas, cantos e outro monitor; reinicie e confira a posição. Teste semiocultação lateral e revelação por hover. Abra o painel junto às quatro bordas e confirme que fica dentro da área útil. Em múltiplos monitores, inclua DPI diferentes.

## Validação realizada em 07/10/2026

- Compilação Release: **0 erros e 0 avisos**.
- Suíte Release: **17 testes aprovados**, sem falhas ou ignorados.
- Testes anteriores mantidos; arquivo de regressão ampliado para **32 MiB + 17 bytes**.
- Pareamento/envio HTTPS real entre duas instâncias de teste no mesmo PC, certificado fixado e bytes recebidos verificados.
- PNG enviado com dimensões/transparência conferidas, histórico nos dois lados e limpeza de temporários também após erro.
- Clipboard real do Windows: PNG + bitmap e bitmap puro lidos; texto anterior restaurado. Sem monitoramento global.
- Regra Ctrl+V ativa/inativa e diálogo filho; texto não tratado como imagem; PNG inválido com fallback para bitmap.
- Importação PNG/ICO, exclusão do original, reabertura do serviço, persistência, fallback e restauração. Identidade, par e posição da V1 preservados no teste de armazenamento.
- CF_HDROP/`TYMED_HGLOBAL` validado pelo IDataObject OLE real; o Windows leu o caminho absoluto existente, incluindo acentos, por `DragQueryFileW`.
- Geometria do painel com bordas, coordenadas negativas e tela pequena; testes de snap existentes mantidos.
- Painel vazio, cards, orb e configurações renderizados pelo WPF e revisados visualmente; contraste e controles corrigidos. Previews usam arquivos de teste, não arquivos pessoais.
- Executável publicado em diagnóstico isolado: orb/painel abertos, encerramento normal, saída **0**.

**Ainda requer validação manual:** descoberta/firewall entre dois PCs físicos; tecla Ctrl+V digitada com conteúdo de cada aplicativo citado; drop/paste em Outlook/WhatsApp/Office; monitores com DPI diferentes. Os testes de formatos e HTTPS local não substituem integração com aplicativos externos.

Não foram instaladas ferramentas grandes ou novos pacotes de terceiros nesta etapa. Foram usadas as dependências .NET da base. O pacote inclui seus runtimes.

## Arquivos alterados e novos

Caminhos relativos à raiz do projeto. Ainda não existe commit-base da V1; esta é a relação desta etapa, não uma atribuição automática de `git diff`.

### Alterados — aplicativo

- `src/AMS.Transfer.App/AMS.Transfer.App.csproj`: nome, versão, ícone, usings e DPI.
- `src/AMS.Transfer.App/App.xaml`, `App.xaml.cs`: tema, serviços, tray, saída e diagnóstico isolado.
- `src/AMS.Transfer.App/AssemblyInfo.cs`: acesso interno para testes.
- `src/AMS.Transfer.App/BubbleWindow.xaml`, `BubbleWindow.xaml.cs`: orb, estados, aparência, arrasto/posição, badge e animações.
- `src/AMS.Transfer.App/MainWindow.xaml`, `MainWindow.xaml.cs`: painel, cards, Ctrl+V, feedback, geometria e vida útil durante OLE.
- `src/AMS.Transfer.App/DevicesWindow.xaml`, `DevicesWindow.xaml.cs`: tema, nome e atualização da lista.
- `src/AMS.Transfer.App/HistoryWindow.xaml`, `HistoryWindow.xaml.cs`: tema, nome, datas/status em português.
- `src/AMS.Transfer.App/TextSendWindow.xaml`: tema/título; lógica de texto preservada.
- `src/AMS.Transfer.App/StartupManager.cs`: entrada OrbLink e reconhecimento da legada.
- `src/AMS.Transfer.App/app.manifest`: nome, versão, DPI PerMonitorV2.

### Novos — aplicativo

- `src/AMS.Transfer.App/GlobalUsings.cs`.
- `src/AMS.Transfer.App/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`.
- `src/AMS.Transfer.App/Themes/Colors.xaml`, `Controls.xaml`, `Icons.xaml`.
- `src/AMS.Transfer.App/Services/AppearanceService.cs`, `IconStore.cs`, `ClipboardImages.cs`, `NativeFileDrag.cs`, `PanelShortcuts.cs`, `ScreenGeometry.cs`, `TrayIconFactory.cs`.
- `src/AMS.Transfer.App/Assets/OrbLink.ico`.

### Lógica, persistência e rede

- Novos: `src/AMS.Transfer.Core/AppearanceSettings.cs`, `PanelPlacement.cs`, `TransferActivity.cs`.
- Novo: `src/AMS.Transfer.Storage/AppearanceStore.cs`.
- Alterado: `src/AMS.Transfer.Storage/AppPaths.cs` — aparência e temporários, sem mudar a raiz da V1.
- Alterados: `src/AMS.Transfer.Network/TransferServer.cs`, `NetworkCoordinator.cs`, `NetworkOptions.cs` — progresso, limite de upload e descoberta opcional no diagnóstico.

### Testes, publicação e documentação

- Alterados: `tests/AMS.Transfer.Tests/AMS.Transfer.Tests.csproj`, `TransferIntegrationTests.cs`.
- Novos: `tests/AMS.Transfer.Tests/AppearanceTests.cs`, `WpfFeaturesTests.cs`, `GlobalUsings.cs`.
- Alterados: `scripts/publish.ps1`, `README.md`, `docs/ARCHITECTURE.md`, `.gitignore` (intermediários em `work/`).
- Novo: `docs/ORBLINK.md` — cópia em `outputs/GUIA-OrbLink.md` e publicação `LEIA-ME.md`.
- Entregas: `outputs/OrbLink-win-x64/`, `outputs/OrbLink-win-x64.zip`, `outputs/OrbLink-source.zip`, `outputs/previews/`.

## Limites e continuação

Sem tema claro implementado, Acrylic/blur nativo, seletor de destino, fila/cancelamento visual completo, retenção automática, assinatura ou instalador. ResourceDictionaries e serviços separados preparam a evolução. Nenhum recebido é aberto automaticamente.
