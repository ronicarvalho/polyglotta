using Polyglotta.Core.Audio;
using Polyglotta.Host;
using Xunit;

namespace Polyglotta.Host.Tests;

/// <summary>
/// O que a tela pode oferecer. O cabo virtual não é escolha do usuário: o lado de
/// saída é destino fixo da tradução e o de entrada é o que a chamada escuta.
/// </summary>
public class SelectableDevicesTests
{
    private static readonly List<AudioDeviceInfo> Devices =
    [
        new("{mic-usb}", "Microphone (USB PnP Sound Device)", AudioDeviceKind.Capture, true),
        new("{cable-out}", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceKind.Capture, false),
        new("{fone}", "Speakers (USB PnP Sound Device)", AudioDeviceKind.Render, true),
        new("{hdmi}", "27HC5R (2- HD Audio Driver)", AudioDeviceKind.Render, false),
        new("{cable-in}", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceKind.Render, false)
    ];

    [Fact]
    public void Saida_nao_oferece_o_microfone_virtual()
    {
        var render = SessionPlanner.Selectable(new PolyglottaSettings(), AudioDeviceKind.Render, Devices);

        Assert.DoesNotContain(render, d => d.Id == "{cable-in}");
        Assert.Equal(["{fone}", "{hdmi}"], render.Select(d => d.Id));
    }

    [Fact]
    public void Entrada_nao_oferece_a_saida_do_cabo()
    {
        var capture = SessionPlanner.Selectable(new PolyglottaSettings(), AudioDeviceKind.Capture, Devices);

        Assert.DoesNotContain(capture, d => d.Id == "{cable-out}");
        Assert.Equal(["{mic-usb}"], capture.Select(d => d.Id));
    }

    [Fact]
    public void Nomes_do_cabo_vem_da_configuracao_nao_do_codigo()
    {
        // macOS/Linux: o mesmo dispositivo costuma ser os dois lados do cabo.
        var devices = new List<AudioDeviceInfo>
        {
            new("{mic}", "MacBook Pro Microphone", AudioDeviceKind.Capture, true),
            new("{black-in}", "BlackHole 2ch", AudioDeviceKind.Capture, false),
            new("{fone}", "MacBook Pro Speakers", AudioDeviceKind.Render, true),
            new("{black-out}", "BlackHole 2ch", AudioDeviceKind.Render, false)
        };
        var settings = new PolyglottaSettings
        {
            VirtualMicName = "BlackHole",
            VirtualMicCaptureName = "BlackHole"
        };

        Assert.Equal(["{fone}"],
            SessionPlanner.Selectable(settings, AudioDeviceKind.Render, devices).Select(d => d.Id));
        Assert.Equal(["{mic}"],
            SessionPlanner.Selectable(settings, AudioDeviceKind.Capture, devices).Select(d => d.Id));
    }

    [Fact]
    public void Sem_nome_de_cabo_configurado_nada_e_escondido()
    {
        // Um nome vazio não pode virar "esconde tudo": a tela ficaria sem opções.
        var settings = new PolyglottaSettings { VirtualMicName = "", VirtualMicCaptureName = "" };

        Assert.Equal(3, SessionPlanner.Selectable(settings, AudioDeviceKind.Render, Devices).Count);
        Assert.Equal(2, SessionPlanner.Selectable(settings, AudioDeviceKind.Capture, Devices).Count);
    }

    [Fact]
    public void Esconder_da_tela_nao_desliga_a_guarda()
    {
        // O CLI aceita qualquer nome, e configurações antigas podem trazer o cabo gravado.
        var settings = new PolyglottaSettings
        {
            Microphone = new DeviceSelection("CABLE Output", null),
            Loopback = new DeviceSelection("27HC5R", null),
            Phones = new DeviceSelection("Speakers", null)
        };

        var result = SessionPlanner.Prepare(settings, Devices);

        Assert.Null(result.Plan);
        Assert.Contains(result.Problems, p => p.Field == "mic");
    }
}
