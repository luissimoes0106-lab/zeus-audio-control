# ZEUS Audio Control Beta 0.4

Aplicativo gratuito e de código aberto para controlar dispositivos de áudio do Windows, incluindo o Redragon Zeus H510 Pro. Funciona offline, sem cadastro, anúncios ou telemetria. Licenciado sob MIT.

## Instalação

Requer Windows 10 ou 11 x64 e um dispositivo reconhecido pelo Windows. Os pacotes incluem o runtime do .NET.

- **Instalador**. Execute o arquivo `Setup-x64.exe` para instalar na sua conta, com atalhos e entrada em Aplicativos instalados. Para remover o programa, use essa entrada. Preferências em `%LOCALAPPDATA%\ZeusAudioControl` são preservadas.
- **Portátil**. Extraia o ZIP e abra `ZeusAudioControl.exe`. Nessa modalidade, as preferências também ficam em `%LOCALAPPDATA%\ZeusAudioControl`.

**Assinatura digital**. Os binários da beta estão sem assinatura Authenticode. Compare os SHA-256 com `SHA256SUMS.txt` da mesma distribuição. Mantenha as proteções do Windows ativas.

## Uso

- **Áudio**. Selecione uma saída e ajuste volume, mudo e balanço estéreo quando o endpoint permitir. Use o teste esquerdo/direito com volume baixo. Para testar o EQ de 10 bandas, Bass Boost, controle de tonalidade e limitador local, abra um arquivo de áudio. Os ajustes do EQ são aplicados durante a reprodução. Tanto o EQ quanto a reprodução de teste de microfone usam a saída padrão do Windows.
- **Microfone**. Escolha a entrada, confira o nível e grave até cinco segundos para ver a forma de onda e ouvir a amostra. Use a opção de comparação para ouvir a mesma gravação sem filtros. Ganho, passa-altas, presença, gate, compressor, supressão de ruído e limitador são processados localmente. A gravação temporária é removida ao fechar o aplicativo normalmente.
- **Configurações**. Alterne entre português e inglês e entre controles simples e avançados. Exporte ou importe perfis JSON de EQ e voz. Também é possível importar o formato anterior, que contém apenas o perfil de voz. Gerencie a inicialização e a bandeja. A opção de restaurar configurações também limpa aliases e volta à seleção automática de dispositivos.
- **Dispositivos**. Use aliases para identificar endpoints. Alterações de disponibilidade são consultadas periodicamente e o dispositivo preferido é restaurado quando retorna com o mesmo ID. Veja canais e taxa de amostragem da saída nas informações de diagnóstico.

## Limitações da beta

O teste de voz e o EQ operam no próprio aplicativo. O volume, mudo e balanço são controles do endpoint do Windows. A supressão convencional pode atenuar sons de fundo baixos e estáveis. O resultado depende do ruído e da entrada, e não há separação de voz.

O formato de mixagem anunciado pelo Windows permite identificar uma máscara de canais 7.1. A configuração do driver, a saída física e a virtualização precisam ser verificadas no painel de som do Windows e no dispositivo. O ZEUS não emula surround.

O Windows permite efeitos globais via **Audio Processing Objects** instalados e associados aos endpoints por um pacote compatível. Outra opção para voz seria um endpoint virtual assinado e selecionado no Windows. WASAPI loopback captura a mixagem existente e não substitui o fluxo para outros aplicativos. A beta não instala APOs ou drivers virtuais. [Documentação do Windows sobre APOs](https://learn.microsoft.com/windows-hardware/drivers/audio/windows-audio-processing-objects)

## Desenvolvimento e distribuição

Em Windows, com o SDK .NET 8:

```powershell
dotnet run --project tests/ZeusAudioControl.Tests -c Release
dotnet build src/ZeusAudioControl -c Release -r win-x64
dotnet run --project src/ZeusAudioControl
```

O workflow **Windows build** compila em Windows, gera o ZIP, o instalador NSIS e os hashes como artefatos, e verifica instalação e remoção em um runner Windows. Tags `v0.4.0-beta*` preparam um pré-lançamento em rascunho no GitHub. Publique o rascunho no GitHub após validar os pacotes. Para o site de downloads, configure o GitHub Pages com origem GitHub Actions e execute o workflow **Download site** a partir de `main`.

Em Windows, valide os atalhos, a bandeja, instalação e remoção como usuário comum, troca de idioma, seleção de dispositivos, desconexão e reconexão de USB/Bluetooth, gravação e reprodução repetidas e estabilidade de CPU/memória. Verifique o balanceamento e o surround com os canais físicos.
