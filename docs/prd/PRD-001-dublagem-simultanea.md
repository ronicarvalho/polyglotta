# PRD-001 — Dublagem simultânea em chamadas (v1 Windows)

**Status:** Ativo · **Última atualização:** 2026-09-18

## Problema

Reuniões entre falantes de PT-BR e EN exigem intérprete humano ou inglês forçado. Queremos que cada participante fale sua língua nativa e ouça a do outro, usando Meet/Teams sem plugin no navegador.

## Objetivo da v1

Aplicação Windows que, durante uma chamada:
1. Captura a voz do usuário (microfone), traduz em streaming e entrega áudio sintetizado no idioma alvo a um microfone virtual selecionado no Meet/Teams.
2. Captura o áudio do interlocutor (loopback do sistema), traduz e reproduz no fone do usuário.

## Não-objetivos (v1)

- Escrever driver de áudio próprio (usamos VB-Cable — ADR-0002).
- Mac/Linux (planejado; a arquitetura já isola plataforma — ADR-0004).
- Tradução instantânea (<500 ms) — inviável com reordenação sintática PT↔EN.
- Múltiplos interlocutores com vozes distintas (v2+).
- UI rica (v0 é console; v1 tem UI mínima).

## Persona e cenário principal

Profissional brasileiro em reunião com cliente/time nos EUA. Entra na chamada, abre o app, escolhe PT-BR→EN-US, seleciona "CABLE Output" como mic no Meet, conversa normalmente.

## Requisitos funcionais

| ID | Requisito |
|----|-----------|
| RF-01 | Listar dispositivos de captura e reprodução do sistema |
| RF-02 | Pipeline outbound: mic → tradução → mic virtual |
| RF-03 | Pipeline inbound: loopback → tradução → fone |
| RF-04 | Seleção de par de idiomas (mín. pt-BR↔en-US) e voz TTS |
| RF-05 | Mute do pipeline outbound (falar sem traduzir) |
| RF-06 | Exibir transcrição parcial/final e tradução em tempo real (log na v0) |
| RF-07 | Reconexão automática do serviço de tradução em caso de queda |

## Requisitos não funcionais

- **Latência:** ≤ 3 s do fim da frase à síntese (p95). Medir e logar.
- **Estabilidade:** 60 min de chamada sem vazamento de memória ou drift de buffer.
- **CPU:** processamento local leve (o trabalho pesado é na nuvem).
- **Segurança:** chaves só via variável de ambiente; nenhum áudio persistido por padrão.

## Métricas de sucesso

- Latência p95 ≤ 3 s em rede doméstica.
- Qualidade percebida aceitável em conversa de negócios (avaliação qualitativa).
- Setup < 5 min para usuário técnico (v0) e < 2 min (v1 com UI).

## Marcos

1. **M0 — Esqueleto** (este repo): interfaces, pipeline, CLI compilando.
2. **M1 — Outbound funcional**: falar PT no mic e sair EN no VB-Cable.
3. **M2 — Inbound funcional**: loopback traduzido no fone (atenção a eco/feedback).
4. **M3 — Sessão completa**: dois pipelines simultâneos, mute, métricas de latência.
5. **M4 — UI mínima** (WinUI/Avalonia — exigirá ADR).
