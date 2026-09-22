# ADR-0004 — Abstração da camada de áudio para portabilidade

**Status:** Aceito · **Data:** 2026-09-18

## Contexto

Alvos: Windows (v1), depois Mac e Linux. Cada plataforma tem API (WASAPI, CoreAudio, PipeWire/ALSA) e dispositivo virtual próprios. Espalhar código de plataforma pelo pipeline tornaria o porte caro.

## Decisão

`Polyglotta.Core` define `IAudioCaptureDevice`, `IAudioRenderDevice`, `ISystemAudioCapture` (loopback) e `IAudioDeviceEnumerator`, e não referencia nenhuma biblioteca de plataforma. Implementações vivem em projetos por plataforma (`Polyglotta.Audio.Windows` com NAudio — versão fixada no ADR-0005; futuros `Polyglotta.Audio.Mac` e `Polyglotta.Audio.Linux`).

*Nota editorial (2026-09-21): os projetos foram nomeados `Polyglotta.*`; o texto original dizia `Dubbing.*`. Apenas os nomes foram atualizados — a decisão é a mesma.*

## Alternativas consideradas

- **Biblioteca multiplataforma única (miniaudio/PortAudio/xt-audio via bindings)** — reduz código por plataforma, mas loopback WASAPI e dispositivos virtuais variam; segue como opção interna dos projetos de plataforma.

## Consequências

- Porte = implementar 4 interfaces + resolver o dispositivo virtual da plataforma.
- Core 100% testável sem hardware.
- Conversão de formato (resample para 16 kHz mono) é responsabilidade da camada de plataforma.
