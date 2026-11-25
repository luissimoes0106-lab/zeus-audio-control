# ZEUS Gaming Mode e diagnóstico H510-PRO

A Beta 0.7.2 mantém a pasta de instalação, os perfis e a identidade visual aprovados. Abra **Gaming Mode** no **Intelligence Pack** ou no **Quick Control**.

## Reprodução e sessões

- **Gaming FPS** atenua subgraves e realça a presença. **Gaming Imersivo** reforça graves e ambiente. Ambos são pontos de partida e dependem do jogo, da mixagem e do fone.
- Para personalizar, ajuste o equalizador na aba Áudio e escolha **EQ atual** no Gaming Mode. A opção de associação guarda uma cópia desses ajustes. Perfis exportados continuam disponíveis no gerenciador de perfis anterior.
- No **Audio Mixer**, ajuste volume e mudo das sessões nativas da saída selecionada. Associe o executável do jogo ou selecione o executável de comunicação para rotular as linhas correspondentes. Se jogo e comunicação compartilham uma única sessão, o Windows não oferece controles independentes naquele endpoint.
- O limitador e a proteção configurável existentes permanecem na rota processada. O modo de baixa latência solicita 25 ms ao WASAPI. Use o diagnóstico de perdas e modo padrão se a rota ficar instável. O valor solicitado não mede latência física.

## Automação

A detecção por processo começa somente quando autorizada e limitada aos nomes dos executáveis associados. O ZEUS confirma o caminho para evitar aplicar um perfil a outro executável homônimo. Entre vários jogos, ganha a maior prioridade. Ao sair do jogo, a automação remove o EQ temporário e restabelece o Smart Profile ou EQ manual que estaria em uso. Uma escolha manual de Gaming Mode tem precedência até ser desativada. A troca usa a transição de EQ existente.

O ZEUS consulta a lista de processos e o caminho do executável por um handle Windows com acesso de consulta limitada. Não instala hooks nos jogos, injeta código, abre memória de jogos ou altera seus arquivos. Processos cujo caminho é protegido podem não ser detectados. Regras de anticheat variam entre títulos.

## Gaming Voice

Aplica o pipeline existente de passa-altas, gate, presença, Voice Shield, compressor e limitador, com ganho automático que eleva níveis de fala de forma limitada e deixa o ruído abaixo do limiar sem reforço. Ruído contínuo de baixa amplitude tende a ser mais fácil de atenuar. Cliques e digitação que se sobrepõem à fala podem permanecer audíveis. Reinicie uma rota de voz já ativa para carregar os parâmetros do novo perfil.

Discord, Steam e jogos podem selecionar a entrada de gravação de um cabo virtual compatível já instalado no Windows para usar a saída da rota de microfone. Aplicativos que recebem a mesma entrada virtual recebem o mesmo processamento. Volume de sessão controla reprodução; o Core Audio não dá ao ZEUS um ganho de captura independente por aplicativo nessa entrada.

## H510-PRO Wireless · receptor 040B:0897

A verificação pública comparou o VID/PID com USB IDs e a árvore do HeadsetControl em `bd56cf8`. As fontes consultadas não identificaram um protocolo de porcentagem para esse par. O VID consta como Weltrend em USB IDs; isso não identifica a semântica do PID, nem torna compatíveis os comandos de outros produtos. A busca pública de código do GitHub solicitou login. A presença de um relatório HID ou do uso padrão de bateria não confirma a escala nem a leitura do receptor.

Abra **Intelligence Pack → Diagnóstico H510-PRO USB/HID** com o dongle e o fone conectados. O relatório inclui contagem de nós PnP USB, coleções HID correspondentes, identificação publicada, páginas de uso, comprimentos de relatórios e presença declarada do uso padrão Battery Strength `0006:0020`. Abre somente metadados de descritor com acesso zero. Não lê relatórios de entrada, não solicita relatórios de recurso, não envia saídas e não altera firmware. O resultado pode ser salvo localmente, sem números de série ou caminhos de instância. A porcentagem via dongle fica indisponível até a confirmação no equipamento. O monitor BLE da Beta 0.7.1 continua disponível.

Referências de implementação

- [USB IDs, registro do VID](https://github.com/usbids/usbids/blob/master/usb.ids)
- [HeadsetControl, código-fonte inspecionado](https://github.com/Sapd/HeadsetControl/tree/bd56cf875b736ff91f69ebf17b519a79d0270564)
- [Linux, constante HID_DC_BATTERYSTRENGTH](https://github.com/torvalds/linux/blob/master/include/linux/hid.h)
- [Microsoft, metadados HID pré-interpretados](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getpreparseddata)
- [Microsoft, localizar capacidades de uso no descritor](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getspecificvaluecaps)
