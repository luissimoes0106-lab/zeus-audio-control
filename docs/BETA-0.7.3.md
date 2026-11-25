# ZEUS Audio Control Beta 0.7.3

## H510-PRO Wireless · 040B:0897

O proprietário verificou revisão `0x002A`, três nós USB PnP e duas coleções HID. A coleção `000C:0001` expõe entrada de 3 bytes e é descartada. A coleção `F100:0000` expõe entrada de 13 bytes, saída de 65 bytes e nenhum relatório de recurso. O uso padronizado Battery Strength `0006:0020` não foi anunciado.

Em **Intelligence Pack → Diagnóstico H510-PRO USB/HID**, marque a autorização e inicie a captura. A ação valida VID, PID, revisão, página, uso e tamanhos antes de abrir a coleção. O handle é assíncrono, somente leitura e com compartilhamento de leitura e escrita no Windows, sem acesso exclusivo. A captura termina após 20 segundos, 256 relatórios, cancelamento, fim do fluxo ou erro. O TXT exportado contém horário e bytes de entrada espontâneos, sem incluir caminho de instância ou número de série. O aplicativo não abre a coleção de mídia para leitura, não chama `WriteFile`, `SetOutputReport`, `SetFeature` ou `GetFeature`, e não envia comandos USB proprietários. Se o sistema ou o software oficial impedir acesso compartilhado, feche a captura e mantenha o aplicativo oficial em uso.

A revisão pública da Beta 0.7.2, a pesquisa adicional por `F100` e `0897` e o relatório de hardware não confirmaram a semântica de um campo de bateria. As páginas da Redragon retornaram bloqueio de rede no ambiente de trabalho. O binário oficial e um tráfego correlacionado com estados de carga não estavam disponíveis para validar o protocolo. Um fluxo vazio pode significar que os dados só são enviados após consulta do software oficial. Nenhum byte é convertido em porcentagem. Uma implementação futura deve correlacionar o mesmo campo com leituras independentes em diferentes estados de carga, confirmar revisão e escala e preservar a disponibilidade do áudio. O monitor de bateria BLE existente continua independente.

## Smart Voice

A aba permanente **Smart Voice** lista os presets WhatsApp, Google Meet, OBS, Discord, Gaming Voice e Personalizado. Presets e automação preservam a quantidade atual de supressão adaptativa, inclusive 18%, além da opção de Voice Shield e do teto preexistentes. Os ajustes detalhados continuam em **Microfone**. Medidores na página mostram picos digitais da entrada e da saída da rota do microfone. Com a rota inativa, os medidores indicam indisponibilidade.

A automação começa somente se autorizada e com uma rota de microfone já ativa. O maior valor de prioridade entre os executáveis associados e em execução ganha. Empates usam ordem de caminho para manter resultado determinístico. A automação usa o pipeline de voz existente e transição de 50 ms, sem iniciar a rota, mudar dispositivo padrão ou desmutar o microfone. Ao sair o aplicativo, o perfil manual volta. Para o navegador, a associação se refere ao executável escolhido, sem identificar a aba ou site em uso. Isso também vale para clientes web do WhatsApp ou Google Meet. Todos os aplicativos que usam a mesma entrada virtual recebem o mesmo sinal processado. O Mixer do Windows controla sessões de reprodução, sem criar um controle separado de captura para cada cliente.

A calibração usa quatro gravações locais de cinco segundos: ambiente, fala baixa, normal e alta. Mostra RMS e pico em dBFS, calcula ganho, gate, limiar do compressor e teto do limitador, e permite ouvir a mesma amostra bruta e com a recomendação. Aplicação requer clique explícito. Um perfil anterior fica disponível para restauração, e os WAV temporários são removidos ao fechar normalmente. Clipping ou separação insuficiente interrompem a recomendação, sem alterar a configuração.

## Audio Doctor e auditoria

| Item | Resultado da auditoria |
|---|---|
| Gaming Mode | FPS, Imersivo, EQ personalizado, associações, prioridade e retorno ao perfil anterior preservados. Baixa latência continua sendo uma solicitação ao WASAPI. |
| Audio Mixer | Controles de volume/mudo de sessões existentes reaproveitados na navegação principal e no Quick Control. Separação jogo/comunicação exige sessões distintas no endpoint. |
| Meus Perfis | Página permanente para criar, carregar e editar pelo fluxo carregar → ajustar → salvar com confirmação. Armazena snapshots locais de EQ e voz no formato de perfil existente. Importação e exportação anteriores continuam. |
| Voz por aplicativo | Associação à rota controlada e prioridade configurável. Independência por aplicativo requer entradas/rotas independentes; uma entrada virtual compartilhada tem um único processamento. |
| Seleção de dispositivos | Audio Doctor consulta seleção atual, preferência salva e padrões de multimídia e comunicação do Windows separadamente. IDs diferentes não são tratados como desconexão. A ação de salvar as seleções atualiza apenas as preferências do ZEUS. |
| Identidade, proteção, bateria BLE e atalhos | Mantidos; os ajustes da Beta 0.7.2 continuam na mesma pasta de perfil. |

## Validação

Os testes sintéticos cobrem o filtro de identidade HID, a conservação de 18% de supressão, prioridade de associação, rejeição de saturação e transição do limitador. O CI Windows valida abertura e layout das páginas, instalador, desinstalador e inspeção HID sem receptor. Captura e cancelamento com o receptor, correspondência de bytes com a bateria, qualidade acústica real e rotas compartilhadas com clientes de voz precisam de teste físico. A saída do CI não representa medições de latência ou bateria do H510-PRO.
