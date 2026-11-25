namespace ZeusAudioControl;

public partial class MainWindow
{
    private string _page = "Visão geral";
    private bool _english;
    private static readonly (string Portuguese, string English)[] Labels =
    [
        ("◈  Visão geral", "◈  Overview"),
        ("♫  Áudio", "♫  Audio"),
        ("◉  Microfone", "◉  Microphone"),
        ("⚙  Configurações", "⚙  Settings"),
        ("Central universal de áudio  •  Windows  •  Beta", "Windows audio controls"),
        ("Dispositivos", "Devices"),
        ("Controles nativos do Windows e laboratório de voz local.", "Windows endpoint controls and local microphone processing."),
        ("Atualizar dispositivos", "Refresh devices"),
        ("Áudio de saída", "Audio output"),
        ("Balanço estéreo (quando suportado)", "Stereo balance (if supported)"),
        ("Equalizador", "Equalizer"),
        ("Aplicado ao player local e às rotas de áudio de aplicativos.", "Applies to local playback and to app-audio routes."),
        ("Graves", "Low"),
        ("Médios", "Mid"),
        ("Agudos", "High"),
        ("Personalizado", "Custom"),
        ("Música", "Music"),
        ("Filmes", "Movies"),
        ("Abrir áudio e testar", "Open audio file"),
        ("Parar", "Stop"),
        ("Testar esquerda", "Test left"),
        ("Testar direita", "Test right"),
        ("Apelido da saída", "Output alias"),
        ("Salvar", "Save"),
        ("Volume de saída", "Output volume"),
        ("Silenciar saída", "Mute output"),
        ("Microfone de entrada", "Microphone input"),
        ("Apelido do microfone", "Microphone alias"),
        ("Volume do microfone", "Microphone volume"),
        ("Silenciar microfone", "Mute microphone"),
        ("Intensidade do microfone", "Microphone level"),
        ("Ganho digital do microfone", "Digital microphone gain"),
        ("VOICE STUDIO  /  TESTE LOCAL", "Voice studio"),
        ("Comunicação", "Communication"),
        ("Gravação", "Recording"),
        ("Salvar perfil", "Save profile"),
        ("Ativar efeitos Studio", "Enable studio effects"),
        ("Filtro de graves (80 Hz)", "High-pass filter (80 Hz)"),
        ("Supressão de ruído adaptativa (local)", "Adaptive noise reduction (local)"),
        ("Atenua ruído de fundo baixo e constante no teste e nas rotas de microfone.", "Reduces steady low-level background noise in tests and microphone routes."),
        ("Noise Gate (reduz sons baixos)", "Noise gate"),
        ("Limiar do gate (dBFS)", "Gate threshold (dBFS)"),
        ("Compressor de voz", "Voice compressor"),
        ("Presença da voz (dB)", "Voice presence (dB)"),
        ("Compare o mesmo teste antes e depois. Em tempo real, abra Rotas.", "Compare the same recording before and after processing. For live monitoring, open Routes."),
        ("Comparar: ouvir áudio original (sem efeitos)", "Compare with original recording"),
        ("Gravar teste (5 s)", "Record test (5 s)"),
        ("Ouvir teste", "Play test"),
        ("Modo avançado", "Advanced mode"),
        ("Minimizar para a bandeja do Windows", "Minimize to the Windows tray"),
        ("Iniciar com Windows", "Start with Windows"),
        ("Exportar perfil", "Export profile"),
        ("Importar perfil", "Import profile"),
        ("Restaurar perfis padrão", "Restore default profiles"),
        ("Configurações locais", "Local settings"),
        ("Salvar seleção de dispositivos", "Save device selection"),
        ("A opção Rotas encaminha áudio entre endpoints. O ZEUS não altera os dispositivos padrão do Windows.", "Routes forward audio between endpoints. ZEUS leaves Windows default devices unchanged."),
        ("Desempenho", "Performance"),
        ("Centro", "Center"),
        ("Controles via Windows Core Audio. Sem alterações no driver ou firmware.", "Windows Core Audio controls. Driver and firmware stay intact."),
        ("Restaurar configurações padrão", "Restore default settings")
    ];

