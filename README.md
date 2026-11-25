# ZEUS Audio Control Beta 0.7.3

Aplicativo offline, gratuito e de código aberto para controlar e processar áudio no Windows. A Beta 0.7 inclui um painel de dispositivos, equalizador de dez bandas, controles de voz, rotas WASAPI e diagnóstico de latência. Licença MIT.

## Instalação e recuperação

Requer Windows 10 ou 11 x64. O instalador é por usuário. A versão portátil é um ZIP com o executável e o runtime .NET incluído. Os executáveis da beta não têm assinatura Authenticode. Confira o SHA-256 da distribuição e mantenha as proteções do Windows ativas.

A Beta 0.7 usa o atalho **ZEUS Audio Control** e uma pasta de instalação própria. No primeiro uso, ela copia os dados da Beta 0.5, com fallback para a Beta 0.4, para `%LOCALAPPDATA%\ZeusAudioControl\Beta07`. A Beta 0.4 permanece disponível no [pré-lançamento v0.4.0-beta.1](https://github.com/luissimoes0106-lab/zeus-audio-control/releases/tag/v0.4.0-beta.1) ou na [execução que gerou seus pacotes](https://github.com/luissimoes0106-lab/zeus-audio-control/actions/runs/45536265411).

## Uso

- **Visão geral** mostra os dispositivos selecionados, volumes, nível do microfone, formato de saída, perfis, filtros e uso de CPU e RAM.
- **Áudio** controla os endpoints do Windows. Ajuste o EQ pelos controles ou arraste a curva. Os efeitos também se aplicam a arquivos tocados no ZEUS. Para WAV com máscara de canais compatível, ative a espacialização estéreo antes de abrir o arquivo.
- **Microfone** inclui controle de ganho, redução adaptativa de ruído constante, filtro passa-altas, presença, gate com ataque/sustentação/liberação, compressor e limitador. Os perfis Comunicação, Studio, Gaming e Gravação podem ser ajustados e exportados.
- **Rotas** encaminha áudio em tempo real, exibe a forma de onda original e de saída, o espectro e os contadores. A escuta local deve usar fones e começar com volume baixo. Para aplicar mudanças de voz ou de dinâmica a uma rota em andamento, pare e reinicie a rota. Os ajustes de EQ de saída são aplicados durante a reprodução.
- **Configurações** permite mudar o idioma e o modo simples/avançado, salvar aliases, importar e exportar perfis e restaurar padrões.

A opção **Áudio de aplicativos** requer um cabo virtual confiável já instalado e escolhido pelos aplicativos ou pelo Windows. A opção **Microfone para aplicativos** usa a entrada virtual desse cabo para disponibilizar o microfone processado. O ZEUS não instala drivers nem troca dispositivos padrão. Veja a [arquitetura de áudio, configuração e limites](docs/AUDIO-BETA-0.5.md).

## Desenvolvimento e distribuição

```powershell
dotnet run --project tests/ZeusAudioControl.Tests -c Release
dotnet build src/ZeusAudioControl -c Release -r win-x64
dotnet run --project src/ZeusAudioControl
```

O GitHub Actions testa e compila em Windows, gera instalador/ZIP/hashes, valida a interface em tamanhos diferentes e testa instalação e remoção no runner Windows. Tags `v0.7.3-beta*` preparam um pré-lançamento em rascunho.

**Sobre o ZEUS** abre a verificação de versões públicas no GitHub quando solicitada. O download e a execução do instalador são manuais. Os controles de áudio continuam disponíveis offline.

Consulte a [matriz operacional da Beta 0.7](docs/BETA-0.7-OPERACIONAL.md) para usar rotas simultâneas, Smart Volume, Voice Shield e recuperação de atualizações.

A Beta 0.7.1 acrescenta o [ZEUS Intelligence Pack](docs/BETA-0.7.1-INTELLIGENCE.md) e usa a mesma pasta e os mesmos perfis da Beta 0.7.

A Beta 0.7.2 integra o [ZEUS Gaming Mode e o diagnóstico seguro do H510-PRO Wireless](docs/BETA-0.7.2-GAMING.md).

A Beta 0.7.3 integra Smart Voice, calibração avançada, páginas permanentes de Mixer e Perfis, Audio Doctor revisto e a captura passiva opcional do H510-PRO. Veja a [auditoria e os limites técnicos](docs/BETA-0.7.3.md).
