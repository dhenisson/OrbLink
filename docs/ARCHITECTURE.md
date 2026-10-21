<!-- generated-by: gsd-doc-writer -->
# Arquitetura do OrbLink

## Decisão central: Drag & Drop externo

O arquivo recebido é materializado em `%LOCALAPPDATA%\OrbLink\Received`. A bandeja inicia um arrasto OLE por `DragDrop.DoDragDrop` com um `System.Windows.DataObject` no formato `DataFormats.FileDrop`. No Windows, esse formato é publicado como `CF_HDROP`, portanto o destino recebe caminhos de arquivos reais. Isso sustenta o fluxo principal `arrastar → soltar → receber → arrastar` e evita a necessidade de “Salvar como”.

Essa decisão exige manter o arquivo local enquanto ele aparece no histórico. Uma política futura de retenção poderá remover itens antigos, mas nunca durante um arrasto ativo.

## Componentes

```text
Explorer ──CF_HDROP──> BubbleWindow
                         │
                         ▼
                 NetworkCoordinator
                  │              │
             UDP discovery   HTTPS streaming
                  │              │
                  └──── LAN ─────┘
                                 │
                                 ▼
                         Received directory
                                 │
                         SQLite history
                                 │
                                 ▼
MainWindow ──CF_HDROP──> Explorer / Outlook / Desktop
```

## Segurança

- Cada instalação gera uma identidade GUID e um certificado RSA autoassinado.
- A descoberta divulga a impressão SHA-256 do certificado.
- O cliente fixa essa impressão digital durante TLS; certificados diferentes são recusados.
- Clientes autenticados não seguem redirecionamentos e não usam o proxy do sistema para alcançar pares LAN/túnel.
- O usuário precisa aceitar o primeiro pareamento no computador receptor.
- O pareamento cria tokens aleatórios de 256 bits separados por direção.
- Tokens e senha do PFX são protegidos com DPAPI `CurrentUser` antes de entrar no SQLite.
- Comparação de tokens usa tempo constante.
- Cabeçalhos de nome são reduzidos a `Path.GetFileName`, caracteres inválidos são substituídos e duplicados ganham sufixo.
- Arquivos são gravados como `.part`, têm tamanho e SHA-256 opcional verificados e são movidos sem sobrescrita; a própria operação de rename resolve concorrência. Textos também são confirmados atomicamente.
- Pareamento: corpo até 8 KiB, 32 pedidos vivos, TTL de dois minutos, consulta autenticada com `X-Pair-Token`. Certificado local inválido provoca erro claro e preserva a identidade em vez de regenerá-la silenciosamente.
- Arquivos nunca são executados automaticamente.

O pareamento inicial depende da confiança do usuário na solicitação exibida e da impressão digital anunciada na LAN. Uma versão futura pode adicionar confirmação por código curto para defesa adicional contra um invasor ativo na rede local.

## Persistência

SQLite usa WAL e conexões curtas. As tabelas guardam configurações, pares e histórico. A posição da bolinha inclui coordenadas, monitor e borda de snap. O banco possui migração inicial para a separação de tokens bidirecionais.

O OrbLink mantém a raiz `%LOCALAPPDATA%\OrbLink`. `AppearanceStore` usa a nova chave `OrbLink.Appearance.v1`, sem alterar identidade/pares. Ícones validados são copiados por hash para `Appearance`; a configuração guarda somente o nome gerenciado. PNGs de envio ficam em `Outgoing` e são removidos ao terminar.

## UI e clipboard

`Themes/Colors.xaml`, `Controls.xaml` e `Icons.xaml` separam brushes, templates e logo vetorial. `AppearanceService` aplica recursos dinâmicos e comunica mudanças à orb. `IconStore` valida PNG/ICO, limita tamanho e oferece fallback. `PanelPlacement` calcula limites lógicos e `ScreenGeometry` adapta monitor/DPI e movimento físico.

`PanelShortcuts` limita Ctrl+V ao painel ativo, sem filho modal. `ClipboardImages` lê PNG/bitmap somente por ação explícita e publica ambos para copiar imagens. `ClipboardImageSender` codifica PNG e usa o mesmo cliente HTTPS da V1. Não existe observador global do clipboard. O painel fica vivo durante o loop OLE de um arrasto externo.

O limite de arquivo do Kestrel segue `MaxFileSizeBytes`; texto usa teto de 8 MiB no JSON/um milhão de caracteres. Até quatro recebimentos simultâneos; excesso retorna 429. `TransferActivity` comunica progresso real do recebimento. SHA-256 é calculado por streaming antes do envio (segunda leitura do arquivo), sem carregar o payload inteiro.

