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

## Análise de projeto
Já existe uma solution criada no diretório `src/Polyglotta.sln` com seus respectivos projetos, mas por questões de versionamento do NAudio, os projetos devem ser analisados e compilados antes de iniciar qualquer codificação
