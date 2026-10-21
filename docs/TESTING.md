<!-- generated-by: gsd-doc-writer -->
# Testes

## Rodar

```powershell
dotnet test OrbLink.slnx -c Release --logger trx
# pipeline completo, inclusive publish
.\scripts\build.ps1
```

xUnit/VSTest no Windows. DPAPI/Schannel exigem um perfil Windows funcional; sandbox sem esse acesso pode falhar mesmo com código correto. TestStorage cria diretórios GUID próprios e remove somente esses fixtures. Aplicativo usa DPAPI real; alguns testes de protocolo usam protetor simulado para dados sintéticos.

## Cobertura

Nomes/path traversal/reservados/Unicode, colisões concorrentes, streaming HTTPS, tamanho/hash inválido, cancelamento e parciais, aprovação/autenticação/capacidade de pareamento, pinning, DeviceId, health, presença sem UDP, tolerância offline/recuperação e IP validado sem duplicação. Persistência de identidade/pares/aparência/posição, certificado corrompido sem overwrite, logs com redação/rotação/expiração, posição NaN/infinita/fora da tela/monitor ausente e comando de startup com aspas.

WPF: PNG/bitmap, Ctrl+V somente no painel ativo, ícone gerenciado PNG/ICO/corrompido/ausente/grande, CF_HDROP via IDataObject/OLE real, arquivo de drag ausente, renderização e regressão de fechamento reentrante. Isso não comprova funcionamento de Outlook/Word/Explorer nem desconexão física de monitores.

`BetaTransferTests` valida 100 MiB, 500 MiB e 1 GiB por streaming, compara SHA-256 entre origem/recebido e exige progresso e ausência de `.part`. O teste gera arquivos por blocos de 128 KiB, não arrays do tamanho inteiro. Precisa de aproximadamente 3 GiB livres para o maior caso e não deve correr paralelamente com outra suíte que use a mesma árvore de build.

## Clipboard real e previews

Testes comuns não alteram seu clipboard. `ORBLINK_NATIVE_CLIPBOARD_TEST=1` habilita teste nativo opt-in com backup/restauração restritos a texto; só use em uma sessão de teste controlada. Não foi habilitado na validação comum desta beta.

```powershell
$env:ORBLINK_PREVIEW_DIR = Join-Path (Get-Location) 'outputs/previews-v0.3.0'
dotnet test OrbLink.slnx -c Release
Remove-Item Env:ORBLINK_PREVIEW_DIR
```

Antes de usar previews em docs/images, confira visualmente e remova nomes/IPs/caminhos pessoais. Não copie todos os previews automaticamente para o source.

## Validação física

[CHECKLIST-TESTES.md](../CHECKLIST-TESTES.md) separa testes locais automatizados de testes manuais pendentes. CI está preparada para Windows em push/PR, mas ainda não foi executada no GitHub. Consulte o relatório de entrega para contagem e evidências da execução local.
