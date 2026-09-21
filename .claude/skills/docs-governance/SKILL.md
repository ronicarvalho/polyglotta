---
name: docs-governance
description: Como criar e manter PRDs e ADRs neste repositório. Use sempre que for tomar uma decisão de arquitetura, adicionar/trocar uma dependência, mudar o escopo de uma feature, criar documentação de produto, ou quando o usuário mencionar PRD, ADR, requisito, decisão ou escopo — mesmo que ele não peça explicitamente um documento.
---

# Governança de docs (PRD/ADR)

## Quando um ADR é obrigatório

- Nova dependência externa ou troca de uma existente (biblioteca de áudio, provedor de tradução, framework de UI).
- Mudança em interface pública do Core.
- Qualquer decisão que seria cara de reverter (formato de áudio canônico, protocolo, estrutura de projetos).

Fluxo: proponha o ADR (status "Proposto") ANTES de escrever o código. Só implemente após o usuário aceitar. Use o comando /new-adr ou o template em docs/adr/ADR-template.md.

## Quando atualizar um PRD

- Requisito novo, removido ou alterado → editar a tabela de RFs no PRD, no mesmo commit do código.
- Marco concluído → marcar no PRD.
- Feature grande e nova (não coberta pelo PRD-001) → novo PRD via /new-prd.

## Regras

- Numeração sequencial: ADR-0005, ADR-0006…; PRD-002, PRD-003…
- ADR aceito nunca é editado no mérito: escreva um novo que o substitui e marque o antigo como "Substituído por ADR-XXXX".
- Docs em PT-BR; código e commits em inglês.
- Todo PR que muda comportamento cita o RF ou ADR correspondente na descrição.
