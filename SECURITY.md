<!-- generated-by: gsd-doc-writer -->
# Segurança

## Reportar uma vulnerabilidade

Não publique uma vulnerabilidade sensível em issue pública, nem anexe banco, tokens, PFX, chaves, clipboard ou logs completos.

O repositório GitHub ainda não foi criado e não há e-mail de segurança definido. Antes da publicação, o mantenedor deve habilitar **Private vulnerability reporting** em GitHub Security Advisories e revisar esta política. Quando disponível, use Security → Advisories → Report a vulnerability no repositório real. Até esse canal existir, compartilhe uma descrição mínima diretamente com o responsável que lhe forneceu o aplicativo; não presumimos um contato público inexistente.

Inclua versão, impacto, passos com dados sintéticos e sugestão de correção. Não há prazo/SLA formal nesta beta.

## Versões suportadas

| Versão | Política |
| --- | --- |
| 0.3.0 beta | Foco atual de correções; sem garantia/SLA |
| 0.2.x / 0.1.x | Históricas; atualizar os dois PCs para 0.3.0 |

## Modelo e limites

HTTPS com certificado fixado, DeviceId esperado e tokens bidirecionais de 256 bits. Credenciais persistidas são protegidas por DPAPI CurrentUser. Arquivos não são executados automaticamente; recebidos e textos ficam em claro no perfil Windows. Confie apenas em computadores que você autorizou.

O primeiro pareamento ainda confia no anúncio LAN e na aprovação humana; não possui código comparável fora de banda. Um invasor ativo na LAN pode enganar essa etapa. O serviço escuta nas interfaces de rede: use firewall/rede privada, não exponha a porta na internet. DPAPI não protege contra malware já executando como seu usuário.

Consulte [auditoria técnica](docs/SECURITY-AUDIT.md) e [checklist](CHECKLIST-TESTES.md). Esta é uma revisão de código/testes locais, não um pentest independente nem certificação.
