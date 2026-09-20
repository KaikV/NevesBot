# Estado atual do KBot

## Implementado

- Descoberta do PokeAlliance por processo e por janela.
- Cadeia `cliente -> PID -> HWND -> GameSession -> captura`.
- Comunicação C# com o núcleo nativo por named pipe.
- Envio de passos direcionais individuais pelo núcleo (`SEND_KEY`).
- Editor de rotas com importação, salvamento e organização de waypoints.

## Parcial

- A tela Rota já permite testar um passo por vez.
- O núcleo expõe o estado do leitor, mas o leitor ainda está em `NOT_CONFIGURED`.

## Não implementado

- Leitura confirmada de posição X/Y/Z.
- Conversão de waypoint absoluto em passos direcionais.
- Execução automática e parada segura da rota.

## Próximo bloqueio técnico

Para ativar o cavebot automático precisamos de um perfil de leitura confirmado
para a versão exata do PokeAlliance em execução. Endereços herdados do PxgBot
não são usados automaticamente porque podem apontar para dados inválidos ou de
outra versão do cliente.
