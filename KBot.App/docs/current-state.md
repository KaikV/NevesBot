# Estado atual do KBot

## Implementado

- Descoberta do PokeAlliance por processo e por janela.
- Cadeia `cliente -> PID -> HWND -> GameSession -> captura`.
- Comunicação C# com o núcleo nativo por named pipe.
- Envio de passos direcionais individuais pelo núcleo (`SEND_KEY`).
- Leitura real da posição X/Y/Z via `ReadProcessMemory` no núcleo C++, por build do cliente:
  - **GL** (`_gl.exe`): base + `0x0027D168` → ponteiro → `+0x0/+0x4/+0x8`.
  - **DX** (`_dx.exe`): direto na base do módulo, `base + 0x37454E0/+0x4/+0x8` (confirmado ao vivo: o valor acompanha o movimento e mantém Z=7; o vizinho `+0x37454F4` fica congelado).
  O build é detectado pelo nome do executável (`IsDx`). O `GET_STATUS` devolve `hasPosition`, `posX`, `posY`, `posZ` e o leitor responde `READY` quando a leitura fecha.
- Dashboard mostra as coordenadas X/Y/Z ao vivo.
- Loop de navegação automático no C#: lê a posição, envia W/A/S/D por eixo dominante, chega no waypoint (tolerância 1 tile) com timeout (~10s) e detecção de stall; INICIAR/PARAR seguros.
- Editor de rotas com importação, salvamento e organização de waypoints.

## Parcial

- Ações `Talk`, `Use`, `StartAttacker`, `StopAttacker` e `OrderPokemon` executam o passo mas ainda não disparam seu atalho/offset próprio (pendente).
- `Wait` aguarda um fixo curto; não lê tempo restante nem estado do cliente.

## Não implementado

- Leitura confirmada de HP / estado de batalha / lista de alvos (outros offsets).
- Execução dos atalhos por ação de waypoint (falta offset/hotkey confirmado).

## Próximo bloqueio técnico

Para ativar curinga/revive e seleções de alvo precisamos de um perfil de leitura
confirmado para a versão exata do PokeAlliance em execução. A posição já usa um único
offset confirmado por build (GL `0x0027D168` com cadeia de ponteiro; DX `0x37454E0`
direto na base); qualquer outro offset deve ser confirmado antes de ser ligado —
endereços herdados do PxgBot não são reusados automaticamente.
