---
name: audio-realtime
description: Convenções e armadilhas do pipeline de áudio em tempo real deste projeto. Use SEMPRE que for tocar em captura, reprodução, loopback, WASAPI, NAudio, VB-Cable, buffers, resampling, formatos PCM, latência, eco/feedback, ou implementar/alterar qualquer classe em Dubbing.Audio.* ou no pipeline do Core — mesmo que a tarefa pareça simples.
---

# Áudio em tempo real — regras do projeto

## Formato canônico

Todo áudio dentro do pipeline (Core) é **PCM 16-bit, 16 kHz, mono** — o formato que o Azure Speech consome. A conversão do formato nativo do dispositivo (ex.: 48 kHz estéreo float) acontece na camada de plataforma, na borda, antes de entrar no `Channel<AudioFrame>`. Nunca faça resample dentro do Core.

No Windows/NAudio: use `WdlResamplingSampleProvider` ou `MediaFoundationResampler` para converter a captura; para downmix estéreo→mono, média dos canais.

## Regras de callback (inegociáveis)

- `DataAvailable` (NAudio) roda em thread de áudio: **nunca** bloquear com I/O, rede, `lock` demorado ou alocação pesada. Só copiar o buffer e `TryWrite` num `Channel<AudioFrame>` bounded.
- Channel bounded com `BoundedChannelFullMode.DropOldest`: em backpressure, descartar áudio velho é melhor que acumular latência.
- Frames de ~20 ms (640 bytes a 16 kHz/16-bit/mono). Não dependa do tamanho do buffer do driver.

## Loopback e eco

- Loopback WASAPI (`WasapiLoopbackCapture`) captura TUDO que sai no dispositivo de render — inclusive o áudio traduzido que o próprio app reproduz. Para evitar loop de feedback: reproduza a tradução inbound num dispositivo diferente do capturado em loopback, ou capture o loopback do dispositivo onde só o Meet/Teams toca.
- Documente no PR qual configuração de dispositivos foi testada manualmente.

## Dispositivo virtual

- Render outbound vai para o dispositivo cujo nome contém "CABLE Input" (VB-Cable). O usuário seleciona "CABLE Output" no Meet/Teams.
- Se "CABLE Input" não existir, falhe com mensagem orientando a instalar o VB-Cable — não caia silenciosamente no dispositivo default.

## Latência

- Meça e logue: timestamp do último frame de fala → primeiro sample sintetizado reproduzido. Meta p95 ≤ 3 s (PRD-001).
- Não adicione buffers "por segurança" sem medir; cada 100 ms de buffer é 100 ms de latência.

## Ao portar (Mac/Linux)

Implemente as interfaces de `Dubbing.Core/Audio/` num projeto novo (`Dubbing.Audio.Mac`, `Dubbing.Audio.Linux`). Não adicione `#if WINDOWS` no Core — se sentir necessidade, a abstração está errada (ADR-0004).
