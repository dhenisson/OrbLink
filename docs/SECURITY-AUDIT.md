<!-- generated-by: gsd-doc-writer -->
# Auditoria de segurança e dados — OrbLink 0.3.0

Revisão local de código e testes em 2026-10-07. Não é pentest/certificação nem garantia de ausência de vulnerabilidades. Nenhum push ou publicação GitHub foi realizado.

## Escopo e correções

| Área | Evidência/resultado |
| --- | --- |
| TLS e presença | PinnedHttp/PeerProbeClient: sem redirects/proxy; certificado SHA-256, token, marcador e DeviceId obrigatórios. Testes recusam certificado/token/ID/serviço incorretos e porta TCP genérica. |
| Credenciais | Tokens de pareamento 256 bits por direção, comparação constante; SqliteStore/SecretProtector DPAPI CurrentUser. Nenhuma mudança da entropia/chaves existentes. |
| Migração/DPAPI inválido | Removida exclusão de pares da migração legada sem token de recepção. Linha e segredo protegido são preservados; credencial indecifrável não autentica e é sinalizada na UI, sem esconder peers saudáveis. Restauração de backup é necessária para recuperar a credencial. |
| Pareamento | Body até 8 KiB, validação estrutural, até 32 pedidos, TTL 2 min e X-Pair-Token na consulta. Cliente confere ResponderId esperado. Não há rate limit por IP nem código fora de banda. |
| Recebimento | 128 KiB de buffer; tamanho exato e hash opcional; temporário na mesma pasta/volume, flush do arquivo binário e rename sem overwrite; até quatro recebimentos ativos. |
| Texto/nomes | Texto até 1 milhão de caracteres/JSON 8 MiB; commit atômico. Reservados Windows, path traversal/absolutos, Unicode e nomes longos tratados; sufixo `(1)` seguro sob concorrência. |
| Parciais | Falha/cancelamento tenta remover somente o parcial desta operação. Startup limpa somente `orblink-<GUID>.part` com mais de um dia. Falha de permissão/disco pode exigir limpeza manual com app fechado. |
| Histórico/UI | Falha posterior ao commit é logada, sem reportar falsamente erro de upload; arquivo salvo pode não aparecer no histórico se o SQLite falhar. Perda de ACK e reenvio não são idempotentes. |
| Ícones/UI | Snapshot até 4 MiB, metadados/dimensões antes de pixels, fallback seguro. Posição inválida recuperada para principal; evento DPI/display e checagem periódica. |
| Certificado | PFX/password existentes inválidos falham explicitamente, sem troca silenciosa da identidade ou dos pares. Criação usa arquivo parcial/rename. |
| Logs | Tipo/HRESULT, sem mensagem/stack da exceção, bodies/headers omitidos, redação de campos de segredo; 8 MiB/segmento + 3 backups/dia, retenção 14 dias. Falha de log não derruba app. |
| Windows | asInvoker, startup HKCU opt-in/aspas/uma entrada; não executa recebidos, não desativa firewall/SmartScreen, não altera permissões globais. |

## Limites que permanecem

Primeiro pareamento ainda depende de anúncio LAN + aprovação humana; um atacante ativo pode se apresentar como outro computador. Código de confirmação fora de banda é futuro. Pedidos limitados ainda podem incomodar o usuário com diálogos; não há CAPTCHA/rate limit por IP. Só pareie PCs confiáveis em rede privada.

Kestrel escuta nas interfaces do computador; firewall deve restringir o serviço. Par autorizado pode usar o limite de arquivos/disco; não há quota por peer, antivírus integrado, sandbox de recebidos ou criptografia dos payloads em repouso. Mesmo usuário Windows/malware/admin pode acessar arquivos/PFX e credenciais DPAPI; pastas usam ACLs herdadas do perfil, não um ACL customizado.

Sem retomada/idempotência: interrupção cancela e limpa parcial; reenvio após ACK perdido pode criar outra cópia. Buffer limitado por código não equivale a benchmark de pico de memória de todo WPF/Kestrel. Monitores/driver/apps externos e quedas físicas de rede precisam da validação manual.

Probes recuperam pares no endereço salvo; IP novo requer anúncio/candidato seguro. Sem UDP e sem endereço alcançável, não há descoberta cega automática. Túnel explícito pode ser alcançável, mas não há autodescoberta Tailscale.

## Recuperar certificado/DPAPI

Não apague banco/PFX nem regenere credenciais para esconder erro. Saia, preserve a pasta completa e restaure backup do mesmo perfil Windows. Senha DPAPI e PFX precisam corresponder. Se não existir backup recuperável, uma nova identidade exige decisão consciente e novo pareamento em ambos os PCs; essa resetagem não é executada automaticamente nesta versão. Interrupção rara entre persistir senha e mover o PFX pode deixar criação incompleta e exigir recuperação explícita.

## Auditoria de segredos/source

O escopo inclui source, docs, scripts/templates, configurações e inventário de gerados/locais. Dados de testes são sintéticos; tokens de fixture não autenticam instalações reais. A suíte gera PFX/bancos em diretórios GUID de teste e os remove ao encerrar.

Identificadores pessoais/IPs do roteiro anterior foram substituídos por exemplos/documentação neutra. `.planning`, `work`, `outputs`, bin/obj, logs, bancos, recebidos, PFX/chaves e configurações locais de segredo são ignorados e excluídos do source. Artefatos antigos/histórico local foram preservados fora do pacote público, não apagados. Git não possui commit-base rastreado neste workspace, portanto não há histórico remoto/commit anterior para sanear.

`scripts/audit-source.ps1` bloqueia extensões privadas e assinaturas comuns de credenciais, sem imprimir matches. A revisão adicional verifica caminhos/nomes/IPs, certificados e ZIP final; busca por assinaturas não prova ausência de segredos genéricos/novos. O relatório de entrega registra os resultados executados e hashes. Revise LICENSE/titular e SECURITY/canal privado antes de publicar.

Referência de nomes: [Microsoft — Naming Files, Paths, and Namespaces](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file). As regras reservadas incluem COM/LPT com dígitos sobrescritos, além dos nomes comuns.
