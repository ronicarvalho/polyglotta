# ADR-0002 — VB-Cable como microfone virtual (não escrever driver)

**Status:** Aceito · **Data:** 2026-09-18

## Contexto

O Meet/Teams precisa "ouvir" o áudio traduzido como se viesse de um microfone, o que exige um dispositivo de entrada virtual no Windows. Escrever um driver próprio (AVStream/APO) significa meses de C++ kernel/user-mode, assinatura de driver e manutenção pesada.

## Decisão

A v1 dependerá do VB-Cable instalado pelo usuário: o app renderiza o áudio sintetizado em "CABLE Input" e o usuário seleciona "CABLE Output" como microfone no Meet/Teams.

## Alternativas consideradas

- **Driver próprio** — controle total e instalação transparente, mas custo/prazo incompatíveis com a v1.
- **Licenciar SDK VB-Audio / Virtual Audio Cable** — opção para instalação embutida; reavaliar na v2.

## Consequências

- Setup manual do usuário (instalar VB-Cable) — mitigar com verificação no startup e instruções guiadas.
- "CABLE Input/Output" fica como default, sempre selecionável na configuração.
- Equivalentes por plataforma: BlackHole (Mac), sink nativo do PipeWire/Pulse (Linux) — a abstração de ADR-0004 encapsula isso.
