# ADR-0003 — Azure Speech Translation como provedor v1

**Status:** Aceito · **Data:** 2026-09-18

## Contexto

Orquestrar STT + MT + TTS separados soma latências e complexidade. Precisamos de tradução de fala em streaming com síntese integrada e reconhecimento parcial.

## Decisão

Usaremos o Azure Speech SDK (`TranslationRecognizer` com síntese de voz habilitada) como provedor da v1, atrás da interface `ISpeechTranslator` do Core.

## Alternativas consideradas

- **OpenAI Realtime API (voz-a-voz)** — forte candidata; manter como segunda implementação de `ISpeechTranslator` para benchmark de latência/custo.
- **Pipeline separado (Whisper + LLM + TTS)** — máxima flexibilidade, pior latência e mais partes móveis; não para a v1.

## Consequências

- Dependência de nuvem e de chave Azure; sem modo offline na v1.
- Formato de entrada exigido: PCM 16-bit 16 kHz mono → vira o formato canônico interno do pipeline.
- Trocar/adicionar provedor = nova implementação de `ISpeechTranslator`, sem tocar no Core.
