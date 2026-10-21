# OrbLink 0.2.1 — presença sem depender de UDP

Guia histórico da 0.2.1; nomes/endpoints abaixo são exemplos sintéticos. Para a beta atual, use [0.3.0 — primeiros passos](GETTING-STARTED.md). Scripts atuais publicam 0.3.0.

## Correção

Na 0.2.0, um peer salvo só era considerado online quando havia um anúncio UDP recente. A janela Computadores também marcava os pares sem anúncio como Offline. Isso recusava o envio mesmo com IP/porta corretos e TCP alcançável, como no cenário Ethernet → repetidor Wi-Fi informado.

Na 0.2.1, o OrbLink verifica diretamente `Address + Port` dos pares já autorizados. A descoberta continua encontrando PCs novos, mas não é mais a única fonte de presença dos PCs pareados.

- Heartbeat a cada 4 segundos; timeout de 2 segundos por probe.
- Assíncrono, sem bloquear a UI, com uma tentativa compartilhada em andamento por DeviceId e até quatro pares em paralelo.
- HTTPS com fingerprint SHA-256 já salvo, autenticação com token existente, marcador OrbLink e DeviceId esperado.
- Sucesso atualiza `LastSeenUtc` e libera o envio.
- Falhas transitórias: Offline após 3 falhas consecutivas ou 15 segundos sem sucesso. Uma única falha de conexão não derruba uma presença válida recente.
- Falha de autenticação/certificado/identidade/protocolo: invalidação imediata, até um novo sucesso seguro. Uma porta TCP aberta não basta.
- Antes de recusar por falta de pares online, arquivos, imagens e texto fazem uma tentativa direta.
- Computadores > Atualizar faz discovery e verifica todos os pares diretamente; a lista mostra o resultado ao terminar.
- Novo IP anunciado para o mesmo DeviceId só substitui o endpoint depois de validação segura. Não cria outro peer e não substitui credenciais.
- Proteção contra fechamento reentrante do painel: perda de foco durante o fechamento não chama Close novamente. A falha foi encontrada e reproduzida durante a validação da publicação.

O probe usa `GET /api/peer/health`. O health público antigo continua disponível, mas não é usado como prova de presença. Redirecionamentos HTTP e proxy automático são desabilitados nos clientes fixados para não encaminhar autenticação a outro destino.

## Atualizar os dois PCs

1. Em cada PC, use **Sair** no ícone do OrbLink/OrbLink ao lado do relógio.
2. Extraia `OrbLink-0.2.1-win-x64.zip` em uma pasta nova e estável. Abra `OrbLink.exe`.
3. Confirme **0.2.1** em Configurações > Sobre nos dois PCs.
4. Não exclua `%LOCALAPPDATA%\OrbLink`: identidade, certificado, tokens, histórico, recebidos, aparência e posição continuam nessa pasta.
5. Não refaça o pareamento se os dois PCs já estiverem pareados.
6. Se a inicialização automática estiver ativa, salve Configurações > Geral com essa opção marcada para apontar para o novo executável.

**Os dois PCs precisam de 0.2.1.** O endpoint autenticado é novo; 0.2.0 responderá 404 (`UnsupportedEndpoint`) e não será aceito como presença segura pelo cliente novo. Isso não apaga nem invalida o pareamento armazenado.

Runtime incluído, Windows x64, publicação portátil. Ainda sem instalador ou assinatura digital. Se o Windows pedir acesso à rede, autorize apenas a rede privada em que você confia. Os pacotes anteriores não foram substituídos.

## Teste no cenário informado

Este roteiro precisa ser executado nos dois PCs físicos; a validação automatizada usa duas instâncias HTTPS locais, sem discovery.

1. PC 1: OrbLink 0.2.1 aberto, endereço LAN privado alcançável, TCP `45832` acessível.
2. PC 2: OrbLink 0.2.1 aberto, peer previamente salvo como `PC receptor / <endereço-LAN-do-PC-1>:45832`.
3. No PC 2, abra **Computadores** e clique **Atualizar**. Mesmo sem anúncio UDP de PC 1, deve aparecer `PC receptor · ● Online · Pareado` depois do probe.
4. Arraste um arquivo do PC 2 para a orb e confira os bytes recebidos no PC 1.
5. Envie também uma imagem por Ctrl+V no painel ativo e uma mensagem por Enviar texto.
6. Confira que PC 1 → PC 2 continua funcionando.
7. Encerre OrbLink no PC 1: o PC 2 deve cair para Offline após as falhas/período de tolerância. Reabra: o heartbeat deve recuperar Online.

Não é necessário desativar UDP, firewall, antivírus ou VPN para usar o fallback. Se TCP 45832 deixar de ser alcançável, a correção não contorna bloqueios de rede. Não remova certificado/tokens para tentar resolver um erro de identidade.

## Interfaces LAN

A descoberta vincula o socket de saída às interfaces Ethernet/Wi-Fi ativas com IPv4 privado RFC1918. Prioriza interfaces com gateway e, entre equivalentes, Ethernet antes de Wi-Fi. Anuncia em todas as interfaces físicas elegíveis, pelo broadcast da sub-rede e pelo broadcast limitado.

Loopback, APIPA `169.254.0.0/16`, Radmin, Tailscale, VPNs/túneis e adaptadores reconhecidos como virtuais não são escolhidos automaticamente como LAN principal. Isso não adiciona uma restrição RFC1918 aos probes diretos: um endereço de túnel explicitamente salvo pode continuar sendo alcançado e autenticado. Autodescoberta/seleção automática de Tailscale fica para uma etapa futura.

