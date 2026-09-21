# Realtime Dubbing

Dublagem simultânea PT↔EN para chamadas no Meet/Teams. Você fala português, o outro lado ouve inglês sintetizado — e vice-versa.

## Pré-requisitos (Windows)

1. .NET 9 SDK
2. [VB-Cable](https://vb-audio.com/Cable/) instalado (cria o microfone virtual "CABLE Input")
3. Chave do Azure Speech (`AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`)

## Rodando a v0 (console)

```bash
dotnet run --project src/Polyglotta.Cli -- --list-devices
dotnet run --project src/Polyglotta.Cli -- --from pt-BR --to en-US --voice en-US-JennyNeural
```

No Meet/Teams, selecione **CABLE Output** como microfone.

## Desenvolvimento com Claude Code

Este repositório é preparado para desenvolvimento agêntico: contexto em `CLAUDE.md`, requisitos em `docs/prd/`, decisões em `docs/adr/`, manuais de bibliotecas em `docs/man` skills do projeto em `.claude/skills/` e o comando `/new-adr` em `.claude/commands/`.

## Build e testes

```bash
dotnet build src/Polyglotta.sln
dotnet test src/Polyglotta.sln
```

A camada de áudio usa NAudio 3.x com a API de builders (`WasapiRecorderBuilder`/`WasapiPlayerBuilder`) — ver ADR-0005. As classes `WasapiCapture`/`WasapiOut` do NAudio 2.x estão obsoletas e são tratadas como erro de compilação em `Polyglotta.Audio.Windows`.
