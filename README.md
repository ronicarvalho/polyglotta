# Polyglotta — Realtime Dubbing

Dublagem simultânea PT↔EN para chamadas no Meet/Teams. Você fala português, o outro lado ouve inglês sintetizado — e vice-versa.

Roda como um **agente de bandeja** na sua sessão do Windows (ADR-0007): ícone ao lado do relógio com **Ativar/Desativar**, **Configurar**, **Transcrição** e **Fechar**. O CLI continua existindo e usa o mesmo mecanismo, mas o caminho normal é a interface; o CLI é para diagnóstico.

Não há integração com o Meet, o Teams ou qualquer outro app: a tradução sai num microfone virtual, e **qualquer** aplicação que escolher esse microfone funciona.

## Instalando

Baixe o `Polyglotta-<versão>-setup.exe` mais recente em [Releases](https://github.com/ronicarvalho/polyglotta/releases).

A instalação é **por usuário** e não pede administrador: vai para `%LocalAppData%\Programs\Polyglotta`, com atalho no menu Iniciar e entrada em "Aplicativos instalados". Não precisa ter o .NET na máquina — o runtime vai dentro do pacote (ordem de 60 MB de download, 155 MB em disco).

Duas coisas o instalador **não** resolve, e avisa em vez de falhar depois:

- **O VB-CABLE é instalação separada.** É driver de terceiro, pede administrador e não pode ser redistribuído, então o instalador procura e, se não achar, oferece o link. Sem ele a chamada não tem o que escutar.
- **O instalador não é assinado.** O SmartScreen vai avisar na primeira execução: **Mais informações → Executar assim mesmo**. O ADR-0011 explica por que não assinamos (resumo: a via gerenciada barata não atende o Brasil, e a reputação do SmartScreen se constrói por downloads, não por certificado).

Desinstalar remove os binários e a tarefa de início automático, e **mantém** `%AppData%\Polyglotta` — configuração e credenciais continuam lá se você reinstalar. Apague a pasta à mão para zerar.

## Pré-requisitos (Windows)

1. .NET 9 SDK — **só para rodar a partir do código**; quem instala pelo `setup.exe` não precisa
2. [VB-Cable](https://vb-audio.com/Cable/) instalado (cria o microfone virtual "CABLE Input")
3. Conta no provedor de tradução: Azure Speech (`AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`) e/ou Gemini (`GEMINI_API_KEY`)

## Subindo

Instalado, é o atalho **Polyglotta** no menu Iniciar. A partir do código:

```powershell
dotnet run --project src/Polyglotta.Tray
```

Sobe sem console e sem janela: procure o ícone de dois balões ao lado do relógio. Ele começa **desativado** — abrir o seu microfone sozinho no logon não é comportamento que se assume. Clicar no ícone, ou escolher **Configurar**, abre a tela.

## Configurando pela interface

A tela tem quatro abas. Na primeira vez, a ordem que funciona é **Credenciais → Dispositivos → Tradução**, porque a lista de vozes depende da chave estar no lugar.

O rodapé mostra o estado da sessão e, enquanto ela roda, a latência p95 de cada pipeline. Os três botões fazem coisas diferentes:

| Botão | O que faz |
|---|---|
| **Validar** | Só confere. Não grava nada. |
| **Salvar** | Grava a configuração e confere. |
| **Ativar** | Grava e sobe a dublagem. É o equivalente ao comando de linha. |

O resultado da conferência aparece num aviso no topo da tela, com um **✕** para dispensá-lo. O título diz o resultado — "Tudo certo" ou "N pendências" —, então dá para saber se vale ler antes de ler.

> Mexer nos campos não muda nada sozinho: enquanto você não clicar em **Salvar** (ou em **Ativar**, que salva antes de subir), vale o que estava gravado.

### Aba Credenciais

Escolha o nome no combo — ele muda conforme o provedor selecionado na aba Tradução —, cole o valor e clique em **Salvar**. O valor é gravado cifrado com DPAPI em `%APPDATA%\Polyglotta\credentials.json`, decifrável só por você, nesta máquina (ADR-0009).

A linha de status abaixo diz de onde a credencial está vindo e mostra só os quatro últimos caracteres. Se disser **"vindo da variável de ambiente"**, é porque existe uma variável com esse nome, e **ela tem precedência** sobre o que você salvar aqui — útil saber antes de passar meia hora procurando por que a chave nova não fez efeito.

> **Atenção com o `cmd.exe`:** `set VAR="eastus"` guarda as **aspas dentro do valor** e a conexão falha com um erro de DNS confuso (`wss://"eastus".stt.speech…`). Use `set VAR=eastus`, sem aspas. O app remove aspas e espaços sobrando, mas nem todo programa remove.

### Aba Dispositivos

São três escolhas, e a relação entre elas é o que faz a coisa funcionar:

| Campo | O que é |
|---|---|
| **Microfone** | Onde você fala. |
| **Saída da chamada** | Onde o Meet/Teams **toca** o áudio do interlocutor. É o que o app captura por loopback. |
| **Fone** | Onde **você** ouve a tradução. |

**Saída da chamada e Fone precisam ser dispositivos diferentes.** Se forem o mesmo, a tradução que o app reproduz seria recapturada e retraduzida em loop — e o app se recusa a iniciar, em vez de produzir esse resultado. O arranjo típico é configurar o Meet/Teams para tocar numa saída secundária (um monitor por HDMI, por exemplo) e deixar o fone só para você.

O microfone virtual **não se escolhe**, e por isso não aparece: os dois lados do cabo ficam fora das três listas. O lado de saída ("CABLE Input") é destino fixo da tradução, encontrado sozinho; o lado de entrada ("CABLE Output") é o que a chamada escuta, e usá-lo como microfone faria o app capturar a própria tradução. Outros dispositivos do VB-Cable — "CABLE In 16ch", por exemplo — são dispositivos diferentes e continuam disponíveis: são uma escolha legítima de saída da chamada.

Esconder da tela **não** desliga a verificação: o CLI aceita qualquer nome e uma configuração antiga pode ter o cabo gravado, então a validação continua recusando esses casos com a explicação. Os nomes dos dois lados são configuração, não código (`VirtualMicName` e `VirtualMicCaptureName`), porque no macOS e no Linux o cabo virtual tem outro nome — e costuma ser um único dispositivo servindo de entrada e saída.

**Reler dispositivos** atualiza as listas sem fechar a tela, para quando você pluga um fone no meio da configuração.

> Dispositivos **desabilitados** no Windows não aparecem na lista e não podem ser usados. Se o seu microfone sumiu, abra `mmsys.cpl` → aba Gravação → botão direito → *Mostrar dispositivos desativados* → Ativar.

A configuração guarda **nome e id** de cada dispositivo, tentando o nome primeiro: id de endpoint muda em troca de hardware e em atualização do Windows, e já mudou duas vezes neste projeto.

### Aba Tradução

**Provedor** (`azure` ou `gemini`), **Eu falo** (seu idioma) e **Ele ouve** (o idioma que sai para o outro lado).

Abaixo do separador ficam as duas vozes:

- **que ele ouve** — a voz que fala no idioma de destino, para o interlocutor.
- **que eu ouço** — a voz que fala no seu idioma, traduzindo o interlocutor.

As duas listas vêm do **catálogo ao vivo do seu próprio recurso Azure** — não de uma tabela fixa —, filtradas pelo idioma de cada lado, com o nome local, o gênero e a marca `HD`. No topo ficam dois apelidos, **Feminina** e **Masculina**: eles continuam escolhendo a melhor voz do catálogo a cada sessão (preferindo as famílias `DragonHD`, bem mais naturais que as neurais de primeira geração), enquanto escolher um nome concreto **fixa** aquela voz para sempre. As duas coisas são legítimas; a diferença é se você quer acompanhar o catálogo ou pinar o timbre.

O botão **Ouvir** sintetiza uma frase na língua da voz e toca no seu fone. Ele usa a voz **efetiva**: com um apelido selecionado, você ouve exatamente a voz que a chamada usaria.

Sem credenciais, ou com o catálogo fora do ar, as combos caem de volta nos dois apelidos e a linha de status diz o motivo. **Recarregar vozes** tenta de novo — é o que usar depois de salvar a chave.

> Com o provedor **gemini** as vozes ficam desabilitadas: o modo de tradução dele não permite escolher voz (ADR-0006).

### Aba Instância

**Iniciar o Polyglotta ao entrar no Windows** cria uma tarefa agendada de logon, sem exigir administrador. Desmarcar remove a tarefa. A linha de status abaixo confirma ou diz por que falhou.

**Já ativar a dublagem ao iniciar** faz o agente subir dublando, em vez de subir parado. Deixe desmarcado até confiar no arranjo de dispositivos.

A aba também mostra a versão em execução e onde fica o `config.json`. Os logs ficam em `%APPDATA%\Polyglotta\logs`, um arquivo por dia.

## Usando numa chamada

1. **Ativar** — pela tela ou pelo menu da bandeja. O ícone muda de estado.
2. No Meet/Teams, escolha **CABLE Output** como microfone.
3. No Meet/Teams, escolha como alto-falante o mesmo dispositivo que você marcou em **Saída da chamada**.
4. Fale.

**Transcrição**, no menu da bandeja, abre uma janela à parte com o que foi reconhecido e como foi traduzido, nos dois sentidos. Ela acompanha a conversa com a tela de configuração fechada, e rola sozinha para a linha mais recente.

Se a sessão cair, o agente para e abre a tela com o erro. **Reconexão automática ainda não existe** (RF-07): hoje é você quem clica em Ativar de novo.

## CLI (diagnóstico)

O CLI lê e grava a **mesma** configuração da bandeja, então dá para configurar num e rodar no outro. Ele é o caminho quando você precisa de saída em texto, de um erro completo, ou de rodar sem interface.

```powershell
dotnet run --project src/Polyglotta.Cli -- --help
dotnet run --project src/Polyglotta.Cli -- --list-devices
dotnet run --project src/Polyglotta.Cli -- --show-config
dotnet run --project src/Polyglotta.Cli -- --from pt-BR --to en-US --voice male
```

Gravar a configuração inteira de uma vez, sem a tela:

```powershell
dotnet run --project src/Polyglotta.Cli -- --save-config --provider azure --from pt-BR --to en-US --mic "USB PnP" --loopback "27HC5R (2-" --phones "Speakers" --voice male --voice-in female
```

`--mic`, `--loopback` e `--phones` aceitam **o id ou um trecho do nome**. Se o trecho casar com mais de um dispositivo, o app lista os candidatos e pede algo mais específico. Os ids vêm de `--list-devices` (captura começa com `{0.0.1…}`, reprodução com `{0.0.0…}`).

Credenciais, sem ecoar o valor no terminal:

```powershell
dotnet run --project src/Polyglotta.Cli -- --set-credential AZURE_SPEECH_KEY
dotnet run --project src/Polyglotta.Cli -- --forget-credential AZURE_SPEECH_KEY
```

Catálogo de vozes do seu recurso:

```powershell
dotnet run --project src/Polyglotta.Cli -- --list-voices          # idiomas de origem e destino
dotnet run --project src/Polyglotta.Cli -- --list-voices pt-BR    # um locale específico
```

O plano gratuito (F0) **não** reduz a qualidade da voz — ele limita volume de caracteres e concorrência.

### Testando os dispositivos sem gastar Azure

```powershell
dotnet run --project src/Polyglotta.Cli -- --check-audio 10
```

Mede o microfone e o loopback pelo número de segundos indicado (padrão 8), mostrando uma barra de nível por segundo, e no fim toca 1 s de tom no fone e no "CABLE Input". Não exige credencial nenhuma. Sai com código 1 se o microfone não captou voz alguma — útil como verificação antes de uma reunião.

### Escolhendo o provedor

| | `azure` (padrão) | `gemini` |
|---|---|---|
| Arquitetura | STT → tradução → TTS | fala-para-fala nativo |
| Credenciais | `AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION` | `GEMINI_API_KEY`, ou `GOOGLE_CLOUD_PROJECT` + `GOOGLE_CLOUD_LOCATION` (Vertex AI) |
| Escolha de voz | sim | **não** — o modo de tradução não expõe seleção de voz |
| Maturidade | GA | modelo em preview (`GEMINI_LIVE_MODEL` fixa outro) |

Ver ADR-0006. A comparação entre os dois é o objetivo: meça a latência pelo p95 e ouça as duas saídas antes de eleger um padrão.

## Quando não aparece transcrição nenhuma

A cada 5 s o app avalia o nível de entrada de cada pipeline. Se aparecer

```
warn: [outbound] entrada sem sinal: 250 frames em 5s, pico 0,0 % do máximo.
```

o áudio está chegando, mas mudo: microfone desconectado, mudo no Windows, ou — no caso do inbound — nada tocando no dispositivo capturado por loopback. Enquanto a entrada for silêncio, o Azure segmenta o áudio e devolve resultados vazios, registrados como `segmento sem fala reconhecida` (nível Debug).

## Ícones

O ícone tem três estados: ativo, parado e com erro. A arte é vetorial e vive em `docs/design/*.svg`; `src/Polyglotta.Tray/Assets/*.png` e o `.ico` do executável são rasterizações dela. Para mudar o ícone, mude o SVG e re-renderize — não edite o PNG.

## Desenvolvimento com Claude Code

Este repositório é preparado para desenvolvimento agêntico: contexto em `CLAUDE.md`, requisitos em `docs/prd/`, decisões em `docs/adr/`, manuais de bibliotecas em `docs/man`, skills do projeto em `.claude/skills/` e o comando `/new-adr` em `.claude/commands/`.

## Publicando uma versão

A versão vem da **tag**, não de um número escrito em `.csproj` (ADR-0011). As tags de publicação vivem sob `releases/`, sem o `v` — `releases/0.2.0`. Publicar é uma linha:

```powershell
git tag releases/0.2.0
git push origin releases/0.2.0
```

A tag dispara `.github/workflows/release.yml`: roda os testes, publica self-contained `win-x64`, compila o instalador com o Inno Setup e cria o GitHub Release com o `setup.exe` e o `.zip`. Merge na `main` **não** publica — ali roda só build e teste (`build.yml`).

O número aparece no log e na aba Instância; fora de uma versão publicada ele vale `0.0.0-dev`, que é como você sabe que aquele binário não saiu de uma tag.

Para compilar o instalador aqui antes de taggear, é preciso o Inno Setup 6:

```powershell
winget install -e --id JRSoftware.InnoSetup
```

```powershell
dotnet publish src/Polyglotta.Tray/Polyglotta.Tray.csproj -c Release -r win-x64 --self-contained true -p:Version=0.2.0 -o publish
dotnet publish src/Polyglotta.Cli/Polyglotta.Cli.csproj -c Release -r win-x64 --self-contained true -p:Version=0.2.0 -o publish
Remove-Item publish\*.pdb -ErrorAction SilentlyContinue
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=0.2.0 /DVersionInfo=0.2.0 installer\Polyglotta.iss
```

O instalador sai em `installer\out\Polyglotta-0.2.0-setup.exe`.

## Build e testes

```powershell
dotnet build src/Polyglotta.sln
dotnet test src/Polyglotta.sln
```

A orquestração mora em `Polyglotta.Host` (neutro de plataforma): o CLI e a bandeja são dois frontends finos sobre o mesmo `DubbingSession`, e as guardas de roteamento são as mesmas nos dois porque vêm do mesmo `SessionPlanner`.

A camada de áudio usa NAudio 3.x com a API de builders (`WasapiRecorderBuilder`/`WasapiPlayerBuilder`) — ver ADR-0005. As classes `WasapiCapture`/`WasapiOut` do NAudio 2.x estão obsoletas e são tratadas como erro de compilação em `Polyglotta.Audio.Windows`.
