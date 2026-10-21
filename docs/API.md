<!-- generated-by: gsd-doc-writer -->
# Protocolo interno HTTPS

Não é uma API pública de nuvem. `TransferServer` usa Kestrel em TCP 45832; clientes não seguem redirects, não usam proxy automático e fixam SHA-256 do certificado. Base URL construída do endpoint do peer, não há servidor fixo.

| Método | Caminho | Uso | Autenticação |
| --- | --- | --- | --- |
| GET | /api/health | Health legado; não prova presença | Público |
| GET | /api/peer/health | Marcador ORBLINK_PEER_V1, DeviceId e nome | X-Device-Id + X-Auth-Token |
| POST | /api/pair/request | Pedido com identidade/nome/porta/fingerprint/token de recepção | Aprovação humana; entrada validada |
| GET | /api/pair/status/{requestId} | pending/accepted/rejected/not_found | X-Pair-Token do solicitante |
| POST | /api/files | Body binário por streaming | X-Device-Id + X-Auth-Token |
| POST | /api/text | JSON text + label opcional | X-Device-Id + X-Auth-Token |

## Pareamento

`PairRequestMessage`: requesterId GUID não vazio/diferente do local, requesterName até 128 sem controles, requesterPort 1–65535, requesterCertificateFingerprint 64 hex, requesterReceiveToken base64 de 32 bytes. Corpo até 8 KiB. A resposta contém requestId e status pending; consulta usa o token já enviado como prova. Aceitação persiste credenciais bidirecionais e retorna identidade/token do receptor, que o cliente confere contra o DeviceId descoberto.

Pedidos expiram em dois minutos, com limpeza periódica a cada 30 s; operações também removem expirados. Limite global de 32 pedidos vivos, não um rate limit por IP/janela. 429 indica capacidade esgotada. Novo pareamento em receptor 0.3.0 exige cliente atualizado; transferências previamente pareadas 0.2.1 permanecem compatíveis.

## Arquivos e texto

Arquivos exigem Content-Length até 10 GiB e X-File-Name URI-encoded (até 4096 caracteres no header). X-File-SHA256, quando presente, precisa ser 64 hex e coincidir com o hash calculado pelo servidor. Clientes novos calculam SHA-256 por streaming antes de enviar. Cliente antigo sem header é aceito com tamanho exato + TLS. Body incompleto/hash divergente não é confirmado; parciais são removidos quando possível.

Texto JSON: `{"text":"conteúdo sintético","label":"nota.txt"}`. Até um milhão de caracteres, corpo até 8 MiB, label até 4096. Corpo e conteúdo não entram nos logs. Recebimento usa arquivo temporário e rename sem overwrite, como arquivos.

Até quatro recebimentos ativos; excesso retorna 429, sem retry automático de upload. 400: entrada inválida; 401: credencial/prova inválida; 404: pedido inexistente/expirado; 413: limite de body; 500: erro de armazenamento/recepção. Código de sucesso de arquivo retorna id/name/size; texto retorna id. Não há retomada ou chave de idempotência: perda da resposta depois do commit pode levar a uma cópia com sufixo se o usuário reenviar.

## Segurança e identidade

O probe exige certificado fixado, token recebido válido, marcador e DeviceId esperado. Anúncios UDP são candidatos não confiáveis; endpoint de peer existente só é atualizado depois do probe seguro, sem trocar tokens/certificado. Nunca use health público/porta TCP aberta como prova de identidade.

O primeiro pareamento não tem confirmação fora de banda. Consulte [auditoria](SECURITY-AUDIT.md); não exponha endpoints na internet nem inclua credenciais reais em exemplos/bugs.
