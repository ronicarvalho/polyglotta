# ADR-0001 — C# / .NET 9 como linguagem principal

**Status:** Aceito · **Data:** 2026-09-18

## Contexto

O pipeline local é leve: capturar buffers de ~20 ms, enviar por streaming à nuvem e reproduzir o retorno. O trabalho pesado (STT, tradução, TTS) é remoto; a latência total esperada de 1,5–3 s absorve pausas de GC de poucos ms. O time tem muito mais experiência em C# do que em Rust.

## Decisão

Usaremos C# com .NET 9 em todo o projeto.

## Alternativas consideradas

- **Rust** — melhor caso teórico para áudio (cpal, sem GC), mas curva de aprendizado alta e o gargalo real é a nuvem, não o runtime.
- **Go** — GC ok, mas ecossistema de áudio fraco.
- **Java** — javax.sound sem loopback WASAPI decente; ecossistema de áudio o mais fraco.
- **C/C++** — só se fôssemos escrever driver, o que ADR-0002 descarta.

## Consequências

- Produtividade máxima do time; SDK do Azure Speech é first-class em .NET.
- NAudio cobre WASAPI no Windows; no Linux futuro, NAudio.Linux.Alsa (preview), PortAudio.NET ou bindings de miniaudio — menos polidos que o cpal do Rust, risco aceito.
- Publicação com `PublishTrimmed`/AOT quando fizer sentido.
