# ADR-0011 — Instalador com Inno Setup e publicação por tag no GitHub Actions

**Status:** Aceito · **Data:** 2026-10-29

## Contexto

Hoje a única forma de rodar o Polyglotta é `dotnet run` com o SDK instalado. Para usar numa máquina que não seja a de desenvolvimento — ou para outra pessoa usar — falta um artefato instalável.

O que o app é, e que restringe as opções:

- **Agente por usuário.** Configuração em `%APPDATA%\Polyglotta`, credenciais cifradas com DPAPI no escopo do usuário (ADR-0009), início automático por tarefa agendada de logon na conta do usuário (ADR-0007). Nada disso é por máquina, e nada disso exige administrador.
- **Depende do VB-Cable** (ADR-0002), que é software de terceiro, exige administrador, instala um driver e às vezes pede reinício.
- **Windows-only por enquanto.** `net9.0-windows`, WASAPI. Linux e macOS exigem ADRs próprios de camada de áudio.
- Hoje não existe **versão** em lugar nenhum: nenhum `.csproj` define `Version`, e não há `Directory.Build.props`.

Verificações feitas para este ADR (2026-10-29), na imagem `windows-2025` do GitHub Actions:

- **Inno Setup 6.7.1 vem pré-instalado.** Não precisa de passo de instalação no workflow.
- WiX Toolset 3.14.1.8722 também vem pré-instalado.
- SDKs .NET 9 e 10 vêm pré-instalados (9.0.122 a 9.0.319, 10.0.113 a 10.0.402).
- O repositório `ronicarvalho/polyglotta` responde **404 na API pública** do GitHub, ou seja, é privado. Isso elimina o programa gratuito de assinatura de código da SignPath Foundation, que exige repositório público com licença OSI.

## Decisão

**Instalador com Inno Setup, por usuário, gerado no GitHub Actions a partir de uma tag `releases/*`.**

1. **Publicação self-contained**, `win-x64`, sem trimming:
   `dotnet publish src/Polyglotta.Tray -c Release -r win-x64 --self-contained -p:Version=$TAG`.
   Sem trimming porque o SDK do Azure Speech carrega binários nativos e o Avalonia usa reflexão; o ganho de tamanho não paga o risco de quebrar em runtime, que é justamente o que o CI não pegaria. O CLI entra no mesmo pacote: é a ferramenta de diagnóstico documentada no README.
2. **Instalação por usuário**, `PrivilegesRequired=lowest`, em `%LocalAppData%\Programs\Polyglotta`. Sem UAC, coerente com um app cuja configuração, credenciais e autostart já são todos por usuário. Instalar por máquina criaria a expectativa errada de um serviço compartilhado, que o ADR-0007 recusou.
3. **O instalador não embute o VB-Cable**: ele **detecta** e, se faltar, mostra o link e explica que é instalação separada, com administrador. A licença do VB-Audio não permite redistribuição sem acordo, e empacotar driver de terceiro transformaria um instalador sem privilégio num instalador com privilégio.
4. **Dois gatilhos, papéis diferentes:**
   - `push` em `main` e `pull_request`: `dotnet build` + `dotnet test`. Sem release.
   - `push` de tag `releases/*`: publica, gera o instalador e cria o **GitHub Release** com `Polyglotta-<versão>-setup.exe` e o `.zip` do publish.
5. **A versão vem da tag** (`releases/1.2.3` → `-p:Version=1.2.3`), injetada no build e no `AppVersion` do Inno. Nada de versão escrita à mão em `.csproj`. O nome é `releases/<versão>`, **sem o `v`**: barra em nome de tag é só hierarquia de ref (`refs/tags/releases/1.2.3`), o que agrupa as publicações como uma pasta na listagem de tags, e o `v` nunca foi exigência de ferramenta — o workflow remove o prefixo e passa o resto adiante. A única restrição é não criar uma tag chamada exatamente `releases`, que colidiria com o diretório de refs.
6. **Desinstalar não apaga dados do usuário**: `%APPDATA%\Polyglotta` fica. Ele contém credenciais e configuração que a pessoa pode querer de volta; o instalador remove a tarefa agendada e os binários, e o README diz onde está o resto.

### Por que não no merge para a `main`

Foi a pergunta de partida, e dá para fazer. Mas todo merge virando release significa que corrigir um typo no README publica uma versão nova, com número novo, para os usuários baixarem. A `main` passa a não poder receber trabalho incompleto, e o número da versão deixa de significar algo. Com tag, publicar é um ato deliberado de uma linha (`git tag releases/0.2.0 && git push origin releases/0.2.0`), enquanto a `main` continua protegida por build e teste a cada merge.

## Alternativas consideradas

