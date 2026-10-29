using System.Reflection;

namespace Polyglotta.Host;

/// <summary>
/// A versão do agente em execução.
/// <para>
/// Vem da tag do Git no empacotamento (ADR-0011) e cai em <c>0.0.0-dev</c> quando o
/// binário veio de um <c>dotnet build</c> comum — o que é informação útil quando
/// alguém manda um log: diz se aquilo é uma versão publicada ou não.
/// </para>
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? "desconhecida";

        // O SDK anexa "+<commit>" quando o build tem informação de repositório.
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
