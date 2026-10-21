<!-- generated-by: gsd-doc-writer -->
# Distribuição da beta

## Alvo e pipeline

Windows x64, portátil, self-contained/single-file com bibliotecas nativas autoextraídas pelo runtime. ZIP é o principal: `OrbLink-win-x64-v0.3.0.zip`; source: `OrbLink-source-v0.3.0.zip`. Sem servidor de nuvem, Docker, site, telemetria externa ou deploy remoto.

```powershell
.\scripts\build.ps1
.\scripts\package.ps1
```

Build restaura, compila Release, testa e publica serialmente. Package inclui somente executável/guia/licença/checklist e avisos/licenças de dependências no ZIP binário. Source é listado por Git e filtrado com `.gitignore`, também para arquivos rastreados; gate bloqueia artefatos privados e assinaturas comuns de credenciais. Ordem/timestamp das entradas ZIP fixos, nomes sem caminhos absolutos. `SHA256SUMS-v0.3.0.txt` contém hashes dos dois ZIPs e do executável. Assine primeiro e gere hashes/pacotes depois se adotar assinatura no futuro.

Git é necessário ao criar o source ZIP (inclusive em checkout ainda sem primeiro commit). Source extraído sem `.git` permite restore/build/test/publish, mas precisa inicializar Git localmente antes de executar package, para aplicar a política de ignore. Não é necessário adicionar remote ou enviar arquivos.

CI `.github/workflows/build.yml`: push/PR, Windows, auditoria/restore/build Release/testes; ações fixadas por SHA. Não publica release, não faz push, não exige segredo de deploy. Ainda precisa de uma execução real no GitHub após sua revisão.

## Instalador futuro

`scripts/installer/OrbLink.iss` + `build-installer.ps1` preparam Inno Setup por usuário, sem elevação, em `%LOCALAPPDATA%\Programs\OrbLink`, com atalhos e startup opcional desmarcado. Atualização mantém AppId/caminho; não toca `%LOCALAPPDATA%\AMS Transfer`. Uninstall só remove startup quando ainda aponta para a própria instalação; dados pessoais não são listados para remoção.

Não foi gerado EXE de instalador validado nesta entrega. Instale o compilador oficial Inno Setup apenas em ambiente de teste e use:

```powershell
.\scripts\installer\build-installer.ps1 -PublishPath .\outputs\OrbLink-win-x64-v0.3.0
```

Compilar não basta: verificar instalação limpa, update com app aberto, atalhos, startup duplicado/legado, desinstalação e dados/pareamento intactos. Esse scaffold não migra automaticamente uma entrada legada sem executar o app e não cria regra de firewall.

## Assinatura e SmartScreen

Builds atuais não são assinadas e podem gerar avisos. Não altere SmartScreen/antivírus nem documente bypass. Futuro: usar certificado de assinatura confiável e SignTool com SHA-256 + timestamp RFC3161, verificar assinatura, só então ZIP/hash. Credencial/certificado de assinatura fica fora do repo, armazenado em serviço/segredo apropriado; nenhum dado fictício de assinatura foi adicionado.

## Atualização, rollback e diagnóstico

Fechar app → backup da pasta de dados → extrair nova versão separada → executar e conferir Sobre/pares. Não sobrepor dados nem resetar certificado. Para rollback, feche a nova, use o ZIP anterior preservado e restaure backup somente se necessário, no mesmo perfil Windows. Não existe auto-update; downgrade deve ser testado antes de substituir dados.

Logs locais são o mecanismo de diagnóstico, sem monitor externo. Revise/anonymize antes de compartilhar. O modo smoke-test cria perfil isolado com pasta nova/vazia e não altera startup real. Consulte [GETTING-STARTED](GETTING-STARTED.md) e [CHECKLIST](../CHECKLIST-TESTES.md).

Referências oficiais: [Inno — PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet).
