# Polyglotta — Realtime Dubbing

Dublagem simultânea PT↔EN para chamadas no Meet/Teams. Você fala português, o outro lado ouve inglês sintetizado — e vice-versa.

## Pré-requisitos (Windows)

1. .NET 9 SDK
2. [VB-Cable](https://vb-audio.com/Cable/) instalado (cria o microfone virtual "CABLE Input")
3. Chave do Azure Speech (`AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`)

### Configurando as chaves

No PowerShell (só nesta janela):

```powershell
$env:AZURE_SPEECH_KEY = 'cole-a-chave-aqui'
$env:AZURE_SPEECH_REGION = 'eastus'
```

Para persistir entre sessões: `setx AZURE_SPEECH_REGION eastus` (abra um terminal novo depois).

> **Atenção:** no `cmd.exe`, `set VAR="eastus"` guarda as **aspas dentro do valor** e a conexão falha com um erro de DNS confuso (`wss://"eastus".stt.speech…`). Use `set VAR=eastus`, sem aspas. O app remove aspas e espaços sobrando e loga a região no startup (`Azure region: eastus`) — confira essa linha se houver erro de conexão.

## Rodando a v0 (console)

```bash
dotnet run --project src/Polyglotta.Cli -- --list-devices
dotnet run --project src/Polyglotta.Cli -- --from pt-BR --to en-US --voice en-US-JennyNeural
```

No Meet/Teams, selecione **CABLE Output** como microfone.

### Dispositivos e eco

O pipeline inbound captura, por loopback, o dispositivo em que o Meet/Teams toca e reproduz a tradução no seu fone. Esses dois **precisam ser dispositivos diferentes** — caso contrário a tradução seria recapturada e retraduzida em loop, e o app se recusa a iniciar. Configure o Meet/Teams para tocar num dispositivo (ex.: "CABLE In 16ch" ou uma saída secundária) e aponte o CLI:

```bash
dotnet run --project src/Polyglotta.Cli -- --mic "<id do microfone>" --loopback "<id do dispositivo do Meet>" --phones "<id do seu fone>"
```

Cada opção aceita **o id ou um trecho do nome** do dispositivo, o que costuma ser mais estável e legível:

```bash
dotnet run --project src/Polyglotta.Cli -- --mic "USB Audio" --loopback "Display Audio" --phones "Headphones"
```

Se o trecho casar com mais de um dispositivo, o app lista os candidatos e pede algo mais específico. Os ids vêm de `--list-devices` (captura começa com `{0.0.1…}`, reprodução com `{0.0.0…}`). Sem `--mic`, usa o microfone padrão de comunicação.

> Dispositivos **desabilitados** no Windows não aparecem em `--list-devices` e não podem ser usados. Se o seu microfone sumiu da lista, abra `mmsys.cpl` → aba Gravação → botão direito → *Mostrar dispositivos desativados* → Ativar. Sem `--loopback`/`--phones` o CLI usa o dispositivo de comunicação padrão para ambos, o que dispara a guarda. "CABLE Input" é reservado ao microfone virtual e não pode ser usado em nenhum dos dois, assim como "CABLE Output" não pode ser o `--mic`.

### Escolhendo a voz

`--voice` define a voz que o outro lado ouve (idioma de saída) e `--voice-in` a que você ouve (idioma de entrada). Ambas aceitam `female`/`male` (ou `feminina`/`masculina`) e também o nome completo de uma voz Azure:

```bash
dotnet run --project src/Polyglotta.Cli -- --voice male --voice-in feminina ...
```

O padrão é voz **HD** (famílias `DragonHD` e `DragonHDOmni`), bem mais natural que as vozes neurais de primeira geração como `JennyNeural` e `FranciscaNeural`. Para ver o que o seu recurso oferece:

```bash
dotnet run --project src/Polyglotta.Cli -- --list-voices          # locais de origem e destino
dotnet run --project src/Polyglotta.Cli -- --list-voices pt-BR    # um locale específico
```

Vozes marcadas com `HD` na listagem são as de alta definição. O plano gratuito (F0) **não** reduz a qualidade da voz — ele limita volume de caracteres e concorrência.

### Testando os dispositivos sem gastar Azure

```bash
dotnet run --project src/Polyglotta.Cli -- --check-audio 10 --mic "..." --loopback "..." --phones "..."
```

Mede o microfone e o loopback pelo número de segundos indicado (padrão 8), mostrando uma barra de nível por segundo, e no fim toca 1 s de tom no fone e no "CABLE Input". Não exige `AZURE_SPEECH_KEY`. Sai com código 1 se o microfone não captou voz alguma — útil como verificação antes de uma reunião.

### O log não mostra nenhuma transcrição

A cada 5 s o app avalia o nível de entrada de cada pipeline. Se aparecer

```
warn: [outbound] entrada sem sinal: 250 frames em 5s, pico 0,0 % do máximo.
```

o áudio está chegando, mas mudo: microfone desconectado, mudo no Windows, ou — no caso do inbound — nada tocando no dispositivo capturado por loopback. Enquanto a entrada for silêncio, o Azure segmenta o áudio e devolve resultados vazios, registrados como `segmento sem fala reconhecida` (nível Debug).

## Desenvolvimento com Claude Code

Este repositório é preparado para desenvolvimento agêntico: contexto em `CLAUDE.md`, requisitos em `docs/prd/`, decisões em `docs/adr/`, manuais de bibliotecas em `docs/man` skills do projeto em `.claude/skills/` e o comando `/new-adr` em `.claude/commands/`.

## Build e testes

```bash
dotnet build src/Polyglotta.sln
dotnet test src/Polyglotta.sln
```

A camada de áudio usa NAudio 3.x com a API de builders (`WasapiRecorderBuilder`/`WasapiPlayerBuilder`) — ver ADR-0005. As classes `WasapiCapture`/`WasapiOut` do NAudio 2.x estão obsoletas e são tratadas como erro de compilação em `Polyglotta.Audio.Windows`.