- **Só um `.zip` do publish, sem instalador** — rejeitada como entrega final, mas **mantida como artefato adicional** no release. É honestamente quase suficiente: o app não tem componente de sistema, roda de qualquer pasta e se registra no logon sozinho pela tela. O que falta é o que um instalador dá de graça: atalho no menu Iniciar, entrada em "Aplicativos instalados" com desinstalação, e um lugar previsível para o executável — que importa porque a tarefa agendada grava o caminho absoluto do exe, e um zip movido de pasta quebra o autostart silenciosamente.
- **Velopack** — rejeitada **por ora**, e é a candidata a substituir esta decisão depois. Dá instalador, atualização automática e pacotes delta, que é exatamente o que falta quando houver usuários que não acompanham o repositório. O custo é uma dependência NuGet **dentro do app** e um caminho de código de verificação de atualização — peso que não se justifica enquanto o público é uma pessoa que roda `git pull`. Reavaliar quando houver um segundo usuário, ou quando o RF-07 estiver pronto e o app passar a ser algo que fica rodando sem supervisão. (Ao adotar, confirmar na documentação oficial o escopo de instalação: pela herança do Squirrel deve ser por usuário em `%LocalAppData%`, mas não achei isso escrito na documentação atual.)
- **MSIX** — rejeitada. É o formato moderno e daria desinstalação limpa, mas: (a) **não instala sem assinatura** — nem para testar, sem antes confiar num certificado autoassinado na máquina; (b) o autostart teria que deixar de ser tarefa agendada e virar a extensão `StartupTask` do pacote, ou seja, **reescrever `WindowsAutostart`** por causa do empacotamento; (c) a virtualização de registro e sistema de arquivos adiciona uma variável a um app que fala com WASAPI e com drivers de áudio, onde diagnosticar já é caro. Custo alto, benefício nenhum no estado atual.
- **WiX/MSI** — rejeitada. Disponível no runner, formato respeitável, mas é pensado para instalação por máquina e políticas corporativas, com XML bem mais verboso para o mesmo resultado. Não há aqui nada que peça MSI, e nenhuma vantagem sobre o Inno para um app por usuário.
- **Microsoft Store** — rejeitada. Exige MSIX (ver acima), conta de desenvolvedor e revisão, para distribuir um app que depende de o usuário instalar um driver de terceiro por fora. Fluxo incompatível com o produto.

## Consequências

- **O instalador não será assinado**, e o SmartScreen vai avisar na primeira execução, com o nome do publicador como desconhecido. Isso vale para **qualquer** alternativa desta lista, menos a Store. As saídas são comprar um certificado OV/EV (ordem de US$ 200–400/ano) ou abrir o repositório com licença OSI e pedir um certificado gratuito à SignPath Foundation. Nenhuma das duas é decisão deste ADR; o que ele registra é que o aviso **vai** aparecer e não é defeito do empacotamento.

  Levantamento de assinatura (2026-10-29), feito a partir da pergunta "e se fosse no GitLab?":

  - **A hospedagem do repositório não entra na conta.** Quem emite confiança é uma autoridade certificadora, não a forja; GitHub e GitLab não têm programa de certificado. O que o repositório muda é só a *elegibilidade* a programas gratuitos de OSS.
  - **SignPath Foundation** exige licença aprovada pela OSI, nenhum componente proprietário e build verificável a partir do repositório. Os termos **não** restringem o host — GitHub aparece só como exemplo —, então no GitLab valeria igual. Não existe versão "gratuita para código fechado": o certificado é emitido em nome da própria SignPath Foundation justamente porque o código é auditável.
  - **Azure Artifact Signing** (ex-Trusted Signing), a opção gerenciada barata (Basic ~US$ 9,99/mês), serve código fechado e assina de qualquer CI — mas os certificados Public Trust são limitados por país: organizações nos EUA, Canadá, UE, Reino Unido, Austrália, Nova Zelândia, Japão, Coreia do Sul, Singapura, Suíça, Noruega e Israel, e **pessoas físicas só nos EUA e Canadá**. O Brasil não está em nenhuma das duas listas, e isso não depende de hospedagem nem de licença. (Brazil South aparece como região de *recurso* do serviço, o que é outra coisa.)
  - **Certificado OV/EV numa CA** é o que sobra para código fechado. Desde 2023 a chave tem de viver em hardware (regra do CA/Browser Forum), então assinar no CI implica um serviço de assinatura em nuvem da própria CA, não um `.pfx` guardado num secret.
  - **E mesmo pagando, o aviso pode continuar.** A reputação do SmartScreen é construída por downloads, e desde 2024 nem o EV dá confiança imediata. Para um app com um punhado de usuários, o certificado compra o **nome do publicador** na caixa de diálogo, não o desaparecimento dela.

  Conclusão prática: assinar não é decisão de CI nem de forja. Enquanto o repositório for privado e o projeto estiver no Brasil, o caminho é conviver com o aviso; se um dia o código abrir com licença OSI, a SignPath Foundation passa a ser possível — no GitHub ou no GitLab.
- **Tamanho, medido na implementação:** 254 MB de publish cru, 154 MB depois de descartar os `.pdb` nativos do SkiaSharp e do HarfBuzz (100 MB de símbolos de depuração que não servem a quem instala) e ~60 MB compactado, que é a ordem do download. O instalador e o workflow excluem `*.pdb` — as duas pontas, porque o `.zip` sai do mesmo publish. É o preço de não exigir o .NET 9 Desktop Runtime antes.
- Passa a existir uma **versão** no projeto, vinda da tag. Isso é bom além do instalador: o log e a tela podem passar a dizer qual versão está rodando, o que hoje não dá para saber.
- O primeiro uso continua tendo **dois passos**: instalar o Polyglotta e instalar o VB-Cable. O instalador deixa isso explícito em vez de falhar depois, quando o app não achasse "CABLE Input" — mas não é um clique só, e nenhuma escolha de empacotamento tornaria isso um clique só.
- `win-x64` apenas. ARM64 fica de fora até alguém precisar.
- A decisão é deliberadamente a mais barata que resolve o problema de hoje. Se o app ganhar usuários, o caminho esperado é **substituir** esta decisão pela do Velopack, não acumular as duas.
