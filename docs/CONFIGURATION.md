<!-- generated-by: gsd-doc-writer -->
# Configuração

## Configuração do usuário

Configurações → Geral/Aparência/Atalhos/Dispositivos/Sobre. Preferências são armazenadas no SQLite, não em `.env` ou JSON externo. Não edite credenciais manualmente.

Raiz mantida por compatibilidade: `%LOCALAPPDATA%\OrbLink`. `orblink.db` guarda identidade, pares, configurações e histórico; `device.pfx` é o certificado privado com senha protegida por DPAPI; `Received` guarda arquivos recebidos, `Outgoing` imagens temporárias, `Appearance` ícones gerenciados, `Logs` logs locais. Banco, PFX e recebidos nunca entram no source ZIP.

Defaults de aparência em `AppearanceSettings`: orb 68 px, ícone 30 px, destaque `#55A5FF`, fundo `#152541`, tamanho normalizado entre 52–100 px e ícone 16–56 px. Consulte o código para todas as preferências. Valores inválidos/JSON inválido usam defaults; ícone ausente/inválido usa logo padrão. Ícones PNG/ICO: até 4 MiB, dimensão até 8192 e até 32 milhões de pixels, redimensionados para exibição.

## Inicialização Windows

Opcional e desativada se não houver entrada existente. Usa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `OrbLink`, com caminho absoluto entre aspas. Ao habilitar/atualizar, remove a entrada legada `OrbLink`. Executar uma nova cópia atualiza uma entrada já habilitada; mover/apagar o executável sem executar a nova cópia ainda pode deixar caminho quebrado. A opção não usa tarefas agendadas ou privilégios administrativos.

## Rede e limites internos

`NetworkOptions` é configuração de código/teste, não uma tela para alterar portas:

| Opção | Default |
| --- | --- |
| DiscoveryPort / TransferPort | UDP 45831 / TCP HTTPS 45832 |
| MaxFileSizeBytes | 10 GiB |
| HeartbeatInterval / ProbeTimeout | 4 s / 2 s |
| OfflineFailureCount / PresenceTimeout | 3 falhas / 15 s |
| OnlineWindow de anúncios | 8 s |

O serviço escuta nas interfaces do computador. Firewall não é modificado automaticamente: permita o aplicativo apenas na rede privada necessária. Sem NAT traversal, relay, servidor obrigatório ou abertura automática de portas.

## Variáveis e modos de desenvolvimento

Nenhuma variável é obrigatória para usar o aplicativo; não há produção/staging remoto.

| Variável | Default | Uso |
| --- | --- | --- |
| ORBLINK_PREVIEW_DIR | ausente | Tests WPF gravam previews nessa pasta |
| ORBLINK_NATIVE_CLIPBOARD_TEST | ausente | `1` habilita teste opt-in do clipboard real; não usar no teste comum |

`OrbLink.exe --smoke-test <pasta-nova-vazia>` usa dados isolados, mutex exclusivo do diagnóstico, TCP dinâmico, sem UDP e sem alterar startup Windows. Abre orb/painel e encerra após aproximadamente cinco segundos. Pasta não vazia é recusada; nunca aponte para dados pessoais.

Certificado/PFX ou DPAPI inválidos provocam falha explícita, sem apagar a identidade. Veja [auditoria](SECURITY-AUDIT.md) para recuperação segura.
