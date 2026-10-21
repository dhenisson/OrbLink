# Changelog

## 0.3.0 — 2026-10-07 — primeira beta pública

- Recebimento atômico de arquivos e textos; nomes duplicados começam em `(1)`, inclusive sob concorrência.
- Nomes Windows reservados, path traversal, Unicode e nomes longos tratados sem perder caracteres válidos comuns.
- Conferência de tamanho e SHA-256 opcional no recebimento; clientes novos enviam o hash por streaming.
- Validação de 100 MiB, 500 MiB e 1 GiB, cancelamento e limpeza de arquivos parciais.
- Retry de discovery após falhas, refresh após mudança de rede/retorno da suspensão, mantendo probe direto autenticado.
- Recuperação de posição inválida/monitor ausente para o principal; fallback de ícone inválido.
- Inicialização única informa processo/versão; startup Windows evita entradas duplicadas e atualiza o caminho quando a nova versão é executada.
- Pareamento limitado, expira em dois minutos e exige prova para consultar credenciais; novo pareamento exige 0.3.0 nos dois PCs.
- Logs limitados/rotacionados, sem mensagens de exceção potencialmente sensíveis; certificado corrompido não é substituído silenciosamente.
- Migração legada preserva linhas de pares em vez de apagá-las; credencial DPAPI inválida fica offline e não impede listar os pares saudáveis.
- Preparação open source: MIT com titular pendente, documentação, templates, CI Windows, scripts e auditoria de source.
- ZIP portátil permanece a distribuição principal. Estrutura Inno Setup preparada, ainda sem instalador validado ou assinatura.

## 0.2.1 — 2026-10-07

- Presença de pares por HTTPS autenticado, independente de anúncios UDP.
- Heartbeat, fallback antes de recusar envio e Atualizar em Computadores.
- Atualização segura de endereço por DeviceId, sem duplicar pares ou alterar credenciais.
- Correção de fechamento reentrante do painel WPF durante perda de foco.

## 0.2.0 — 2026-10-07

- Redesign visual e nome OrbLink sobre a base OrbLink.
- Ctrl+V de imagens, Copiar imagem e melhorias da UI.
- Personalização PNG/ICO, preview, persistência e restauração do ícone.
- Ajustes da orb, multi-monitor e DPI.

## 0.1.0 — 2026-10-07

- Primeira versão funcional OrbLink.
- Transferência LAN de arquivos e texto, pareamento, HTTPS e histórico local.
- Orb flutuante e drag-and-drop externo com arquivos reais/CF_HDROP.
