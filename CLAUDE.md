# CLAUDE.md — Polyglotta (Realtime Dubbing)

App de dublagem simultânea para chamadas (Meet/Teams): o usuário fala PT-BR e o interlocutor ouve EN sintetizado, e vice-versa. Windows primeiro (v1), Mac e Linux depois.

## Como funciona (visão de 30 segundos)

Dois pipelines simultâneos e independentes:

1. **Outbound**: microfone físico → tradução de fala em streaming (Azure) → áudio sintetizado → microfone virtual (VB-Cable), selecionado no Meet/Teams como entrada.
2. **Inbound**: loopback WASAPI do áudio do sistema (voz do interlocutor) → mesmo pipeline → reprodução no fone do usuário.

Latência alvo: 1,5–3s ponta a ponta ("tradução consecutiva rápida", não instantânea).

## Stack e decisões já tomadas

- **C# / .NET 9**. Não sugerir Rust/Go/C++ — decidido em ADR-0001.
- **NAudio 3.x** para WASAPI (captura, loopback, render) — ADR-0004; API de builders (`WasapiRecorderBuilder`/`WasapiPlayerBuilder`), classes 2.x obsoletas são erro de build — ADR-0005.
- **VB-Cable** como microfone virtual na v1; NÃO escrever driver de áudio — ADR-0002.
- **Azure Speech Translation** (speech-to-speech streaming) como provedor v1 — ADR-0003.
- Toda a lógica portável fica em `Polyglotta.Core` atrás de interfaces; código de plataforma isolado em `Polyglotta.Audio.Windows` — ADR-0004.

Antes de qualquer decisão arquitetural nova, leia `docs/adr/` e proponha um ADR (use `/new-adr`).

## Layout do repositório

```
docs/prd/        PRDs — o que construir e por quê
docs/adr/        ADRs — decisões arquiteturais (numeradas)
src/Polyglotta.Core/                Interfaces + orquestração do pipeline (sem dependência de plataforma)
src/Polyglotta.Audio.Windows/       WASAPI/NAudio: mic, loopback, render no VB-Cable
src/Polyglotta.Translation.Azure/   Azure Speech SDK (TranslationRecognizer + síntese)
src/Polyglotta.Cli/                 v0: console app que liga os dois pipelines
tests/Polyglotta.Core.Tests/        xUnit
tests/Polyglotta.Audio.Windows.Tests/  xUnit — só lógica pura (CanonicalFramer); I/O de dispositivo é teste manual
docs/review/     Revisões de código e status de cobertura do PRD
```

## Comandos

```bash
dotnet build src/Polyglotta.sln
dotnet test src/Polyglotta.sln
dotnet run --project src/Polyglotta.Cli -- --list-devices
dotnet run --project src/Polyglotta.Cli -- --from pt-BR --to en-US
```

Variáveis de ambiente necessárias em runtime: `AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`.

## Convenções de áudio (críticas — ver skill audio-realtime)

- Formato interno canônico do pipeline: **PCM 16-bit, 16 kHz, mono** (formato que o Azure Speech espera). Conversões acontecem na borda (camada de plataforma), nunca no Core.
- Frames de ~20 ms. Callbacks de áudio NUNCA bloqueiam: nada de I/O, rede ou lock demorado dentro de `DataAvailable` — só enfileirar em `Channel<AudioFrame>`.
- `Polyglotta.Core` não referencia NAudio nem Azure SDK. Se precisar de um tipo deles no Core, a abstração está errada.

## Convenções de código

- C# 14, nullable habilitado, `var` quando o tipo é óbvio.
- Async de ponta a ponta; `CancellationToken` em toda API pública assíncrona.
- Nomes de código em inglês; docs (PRD/ADR/comentários de contexto) em PT-BR.
- Um teste novo para cada comportamento novo no Core. Código de plataforma (WASAPI) é testado manualmente — documente o roteiro de teste no PR. Para checar uma API do NAudio antes de codificar, faça um probe descartável no scratchpad (funcionou bem: `WithFormat`, loopback, restart).

## Workflow com o agente

1. Leia o PRD relevante em `docs/prd/` antes de implementar uma feature.
2. Decisão de arquitetura nova ou troca de dependência → ADR primeiro, código depois.
3. Mudou comportamento descrito num PRD/ADR? Atualize o documento no mesmo commit.
4. Commits pequenos, mensagens em inglês, formato convencional (`feat:`, `fix:`, `docs:`, `refactor:`).
