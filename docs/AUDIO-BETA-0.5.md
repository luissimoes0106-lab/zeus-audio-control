# Arquitetura de áudio da Beta 0.5

O ZEUS usa WASAPI compartilhado. O caminho em tempo real abre um endpoint de captura verdadeiro, lê pacotes quando o sistema sinaliza o evento, executa o processamento em memória e grava em outro endpoint selecionado. As rotas funcionam sem acesso à internet e sem alterar dispositivos padrão ou instalar componentes de sistema. Uma rota pode ficar ativa por vez.

## Configuração

Use somente um cabo virtual de fornecedor confiável, com driver assinado, instalado separadamente. Verifique no Windows qual é a saída de reprodução do cabo e qual é a entrada de gravação correspondente. Dê aliases distintos a ambas quando necessário.

| Tipo de rota | Origem no ZEUS | Destino no ZEUS | Configuração externa |
|---|---|---|---|
| Áudio de aplicativos | Entrada de gravação do cabo | Fones ou alto-falantes físicos | Selecione a saída de reprodução do cabo no aplicativo ou no mixer do Windows |
| Microfone para aplicativos | Microfone físico | Saída de reprodução do cabo | Selecione a entrada de gravação do cabo no aplicativo que usa o microfone |
| Escuta local em fones | Microfone físico | Fones físicos | Comece com volume baixo. Não selecione alto-falantes |

Evite portas do mesmo cabo nas duas pontas de uma rota e desative qualquer função do Windows ou de outro aplicativo que devolva o áudio do destino à origem. O ZEUS solicita confirmação explícita, rejeita rotas entre aplicativos quando os dois endpoints declaram o mesmo identificador de dispositivo, e deixa a rota parada ao desconectar um endpoint. A escuta local exige que o Windows identifique a saída como fone ou headset e tem um limite de nível adicional. Um fone classificado pelo driver como alto-falante não fica disponível para essa escuta. A prevenção de microfonia depende também da escolha de fones e da configuração física. A perda de um endpoint para a rota e os controles de dispositivos voltam a encontrá-lo pelo ID. Reinicie o roteamento explicitamente após conferir a configuração.

## Processamento e medidas

- **Saída**. EQ de 10 bandas, controles de tonalidade, bass boost, compressor e normalizador opcionais, e limitador. O EQ troca ganhos na fronteira de um bloco e mantém filtros separados por canal. A curva representa os ganhos configurados nas bandas.
- **Voz**. Passa-altas, presença, ganho, gate com ataque, sustentação e liberação, compressor e limitador. O estimador de piso aprende sinais de baixa amplitude e reduz sua contribuição quando a entrada é próxima desse piso. Um ventilador forte ou fala misturada a ruído podem permanecer audíveis.
- **Espectro**. FFT de 2048 amostras com janela Hann do primeiro canal da saída. O cálculo da tela ocorre somente quando o painel de rotas está visível. O espectro visualizado não é usado para reduzir ruído.
- **Latência**. A fila é limitada a 100 ms, com meta de preenchimento inicial de 25 ms. A saída solicita 40 ms ao WASAPI. A interface separa a ocupação da fila, o valor reportado pelo cliente de captura e a solicitação de saída. Esses números não medem latência ponta a ponta. Falta de dados insere silêncio. Sobrecarga descarta o atraso acumulado. Para medir latência real, envie um impulso conhecido e meça a diferença até o retorno gravado no equipamento.
- **Redução espectral futura**. Uma implementação por STFT pode estimar o ruído de cada bin em janelas sem fala, aplicar máscara suave e reconstruir com overlap-add. Deve-se medir atraso adicional, ruído musical e perda de fala com gravações reais antes de ativar esse método. A beta usa redução de ruído por envelope.

## APO e formato de canais

Uma APO de efeito de sistema precisa ser registrada e vinculada aos endpoints por um pacote de driver compatível. Nem todos os drivers permitem substituir a cadeia de efeitos. Seriam necessários um pacote assinado e validações do fornecedor, do Windows e do hardware. O caminho por endpoints virtuais evita alterar a cadeia do driver físico. WASAPI loopback apenas observa o áudio já misturado e não redireciona o áudio dos aplicativos. [Documentação de APOs do Windows](https://learn.microsoft.com/windows-hardware/drivers/audio/windows-audio-processing-objects)

O H510 Pro do proprietário informou estéreo a 48.000 Hz. O teste de canais endereça os canais anunciados pela configuração atual. Uma máscara multicanal anuncia o formato aceito pelo Windows, sem comprovar transdutores individuais. A espacialização local de WAV 5.1/7.1 compatível produz estéreo com mistura e atrasos curtos. Ela não altera o formato do Windows. A beta não afirma 7.1 nativo no H510 Pro e não modifica firmware.

## Validação em equipamento

Teste o caminho completo com o cabo virtual e diferentes aplicativos, primeiro uma rota por vez. Verifique os canais com fones e volume baixo, compare a fala e um ruído constante em gravações equivalentes, remova e reconecte o dispositivo enquanto o áudio flui e observe os contadores. Para caracterizar estabilidade, registre CPU, RAM e perdas durante uma sessão longa. O teste de interface no CI usa três tamanhos de janela. Escalas DPI, qualidade acústica e latência física requerem verificação no computador de destino.
