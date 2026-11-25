# Beta 0.7 · matriz operacional

O escopo abaixo cobre as prioridades disponíveis na solicitação. O comando original com os 12 módulos não consta dos anexos nem do repositório disponível.

| Requisito identificado | Implementado | Verificação | Limite |
|---|---|---|---|
| Identidade oficial | ICO, PNG e tema aprovados preservados | Comparação com `2e2b030` | — |
| Inteligência operacional | Diagnóstico de filas, interrupções, contagem de rotas e indicação de retorno ao modo padrão | Teste da fila e build x64 | Recomendações baseadas em métricas locais, sem alterar o Windows |
| Host e latência | Modo experimental com solicitação de 25 ms, fila de 65 ms e preenchimento inicial de 15 ms | Testes de fila e pacote | Latência ponta a ponta e comportamento de drivers exigem hardware |
| CPU e RAM | Captura de forma de onda apenas na rota exibida. Janela Hann calculada uma vez. Menor fila no modo experimental | Build x64 e testes de regressão | Ganho percentual depende do equipamento e precisa ser medido |
| Smart Volume | Ganho suave e limitado, com retorno ao nível neutro perto do silêncio. Limitador existente mantido | Teste sintético de dinâmica | Calibração acústica não medida |
| Voice Shield | Piso adaptativo reforçado no pipeline de voz existente, opção salva no perfil | Teste de ruído estacionário e compatibilidade de perfil | Fala real e ventiladores precisam de teste físico |
| Rotas simultâneas | Uma rota de reprodução e uma de voz, com rejeição de topologia desconhecida e cabo reutilizado. Escuta local exclusiva | Teste de política de topologia | Requer cabos com identificadores distintos e reconhecidos, além de teste com dispositivos |
| Quick Control | Volume e mudo de saída e microfone preservados. Acesso direto às rotas e parada geral | Build e smoke da interface em CI | Operação com endpoints físicos precisa de teste |
| Atualizador | Download HTTPS de lançamento público, SHA-256 antes de disponibilizar instalador, cópia do executável e perfis, recuperação com verificação | Testes de arquivo, adulteração e recuperação | Execução do instalador manual. SHA-256 publicado na mesma origem verifica integridade, sem substituir assinatura Authenticode |
| Estabilidade e compatibilidade | Identidade por endpoint, encerramento de rota afetada por desconexão e preservação da rota independente | Testes de regressão, Windows x64 em CI | Windows 10/11 e reconexão simultânea precisam de hardware |
| Demais módulos do comando original | Escopo indisponível | — | Necessária a especificação original para identificar lacunas |

## Uso

Em **Rotas**, inicie a reprodução com o primeiro cabo. Troque o tipo para microfone e configure o segundo cabo. O ZEUS rejeita o segundo início se os identificadores necessários estiverem ausentes ou forem incompatíveis. As rotas permanecem ativas ao trocar o tipo. Use **Parar** para encerrar a rota selecionada ou **Parar todas as rotas** no Quick Control. Cabos com loop fora do ZEUS exigem configuração correta em cada aplicativo.

A opção de baixa latência reduz os tamanhos solicitados. Se aparecerem interrupções repetidas, pare a rota e reinicie no modo padrão. A ocupação da fila e o pedido ao WASAPI não representam latência de microfone a alto-falante medida.

Em **Sobre → Verificar atualizações**, selecione **Baixar e verificar instalador** quando houver uma versão pública. O pacote fica indisponível se o manifesto estiver ausente, duplicado ou se o hash divergir. A pasta do pacote inclui o executável e as configurações de recuperação em **Backup**. Feche a aplicação antes de executar o instalador. **Recuperar cópia anterior** valida e copia os dados para uma pasta separada. Para reaplicar os JSON locais, feche o ZEUS e copie os arquivos recuperados para a pasta de perfil indicada no README.