Referências oficiais: [Data objects WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/data-and-data-objects) e [Drag and drop WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview).

O teste nativo também materializa CF_HDROP via IDataObject, consulta os caminhos com [DragQueryFileW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-dragqueryfilew) e libera o meio com [ReleaseStgMedium](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-releasestgmedium).

## Rede

- anúncios UDP a cada 2 segundos;
- workers de discovery tentam reabrir sockets após erro e nunca impedem o startup visível por falha de UDP;
- mudança de endereço/retorno da suspensão dispara refresh; heartbeat periódico permanece como fallback;
- descoberta de novos dispositivos: anúncio recente por até 8 segundos;
- pares autorizados: presença independente do UDP, por `GET /api/peer/health` no último endereço/porta salvo;
- o servidor autentica `X-Device-Id` e `X-Auth-Token` com o ReceiveToken existente, sem gerar outras credenciais;
- o cliente fixa o certificado salvo e exige o marcador `ORBLINK_PEER_V1` e o DeviceId esperado na resposta;
- heartbeat assíncrono a cada 4 segundos, timeout de 2 segundos, no máximo um probe em andamento por DeviceId e quatro pares em paralelo;
- um sucesso atualiza `LastSeenUtc`; três falhas consecutivas de conexão ou 15 segundos sem sucesso deixam o par offline;
- falhas de certificado, autenticação, identidade ou protocolo invalidam a presença imediatamente, até um novo sucesso seguro;
- um anúncio do mesmo DeviceId em outro IP apenas propõe um endpoint: a atualização no SQLite ocorre depois do probe seguro e não altera certificado/tokens;
- Computadores > Atualizar anuncia e verifica todos os pares diretamente; o caminho de envio sem par online força uma verificação antes de recusar;
- anúncios UDP partem de sockets vinculados a cada Ethernet/Wi-Fi ativa com IPv4 RFC1918, usando broadcast da sub-rede e broadcast limitado;
- loopback, APIPA, Radmin, Tailscale, VPNs/túneis e interfaces reconhecidas como virtuais são excluídos da escolha automática LAN;
- probes diretos não filtram o endereço salvo por RFC1918: um transporte de túnel explícito continua possível, sem autodiscovery de Tailscale nesta versão;
- Kestrel HTTPS na porta 45832;
- buffer de transferência de 128 KiB;
- limite padrão de 10 GiB;
- cancelamento propagado por `CancellationToken`;
- arquivo inteiro nunca é carregado na RAM.

`PeerPresenceMonitor` separa presença de descoberta. A UI só consulta o cache autenticado; heartbeat, Atualizar, envio e anúncio de novo IP compartilham a mesma tentativa por peer. Cancelar um chamador da UI não cancela a verificação compartilhada. O timestamp antigo do banco não prova presença após reiniciar.

O endpoint público legado `/api/health` permanece compatível, mas não serve para presença. A versão 0.2.0 não possui `/api/peer/health`. Recomenda-se 0.3.0 nos dois PCs; pareamentos 0.2.1 existentes continuam válidos, mas novo pareamento com receptor 0.3.0 exige prova de consulta que clientes antigos não enviam.

`OrbPlacementRecovery` valida coordenadas finitas, interseção com monitores e monitor salvo. Posição inválida volta ao lado direito/centro vertical do principal; checagem periódica e eventos de display/DPI evitam perda permanente da orb. Startup usa mutex por usuário compatível com OrbLink e uma entrada HKCU Run, sem privilégios elevados.

Logs locais são best-effort: 8 MiB por segmento, três backups por dia, expiração após 14 dias. Mensagens de exceção não são gravadas; apenas tipo/HRESULT. Falha de log não derruba a transferência. Falha de UI/histórico depois do commit não converte um arquivo já salvo em falha do upload.

Referências oficiais: [validação de certificado no HttpClient](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.servercertificatecustomvalidationcallback?view=net-10.0), [UdpClient vinculado a IPEndPoint](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.-ctor?view=net-10.0) e [PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer?view=net-10.0).

## Próximas decisões

1. Seletor de destino quando houver vários pares online.
2. Política configurável de retenção/limpeza dos arquivos recebidos.
3. Instalador MSIX/WiX e regra de firewall criada pelo instalador.
4. Confirmação visual de código no pareamento.
5. Retomada de transferências interrompidas e fila com cancelamento visível.