## Logs

Arquivos em `%LOCALAPPDATA%\OrbLink\Logs`. Exemplos:

```text
Peer probe started: PC receptor / <endereço-LAN>:45832
TCP connection succeeded: PC receptor / <endereço-LAN>:45832
Certificate fingerprint validated: PC receptor / <endereço-LAN>:45832
OrbLink identity validated: PC receptor / <endereço-LAN>:45832
Peer status changed: PC receptor / Offline -> Online
```

Esses são exemplos, não logs de uma execução nos dois PCs físicos. A lista de interfaces elegíveis também é registrada quando muda. Os probes não registram cabeçalhos, tokens, chaves, corpos HTTP ou mensagens de exceção contendo dados de autenticação.

Falhas registradas:

| Código | Significado |
| --- | --- |
| Timeout | A verificação não terminou em 2 segundos. |
| ConnectionFailed | Conexão TCP/TLS indisponível ou interrompida. |
| AuthenticationFailed | O serviço recusou o DeviceId/token enviado. |
| CertificateMismatch | O certificado não corresponde ao fingerprint salvo. |
| DeviceIdMismatch | A identidade da resposta não é a do peer esperado. |
| InvalidService | Protocolo/resposta inválida, erro HTTP ou redirecionamento. |
| UnsupportedEndpoint | Endpoint inexistente; confira a versão no receptor. |

## Testes de regressão

A suíte cobre heartbeat, fallback no envio e Atualizar de forma independente, todos com discovery desativado e serviço HTTPS real. Confere os bytes enviados nos dois sentidos, `LastSeenUtc`, identidade e preservação dos tokens. O teste WPF dispara o botão Atualizar e exige uma única linha `● Online · Pareado`, mesmo sem dispositivos descobertos.

Também cobre certificado, DeviceId e token incorretos; health sem autenticação; porta TCP genérica; timeout; marcador de outro serviço; redirecionamento; endpoint legado; single-flight e cancelamento de um chamador; tolerância a falhas, expiração e recuperação; atualização segura de IP sem duplicar pares; proteção do endpoint já válido contra um anúncio incorreto; exclusão/prioridade de interfaces; logs sem tokens.

Os testes anteriores de arquivos grandes, PNG/bitmap, clipboard por ação explícita, CF_HDROP/OLE, histórico, persistência, aparência, ícones e geometria foram mantidos. Os testes normais não alteram o clipboard pessoal. O teste opt-in do clipboard real não é necessário para validar esta correção de rede.

O teste WPF também provoca perda de foco durante Closing e exige fechamento sem exceção. Os logs do diagnóstico isolado agora distinguem fechamento das janelas, parada do heartbeat e parada do servidor HTTPS, antes de declarar encerramento normal.

### Resultado em 07/10/2026

- Build Release: 0 erros, 0 avisos.
- Suíte Release: 47 aprovados, 0 falhas, 0 ignorados.
- Executável publicado: produto OrbLink, versão 0.2.1 / arquivo 0.2.1.0.
- Diagnóstico publicado com dados isolados e sem UDP: orb e painel iniciaram, encerramento normal, código de saída 0.
- Preview WPF `computadores-fallback.png`: peer salvo Online/Pareado após clicar Atualizar, sem anúncio UDP.
- Relatório TRX e log do diagnóstico em `outputs/validacao-0.2.1/` no workspace de entrega.

Ainda pendente: executar o roteiro acima nos dois PCs físicos, incluindo o repetidor, firewall e as transferências pelos aplicativos externos. Os testes locais não afirmam que esse ensaio físico já foi realizado.

## Arquivos desta correção

Não existe commit-base da 0.2.0 neste workspace; esta relação descreve a etapa, não um `git diff` automático.

Novos:

- `src/OrbLink.Network/PeerPresenceMonitor.cs`
- `src/OrbLink.Network/PeerProbeClient.cs`
- `src/OrbLink.Network/PinnedHttp.cs`
- `src/OrbLink.Network/LanInterfaceSelector.cs`
- `tests/OrbLink.Tests/PresenceIntegrationTests.cs`
- `tests/OrbLink.Tests/PeerPresenceTests.cs`
- `tests/OrbLink.Tests/ProbeProtocolTests.cs`
- `tests/OrbLink.Tests/LanInterfaceTests.cs`
- `docs/ORBLINK-0.2.1.md`

Alterados:

- Rede: `NetworkCoordinator.cs`, `NetworkOptions.cs`, `DiscoveryService.cs`, `TransferServer.cs`, `TransferClient.cs`.
- Armazenamento: `SqliteStore.cs`, com atualização condicional apenas de endereço/porta/LastSeenUtc; sem migração destrutiva.
- Aplicativo: `DevicesWindow.xaml` e `.xaml.cs`, `BubbleWindow.xaml.cs`, `TextSendWindow.xaml.cs`, `Services/ClipboardImages.cs` (onde reside ClipboardImageSender).
- Encerramento/diagnóstico: `MainWindow.xaml.cs`, `App.xaml.cs`; guard contra fechamento reentrante e logs de cada etapa do diagnóstico.
- Versão: `OrbLink.App.csproj`, `app.manifest`, `SettingsWindow.xaml`.
- Regressão WPF: `tests/OrbLink.Tests/WpfFeaturesTests.cs`.
- Publicação/documentação: `scripts/publish.ps1`, `README.md`, `docs/ARCHITECTURE.md`.

Os namespaces e diretório interno OrbLink permanecem intencionalmente inalterados. A interface e os recursos visuais da 0.2.0 foram preservados.
