# ADR-0009 — Credenciais guardadas pela aplicação, cifradas pelo sistema operacional

**Status:** Aceito · **Data:** 2026-10-28

## Contexto

O PRD-001 exigia, desde a v0, "chaves só via variável de ambiente". A regra fazia sentido para um CLI operado por quem escreveu o comando: a chave nunca encosta no disco do projeto e não vaza num `git add` distraído.

O ADR-0007 muda a forma de execução: o Polyglotta passa a ser um *background worker* que sobe no logon, com tela de configuração. Variável de ambiente envelhece mal nesse formato:

- Uma variável de usuário definida com `setx` só é vista por processos criados depois, o que na prática exige logoff — justo no app cujo objetivo é subir no logon.
- A tela de configuração não teria como gravar a chave, então "configurar o Polyglotta" ficaria dividido entre uma tela e um terminal. Isso contradiz a meta de "setup < 2 min com UI" do PRD-001.
- O usuário passa a ter duas chaves (Azure e Gemini — ADR-0006) e pode querer alternar entre provedores sem mexer no ambiente.

## Decisão

As credenciais passam a poder ser **guardadas pela aplicação, cifradas em repouso pelo cofre do sistema operacional**, com resolução em cadeia:

1. **Variável de ambiente**, se existir — tem precedência.
2. **Credencial salva**, caso contrário.

A precedência da variável é deliberada: mantém funcionando tudo o que já existe (os comandos do README, execuções de teste, um eventual CI) e dá uma saída de emergência que não depende da UI.

A abstração é `ICredentialStore` em `Polyglotta.Core.Configuration` — `Read(name)`, `Save(name, value)`, `Delete(name)` — com três implementações:

| Implementação | Papel |
|---|---|
| `EnvironmentCredentialStore` | só leitura, reaproveita `EnvironmentSettings` (inclusive a limpeza de aspas que nos custou uma falha de DNS em 2026-09-22) |
| `DpapiCredentialStore` | Windows; cifra com DPAPI no escopo `CurrentUser` |
| `ChainedCredentialStore` | compõe os dois na ordem acima; grava sempre no segundo |

A implementação do Windows vive num projeto novo, `src/Polyglotta.Security.Windows/`, pelo mesmo motivo que o áudio vive em `Polyglotta.Audio.Windows` (ADR-0004): é código de plataforma, e o Core não pode referenciá-lo. Ela traz a dependência externa **`System.Security.Cryptography.ProtectedData` 10.0.14** — no .NET, `ProtectedData` não está no runtime base, vem desse pacote, e ele lança `PlatformNotSupportedException` fora do Windows.

O arquivo fica em `%APPDATA%\Polyglotta\credentials.json`, separado do `config.json` (ADR-0007): é um mapa de nome → blob DPAPI em base64. Separar os dois permite copiar, versionar ou apoiar a configuração sem arrastar segredo junto.

Quando chegarmos a macOS e Linux, entram `KeychainCredentialStore` e `SecretServiceCredentialStore` na mesma interface. Até lá, nessas plataformas sobra a variável de ambiente — que continua funcionando.

Regras que a implementação deve respeitar:

- A chave **nunca** é logada, nem em `Debug`. A tela e o CLI mostram no máximo "configurada" e os quatro últimos caracteres.
- Nenhuma chave entra em mensagem de exceção.
- O arquivo é escrito com gravação atômica (arquivo temporário + `File.Move` com sobrescrita), para que uma queda no meio não deixe o usuário sem credencial.

## Alternativas consideradas

- **Manter só variável de ambiente e a tela apenas indicar se a chave foi encontrada** — rejeitada. Era a opção (a) do ADR-0007. Preserva o RNF original sem mudança, mas mantém o setup partido entre tela e terminal e esbarra no problema do `setx` exigir logoff. Foi o caminho descartado explicitamente na aceitação.
- **Gravar a chave em texto puro no `config.json`** — rejeitada. É o que a maioria dos utilitários pequenos faz e é indefensável quando o SO já oferece o cofre de graça.
- **Senha-mestra do próprio Polyglotta para cifrar o arquivo** — rejeitada. Adiciona uma senha a digitar no logon, que é exatamente o atrito que este ADR existe para remover, e não melhora o modelo de ameaça real (ver consequências).
- **Windows Credential Manager (`CredRead`/`CredWrite`) em vez de DPAPI** — considerada e adiada. Dá gestão visível no painel do Windows, mas exige P/Invoke manual e tem limite de tamanho por entrada; DPAPI com arquivo próprio é mais simples e usa o mesmo mecanismo de proteção por baixo. Fácil de trocar depois: é uma implementação da interface.

## Consequências

- O RNF de segurança do PRD-001 muda de "chaves só via variável de ambiente" para "chaves nunca em texto puro no disco; variável de ambiente ou cofre do SO". O PRD é atualizado no mesmo commit.
- **O que o DPAPI protege, e o que não protege.** No escopo `CurrentUser`, o blob só é decifrável pelo mesmo usuário na mesma máquina: copiar o arquivo para outro computador, ou lê-lo de outra conta, não serve de nada. Ele **não** protege contra código rodando como você — qualquer processo seu pode chamar `Unprotect`. É a mesma garantia que o Chrome dá aos cookies salvos, e é a garantia correta para esta classe de app; dizer mais do que isso seria vender proteção que não existe.
- Passa a existir um lugar no disco com segredo do usuário. Entra na documentação, e a tela de configuração precisa de um "remover credencial" — esquecer isso é como esquecer o logout.
- `GeminiSpeechTranslator.FromEnvironment()` e `AzureSpeechTranslator.FromEnvironment()` deixam de ser o único caminho: passam a receber o `ICredentialStore`. Os nomes das variáveis não mudam.
