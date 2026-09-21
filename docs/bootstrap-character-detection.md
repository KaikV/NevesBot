# Inicialização e detecção de personagem

O KBot abre o launcher configurado, encontra `PokeAlliance_dx.exe` sem iniciar outra instância, obtém PID/HWND, cria `GameSession` e vincula o processo ao núcleo pelo Named Pipe. A Dashboard só aparece quando `CharacterSession.IsInGame` é verdadeiro. Ao sair do mapa, a Dashboard fecha e o Bootstrap volta sem encerrar o jogo nem destruir a sessão. Se o processo fecha, o estado é `Disconnected`.

## ClientReader

`ClientReader` não tem `AddressResolver`, perfil de endereços confirmado nem leitura de estado. Antes, `GET_STATUS` escrevia `NOT_CONFIGURED` literalmente e `GET_READER_STATUS` criava um reader temporário. Agora o servidor mantém um reader por vínculo de PID, informa `readerStatus` e `readerMessage` reais, e registra PID, disponibilidade do handle e ausência do provedor de endereços. O `ProcessManager` abre apenas um handle de consulta (`PROCESS_QUERY_LIMITED_INFORMATION`); ele não fornece um estado de personagem. Por isso `NOT_CONFIGURED` continua correto após anexar o cliente, e o detector visual pode funcionar independentemente dele.

O Named Pipe usa `KBot.NativePipe.CharacterV3` para que um núcleo antigo já em execução não seja reutilizado pelo executável novo. A publicação portátil embute o núcleo compilado junto com o aplicativo.

## Sinais visuais observados

Em capturas reais de 1920×1057 da área cliente, o mapa apresenta textura no centro, Pokébar com barras verdes e cianas à esquerda, Battle List com barras verdes e painel Pokémon com barras vermelhas/azuis à direita, e barra de ações escura na parte inferior. O minimapa não estava visível nesse layout, por isso não é exigido. Nenhum nome de personagem é usado. A seleção de personagem apresentou um cartão central, um botão verde de entrada e uma coluna de atributos à esquerda, sem a HUD do mapa.

As regiões do detector são frações dos limites da área cliente capturada; não há coordenadas absolutas de 1920×1080 ou 1152×768. A interface observada mantém painéis ancorados às bordas e uma área central de mapa. Mudanças de tema, escala interna, posição dos painéis ou HUD oculta podem exigir recalibração. **VER DETECÇÃO** mostra o frame analisado e os retângulos das regiões; verde indica sinal encontrado, laranja indica sinal ausente.

`InGame` exige textura de mapa, pelo menos dois sinais de HUD e presença da Pokébar ou do painel Pokémon. No layout observado, cinco sinais foram encontrados. A seleção requer três sinais próprios e ausência da HUD. Frames insuficientes permanecem `Unknown`; não há promoção para `InGame` por timeout. O reader só participa quando o protocolo declarar `READY`, retornar um estado explícito e confirmar o PID esperado. Se reader e visão discordarem explicitamente, a decisão fica `Unknown`.

Uma janela minimizada pode ter área cliente 0×0. Falha de captura, bounds inválidos, frame pequeno ou vazio produzem `DetectionStatus=CaptureUnavailable`. Essas leituras não contam como ausência de HUD/mapa nem alteram `CharacterSession.IsInGame`; o horário de `LastConfirmedInGame` permanece. Um frame válido sem a evidência de mapa/HUD gera `DetectionStatus=NotInGame` e pode contar para a saída após quatro leituras. A sequência de negativas é interrompida por captura indisponível e recomeça apenas com frames válidos. Quando a janela voltar, o detector retoma as verificações normalmente.

`LauncherSession` guarda o PID do launcher e `GameSession` usa o PID/HWND do cliente real encontrado pelo watcher. O watcher continua procurando o executável conhecido a cada 500 ms mesmo se o launcher encerrar antes de aparecer a janela do jogo. Depois do vínculo, o ciclo de vida só usa a saúde do processo do jogo para decidir desconexão; o status de handoff e os logs mostram os dois PIDs e o encerramento do launcher.

O watcher não descarta um candidato só por ter o mesmo número de PID que um launcher já encerrado: a identificação usa o caminho/nome do executável real. Isso também cobre reutilização de PID após o fechamento do launcher.

No teste de handoff, launcher PID 12652 e cliente PID 21512 eram processos distintos. O `GET_STATUS` do núcleo retornou PID 21512 e o HWND do cliente, enquanto a Dashboard do NevesBot estava aberta. Uma `LauncherSession` com PID ausente também permitiu ao watcher encontrar o mesmo cliente real. O launcher oficial não encerrou após `CloseMainWindow`; uma tentativa de encerrar somente esse processo retornou acesso negado pelo Windows. Portanto o encerramento **real** do launcher e a permanência da Dashboard após esse evento ainda precisam ser observados quando ele sair pela própria interface.

O agregador confirma entrada após três leituras consecutivas. A saída de `InGame` exige quatro leituras de outro estado ou quatro perdas de evidência; uma captura isolada não fecha a Dashboard. Após seleção/logout, três novas leituras de mapa reabrem a Dashboard. A janela principal fecha via ciclo de vida, e seu `MainViewModel.DisposeAsync()` cancela o monitoramento ativo.

## Estado de validação

Foram analisados três frames reais de personagem no mapa (`5/5`, `InGame`, 98%) e um frame real de seleção (`0/5` sinais de mapa; três sinais de seleção; `CharacterSelection`, 90%). Em uma execução com o cliente aberto no mapa, o ciclo de vida chegou a `Ready`/`InGame` usando Vision com quatro dos cinco sinais e 92% de confiança; a Pokébar havia sido movida para mais baixo. O teste também passou com o frame de mapa redimensionado para 60%, com entrada, saída e reentrada temporais. A tela de login ainda precisa de uma captura real para classificação específica; até lá ela permanece `Unknown` e não libera a Dashboard. `Loading` também permanece `Unknown` quando não há evidência visual específica. O fechamento do processo é identificado pelo `GameSession.IsAlive` e publicado como `Disconnected`.

## Reconexão e retry (2026-09-21)

O ciclo de vida em `KBotLifecycle.RunAsync` agora é um loop externo de reconexão: quando o processo do cliente morre, o ciclo detacha, descarta a sessão (inclusive o `Bot`, que seguraria um HWND morto), volta ao estado `Disconnected` e fica aguardando o jogo reabrir a cada 1 s — **sem** relançar o launcher sozinho (auto-launch acontece apenas na primeira passagem). Assim o bot segue reconhecendo o client sem que o usuário precise reiniciá-lo.

Outras correções da mesma rodada:

- **Attach sem throw**: antes, uma única falha de `AttachGameAsync` lançava `InvalidOperationException` e deixava o app em `Error` para sempre. Agora o ciclo tenta o attach repetidamente a cada 3 s (recriando o núcleo entre tentativas) e mostra "Núcleo nativo sem resposta" na UI quando o pipe não responde — útil quando `KBot.Native.exe` nunca foi compilado.
- **Núcleo morto em runtime**: 3 ticks consecutivos de `GET_STATUS` nulos recriam o núcleo, fazem `PING` e, se voltar, refazem `ATTACH_PID` + reaplicam o offset de posição salvo (o núcleo novo perde os dois).
- **Seleção de instalação**: além de nome/launcher "PokeAlliance", a fallback agora aceita a instalação mais recentemente usada (`GameLibrary.Load()` já ordena por `LastUsedAt`), então uma instalação renomeada continua sendo reconhecida.
