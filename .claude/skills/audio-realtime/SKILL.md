---
name: audio-realtime
description: Convenções e armadilhas do pipeline de áudio em tempo real deste projeto. Use SEMPRE que for tocar em captura, reprodução, loopback, WASAPI, NAudio, VB-Cable, buffers, resampling, formatos PCM, latência, eco/feedback, ou implementar/alterar qualquer classe em Polyglotta.Audio.* ou no pipeline do Core — mesmo que a tarefa pareça simples.
---

# Áudio em tempo real — regras do projeto

## Formato canônico

Todo áudio dentro do pipeline (Core) é **PCM 16-bit, 16 kHz, mono** — o formato que o Azure Speech consome. A conversão do formato nativo do dispositivo (ex.: 48 kHz estéreo float) acontece na camada de plataforma, na borda, antes de entrar no `Channel<AudioFrame>`. Nunca faça resample dentro do Core.

No Windows/NAudio 3.x (ADR-0005): peça o formato canônico ao próprio WASAPI com `WasapiRecorderBuilder.WithFormat(16 kHz/16-bit/mono)` — em shared mode o audio engine converte (AutoConvertPcm), sem resample em processo. `CanonicalFramer` mantém um fallback (`StereoToMonoSampleProvider` + `WdlResamplingSampleProvider`) para quando o engine não honrar o formato, e fatia tudo em frames de 640 bytes. Se usar `BufferedWaveProvider` como fonte de um loop de drenagem, `ReadFully = false` — senão ele preenche com silêncio para sempre.

## Regras de callback (inegociáveis)

- `DataAvailable` (NAudio) roda em thread de áudio: **nunca** bloquear com I/O, rede, `lock` demorado ou alocação pesada. Só copiar o buffer e `TryWrite` num `Channel<AudioFrame>` bounded.
- Channel bounded com `BoundedChannelFullMode.DropOldest`: em backpressure, descartar áudio velho é melhor que acumular latência.
- Frames de ~20 ms (640 bytes a 16 kHz/16-bit/mono). Não dependa do tamanho do buffer do driver: `CanonicalFramer` acumula e fatia; `WithBufferLength(20)` só aproxima o período do callback.

## Loopback e eco

- Loopback WASAPI (`WasapiRecorderBuilder.WithLoopbackCapture()`) captura TUDO que sai no dispositivo de render — inclusive o áudio traduzido que o próprio app reproduz. Para evitar loop de feedback: reproduza a tradução inbound num dispositivo diferente do capturado em loopback, ou capture o loopback do dispositivo onde só o Meet/Teams toca.
- Loopback só entrega pacotes enquanto algum app estiver renderizando no dispositivo (verificado em 2026-09-21): sem Meet/Teams tocando, `Frames` fica em silêncio — não é bug.
- Documente no PR qual configuração de dispositivos foi testada manualmente.

## Dispositivo virtual

- Render outbound vai para o dispositivo cujo nome contém "CABLE Input" (VB-Cable). O usuário seleciona "CABLE Output" no Meet/Teams.
- Se "CABLE Input" não existir, falhe com mensagem orientando a instalar o VB-Cable — não caia silenciosamente no dispositivo default.

## Latência

- Meça e logue: timestamp do último frame de fala → primeiro sample sintetizado reproduzido. Meta p95 ≤ 3 s (PRD-001).
- Não adicione buffers "por segurança" sem medir; cada 100 ms de buffer é 100 ms de latência.

## Ao portar (Mac/Linux)

Implemente as interfaces de `Polyglotta.Core/Audio/` num projeto novo (`Polyglotta.Audio.Mac`, `Polyglotta.Audio.Linux`). Não adicione `#if WINDOWS` no Core — se sentir necessidade, a abstração está errada (ADR-0004).