    private static readonly Dictionary<string, string> Messages = new()
    {
        ["Visão geral"] = "Overview",
        ["Rotas"] = "Routes",
        ["Meus Perfis"] = "My Profiles",
        ["Áudio"] = "Audio",
        ["Microfone"] = "Microphone",
        ["Configurações"] = "Settings",
        ["Abrir ZEUS"] = "Open ZEUS",
        ["Sair"] = "Exit",
        ["Centro"] = "Center",
        ["Selecione um dispositivo de saída"] = "Select an audio output",
        ["Selecione um microfone"] = "Select a microphone",
        ["Selecione uma saída para verificar o formato."] = "Select an output to inspect its mix format.",
        ["Formato 7.1 anunciado pelo Windows. Confirme a saída física no teste de alto-falantes do Windows."] = "Windows reports a 7.1 format. Use the Windows speaker test to verify physical output.",
        ["H510 Pro detectado nas interfaces de áudio do Windows."] = "H510 Pro detected by Windows audio endpoints.",
        ["Modo universal: selecione qualquer dispositivo de áudio ativo."] = "Select any active Windows audio endpoint.",
        ["Não foi possível enumerar os dispositivos de áudio."] = "Could not enumerate audio devices.",
        ["Dispositivo detectado. Os controles abaixo atuam nos endpoints selecionados, sem mudar o driver."] = "Controls affect the selected Windows endpoints.",
        ["Modo universal. Confira os dispositivos selecionados antes de ajustar."] = "Check the selected devices before adjusting levels.",
        ["Reprodução parada."] = "Playback stopped.",
        ["Reproduzindo na saída padrão. Ajustes aplicados em tempo real."] = "Playing on the default output. EQ changes apply in real time.",
        ["Gravando localmente por 5 segundos..."] = "Recording locally for 5 seconds…",
        ["Teste gravado. Ouça com volume baixo. Reprodução pela saída padrão do Windows."] = "Test recorded. Listen at low volume on the default Windows output.",
        ["Seleção salva neste computador."] = "Device selection saved on this computer.",
        ["Perfil personalizado salvo."] = "Custom profile saved.",
        ["Perfil aplicado ao teste local. Ajuste conforme sua voz e salve se desejar."] = "Profile applied to local playback.",
        ["Apelido removido."] = "Alias removed.",
        ["Use até 80 caracteres."] = "Use up to 80 characters.",
        ["Disponível apenas em saídas estéreo."] = "Available on stereo outputs.",
        ["Controles via Windows Core Audio. Sem alterações no driver ou firmware."] = "Windows Core Audio controls. Driver and firmware stay intact.",
        ["Procurando dispositivos..."] = "Looking for devices…",
    };

    private string T(string portuguese) => _english ? Messages.GetValueOrDefault(portuguese, portuguese) : portuguese;

    private void ApplyLanguage(bool english)
    {
        _english = english;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(english ? "en-US" : "pt-BR");
        for (var i = 0; i < Labels.Length; i++)
            Resources["Label" + i] = english ? Labels[i].English : Labels[i].Portuguese;
        PageTitle.Text = T(_page);
        UpdateRouteLabels();
        Resources["ProfilesNavigation"] = L("Meus Perfis", "My Profiles");
        Resources["VoiceLimiterLabel"] = L("Teto digital do limitador de voz", "Voice limiter digital ceiling");
        Resources["AboutZeus"] = L("Sobre o ZEUS", "About ZEUS");
        if (_tray?.ContextMenuStrip is { } menu)
        {
            menu.Items[0].Text = T("Abrir ZEUS");
            menu.Items[1].Text = T("Sair");
        }
        UpdateDeviceStatus();
        try { SurroundStatus.Text = DescribeOutput(); }
        catch (Exception error) { SurroundStatus.Text = error.Message; }
    }
}
