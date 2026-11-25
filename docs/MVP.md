# ZEUS Audio Control — MVP

## Status

Protótipo Windows WPF + .NET 8 + NAudio. Ainda não compilado nem testado em hardware real.

### Incluído
- Lista de endpoints ativos de saída e entrada do Windows
- Identificação por nome contendo H510-PRO/H510 PRO
- Volume/mute de saída e volume de microfone
- Nenhuma substituição de driver ou alteração de firmware

### Fora deste MVP
Equalizador, efeitos 7.1, bateria e cancelamento de ruído. Exigem implementação e testes separados.

## Como testar no Windows
1. Instale .NET 8 SDK.
2. Execute `dotnet run --project src/ZeusAudioControl/ZeusAudioControl.csproj`.
3. Conecte o receptor USB do H510 Pro.
4. Clique em Atualizar dispositivos.
5. Verifique se o endpoint do H510 PRO foi selecionado.
6. Com volume baixo, teste volume, mudo e microfone.

## Identificador de referência
`USB\VID_040B&PID_0897&MI_00` foi fornecido em captura de tela. O MVP não consulta IDs USB diretamente.

## Segurança
O aplicativo não grava áudio, não requer administrador e não realiza conexões de rede.
