<!-- generated-by: gsd-doc-writer -->
# OrbLink 0.3.0 — primeiros passos

Arraste. Solte. Receba.

## Requisitos e instalação

Windows x64 com suporte ao runtime .NET 10 incluído (validar seu Windows 10/11 atualizado). Rede privada confiável e dois PCs. O pacote portátil não exige SDK ou instalação administrativa.

Feche OrbLink/AMS Transfer antigo pela bandeja. Extraia `OrbLink-win-x64-v0.3.0.zip` em uma pasta estável, abra `OrbLink.exe` e confira 0.3.0 em Configurações → Sobre. Não execute duas versões simultaneamente; o aviso de instância bloqueadora mostra processo/PID/versão quando disponíveis.

Não há assinatura digital nesta beta. SmartScreen pode avisar; não desative SmartScreen/antivírus. Confira a origem e compare `Get-FileHash .\OrbLink-win-x64-v0.3.0.zip -Algorithm SHA256` com o manifesto recebido por um canal confiável. Hash não substitui assinatura/autenticidade da origem.

Se o Windows solicitar acesso à rede, autorize apenas o aplicativo na rede privada necessária. Não exponha TCP 45832 na internet. Não é necessário desativar firewall, VPN ou UDP para usar o probe direto.

## Parear e enviar

1. Atualize os dois PCs para 0.3.0. Abra Computadores e Atualizar.
2. Solicite pareamento ao PC correto. O receptor confere nome/endereço e aceita. Pedidos expiram após dois minutos.
3. Arraste um arquivo do Explorer para a orb; o envio usa o primeiro peer online ordenado por nome.
4. No receptor, clique na orb, encontre o item e arraste para Explorer/Desktop ou outro aplicativo que aceite arquivos Windows.
5. Para capturas, copie uma imagem e use Ctrl+V ou Colar imagem no painel ativo. No receptor, Copiar imagem publica PNG/bitmap no clipboard para colar em outro aplicativo.
6. Para texto, use Enviar texto; no receptor fica um arquivo `.txt`.

O clipboard só é lido por ação explícita. OrbLink não captura teclas globalmente. O suporte do aplicativo de destino ao formato é necessário; o checklist inclui Paint, navegador, Word e Outlook.

## Personalizar e iniciar com Windows

Configurações → Aparência aceita PNG/ICO até 4 MiB, com preview e cópia gerenciada. Salve, reinicie e confira persistência. Restaurar padrão remove a escolha; ícone inválido/ausente volta ao logo padrão.

Geral → Iniciar OrbLink com Windows é opcional. Se já habilitado, executar a nova cópia atualiza o caminho. Uma pasta temporária de Downloads pode desaparecer: mantenha o executável em local estável. A orb pode ser arrastada livremente, encaixa nas bordas e recupera posição inválida para o principal.

## Atualizar sem perder dados

Saia da versão antiga e extraia a nova em pasta separada. **Não apague `%LOCALAPPDATA%\AMS Transfer`**: nome interno preservado, assim como identidade, certificados, tokens, pares, recebidos e histórico. Faça backup dessa pasta com o aplicativo fechado antes de mudanças de versão. DPAPI exige o mesmo usuário/perfil Windows para recuperar os segredos; copiar para outro usuário não equivale a migrar credenciais.

Pareamentos válidos da 0.2.1 continuam funcionando com 0.3.0; não é necessário refazê-los. Novo pareamento com receptor 0.3.0 exige cliente 0.3.0. A 0.2.0 não possui o health autenticado exigido pela presença atual.

## Diagnóstico e limites

Computadores → Atualizar executa discovery e probe direto autenticado. Ao retornar da rede/suspensão, o refresh e heartbeat tentam recuperar os pares. Mudança de IP só é salva após validar certificado, token e DeviceId; se UDP estiver bloqueado e o endereço salvo também mudar, não existe busca cega por toda a sub-rede.

Falhas de certificado local são exibidas sem regeneração automática. Preserve a pasta, não exclua credenciais; consulte a auditoria no source (`docs/SECURITY-AUDIT.md`). Logs ficam em `%LOCALAPPDATA%\AMS Transfer\Logs` e contêm dados técnicos que precisam de anonimização antes de compartilhar.

Sem retomada de transferências, seletor de destino, auto-update ou instalador validado. Cancelamento de operação de rede limpa parciais; a UI não oferece fila/botão global de cancelamento para todos os caminhos. Fechar o aplicativo interrompe transferências.

Consulte `CHECKLIST-TESTES.md` e `LICENSE.txt` incluídos no ZIP. O titular legal do copyright deve ser confirmado antes da publicação pública.
