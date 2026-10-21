# Checklist OrbLink 0.3.0 beta

Testes locais automatizados não substituem testes em dois PCs/hardware/aplicativos reais. Marque somente o que você executar e registre versão Windows, rede e DPI de forma anonimizada.

## Dois PCs e rede — pendentes

- [ ] PC 1 envia para PC 2.
- [ ] PC 2 envia para PC 1.
- [ ] Arquivo pequeno; comparar SHA-256 em origem/receptor.
- [ ] Arquivo grande (100/500 MiB e 1 GiB); conferir progresso, bytes/hash e memória.
- [ ] Arquivo repetido e envios simultâneos: `(1)`, `(2)`, sem sobrescrita.
- [ ] Espaços, acentos, ç, parênteses, Unicode/emoji e nome longo.
- [ ] Rede cai e volta: Wi-Fi/cabo/repetidor; peer volta Online sem reiniciar.
- [ ] Mudança de IP por discovery mantém um único DeviceId/peer.
- [ ] Suspender e retomar ambos os PCs; verificar reconexão.
- [ ] Interromper envio/fechar app: nenhum arquivo final incompleto; parciais limpos.
- [ ] Firewall privado adequado; não desativar segurança ou abrir serviço na internet.
- [ ] Novo pareamento requer aprovação; recusa/expiração não libera envio.

## Clipboard e drag — pendentes

- [ ] Imagem Ctrl+V no painel ativo; não funciona como captura global.
- [ ] Win+Shift+S, Print Screen, Paint, navegador: colar/enviar captura.
- [ ] Copiar imagem no receptor → Ctrl+V em Paint, navegador, Word e Outlook.
- [ ] Drag para Explorer.
- [ ] Drag para Desktop.
- [ ] Drag para Outlook/anexo e outro aplicativo compatível.
- [ ] Arquivo recebido removido manualmente: drag mostra erro claro, sem crash.

## Inicialização, monitores e atualização — pendentes

- [ ] App inicia com Windows, opção habilitar/desabilitar sem duplicar HKCU Run.
- [ ] Executar nova pasta atualiza startup já habilitado; reiniciar/login e conferir.
- [ ] Versão antiga aberta: nova informa processo/PID/versão; não encerra a antiga.
- [ ] Orb recupera posição inválida no principal/lado direito.
- [ ] Um monitor, dois monitores e resoluções distintas.
- [ ] DPI diferente (100/125/150/200%) e troca de monitor com painel aberto.
- [ ] Desconectar monitor da orb durante execução; ela permanece acessível.
- [ ] Movimento livre, snap, semioculta e persistência após reiniciar.
- [ ] Custom PNG transparente e ICO: preview, persistência, restaurar padrão.
- [ ] Ícone ausente/corrompido/grande e preferências inválidas: fallback visível.
- [ ] Reiniciar PCs; atualizar versão sem perder pareamento, identidade e histórico.

## Antes do GitHub — pendentes

- [ ] Confirmar titular do copyright MIT em LICENSE.
- [ ] Revisar os ZIPs e SHA-256; conferir ausência de dados pessoais/segredos.
- [ ] Criar repositório real e habilitar Private vulnerability reporting; ajustar SECURITY.md.
- [ ] Executar CI no GitHub e revisar resultado (workflow só foi preparado localmente).
- [ ] Compilar/testar instalador Inno em ambiente controlado, inclusive update/uninstall preservando dados.
- [ ] Definir assinatura Authenticode e timestamp; nunca burlar SmartScreen.
