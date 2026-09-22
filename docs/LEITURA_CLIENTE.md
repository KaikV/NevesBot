# Leitura do client (KBot × Kryon)

Como o KBot "vê" o jogo, onde isso se conecta ao cérebro, e o que falta para ler de
verdade no cliente real. Este doc fecha o loop entre **transporte** (ler memória/tela)
e **decisão** (módulos), que já está testado headless em `KBot.Tests`.

Regra de ouro usada em todo o pipeline: **estado desconhecido = `null`**. Nenhum módulo
chuta; sem leitura, a feature degrada em silêncio.

---

## 1. As três camadas de leitura

O KBot tem três fontes independentes. Só duas leem o processo; a terceira é a
decisão pura que roda por cima da leitura.

| # | Camada | Lê o quê | Fonte (arquivo) | Saída |
| --- | --- | --- | --- | --- |
| 1 | **Visão** (screenshot) | pixel da janela | `WindowCaptureService` → `VisionInGameDetector` | `CharacterPresence` + frame |
| 2 | **Memória externa** (processo) | posições por offset | `KBot.Native.exe` (`ClientReader.h`) via pipe | `NativeStatus` (posição + presença) |
| 3 | **Varredura / scan** (cérebro) | criaturas na tela | `ScreenScan.Analyze` (puro) | `ScanResult` (wilds / meu poke / outros) |

### Camada 1 — Visão (`WindowCaptureService.cs`, `VisionInGameDetector.cs`)
- Captura com `PrintWindow` (3 fallbacks: PrintWindow → DWM → GDI).
- Sobe para `Bgra32`, corta regiões por fração (Mapa, Pokebar, Battle List, Pokémon HUD,
  Action bar), mede sinais (textura de mapa, barras de cor, fração escura).
- Decide **presença** (`InGame` ~92%) **sem offset nenhum** — é a camada mais robusta a
  updates. Roda 1x/tick e alimenta `CharacterSession`.
- **O frame já capturado aqui é reaproveitável** pela camada 3 quando o transporte existir.

### Camada 2 — Memória externa (`ClientReader.h`, `ProcessManager.cpp`, pipe)
- Abre o processo com `PROCESS_VM_READ` e lê por offset, com 2 builds:
  - **DX**: `X = *(int32*)(base + 0x37454E0)`, `Y = +0x4`, `Z = +0x8` (direto na base).
  - **GL**: ponteiro `pointer = *(base + 0x0027D168)` → `X/Y/Z = *(ptr + 0x0/0x4/0x8)`.
- Publica pelo pipe nomeado **`KBot.NativePipe.CharacterV3`**; o C# lê em `NativeService.cs`
  e deserializa em `NativeStatus` (`nativeOnline/clientFound/pid/hwnd/readerStatus/
  hasPosition/posX/posY/posZ`).
- **É aqui que quebra a cada update**: se o offset muda, a leitura volta `UNAVAILABLE` e o
  `GameStateProvider` degrada a posição para desconhecida. O *Caçador de Offsets*
  (`ScanForPositionAsync` / `Dump`) existe exatamente para re-descobrir o `0x37454E0`.
- **Recalibração automática (delta-scan)**: quando o personagem está InGame e o reader
  não está `READY`, o `KBotLifecycle` dispara o `OffsetAutoCalibrator` em background
  (cooldown 5 min, botão "Recalibrar agora" no dashboard). Fluxo zero-input:
  1. `SCAN_DELTA_SNAP` — core faz snapshot dos int32 do módulo (personagem parado);
  2. passo de 1 tile pela pipe de teclas (`D`, fallback `RIGHT`/`UP`);
  3. `SCAN_DELTA_COMMIT` — diff; só valem triplas X/Y/Z onde **exatamente um eixo** mudou
     1 tile e `|v| < 50000` (mata contadores); teto 128 MB por candidato, cap 24;
  4. `SCAN_DELTA_STABLE` — parado de novo; candidatos que continuaram mudando morrem;
  5. cada sobrevivente é aplicado (`SET_POSITION_OFFSET`) e exige **duas leituras READY
     idênticas** antes de ser salvo por executável em `PositionOffsetStore`.
  Durante o processo o `TickBrain` fica suspenso (nenhum módulo pode andar o
  personagem) e nada é persistido sem verificação dupla — falha = motivo explícito,
  sem chute. A calibração manual (`CavebotViewModel`) continua como plano B.
  ⚠ O core precisa ser recompilado no Windows para os comandos `SCAN_DELTA_*` existirem.

### Camada 3 — Varredura / scan (`ScreenScan.cs`)
- Decisão **pura e headless** (nada de IO, nada de relógio): recebe uma lista de
  `ScannedCreature(type, hp, x, y, z)` e devolve `ScanResult(wilds, myPoke, others)`.
- Regras fiéis ao Kryon (`_cbScreen` / `main.lua:977-1062`):
  - `type 3 = SummonOwn` (meu pokémon, nunca alvo) → vira `MyPoke`;
  - `type 4 = SummonOther` (poke de outro jogador) → vira `Others`;
  - resto com `hp > 0` → `wilds`.
  - `sofaPokeOut()`: corpo (hp ≤ 0) **não** conta como poke em campo — o log real de 26/08
    mostrou que contá-lo prendia o socorro justamente na hora do aperto.
- Distância é **Chebyshev `max(|dx|,|dy|)`** e **exige `z` igual** (`DangerDistance = 3`).
- Coberto pelos testes headless A–D em `RunScanChecks` e E em `RunScanSourceChecks`.

---

## 2. Onde a leitura vira decisão (o gap que era real)

Fluxo por tick (`KBotLifecycle.TickBrain`):

```
NativeStatus (camada 2)  ┐
CharacterPresence(1)     ├─► GameStateProvider.From(status, presence, now, scan)
IScreenScanSource(3)     ┘        │  -> EnemyCount, Wilds, FieldHasPoke, WildsNearby
                                  ▼
                            GameState (null p/ desconhecido)
                                  ▼
                    BotBrain.Tick() -> módulos escolhem uma ação
```

Antes de hoje, `TickBrain` chamava `From(status, presence, now)` **sem** o 4º arg `scan`,
então em jogo real `EnemyCount/Wilds/FieldHasPoke` ficavam desconhecidos — a decisão
existia (e passava nos testes, que injetavam `ScannedCreature` direto) mas o **transporte**
nunca produzia o `ScanResult`. Esse era o único gap da ETAPA 2.

### A seam: `IScreenScanSource` (`KBot.App/BotBrain/ScreenScanSource.cs`)
- Interface mínima: `ScanResult? GetScan(NativeStatus?, CharacterPresence)`.
  `null` = "sem leitura este tick" → o provider mantém as campos **desconhecidas**
  (nunca "tela vazia").
- Implementação padrão `NoScreenScanSource`: sempre `null` — idêntica ao comportamento
  antigo, mas substituível numa linha.
- Adapter `ScreenScanSource.FromCreatures(Func<IReadOnlyList<ScannedCreature>>)` : recebe a
  lista bruta que o leitor C++ ou a visão vão emitir e re-roda `ScreenScan.Analyze` a cada
  tick (o `Func` rele a memória viva, sem snapshot stale).
- Injetada via `KBotLifecycle(characterDetector, screenScanSource)` — o cérebro segue
  desacoplado; não mudou nada no `BotBrainFactory` nem nos módulos.

```csharp
// KBotLifecycle.TickBrain — provider agora passa o scan:
var presence = CharacterSession?.State ?? CharacterPresence.Unknown;
var scan = _screenScanSource.GetScan(LastNativeStatus, presence);
return GameStateProvider.From(LastNativeStatus, presence, Environment.TickCount64, scan);
```

Para acender a varredura no real basta implementar `IScreenScanSource` apontando para a
leitura de criaturas (offsets ou visão) e injetá-la — zero mudança no cérebro.

---

## 3. O modelo do Kryon (alvo da leitura "de dentro")

O Kryon **não lê offset**. Ele injeta um hook (LuaJIT + `pezunu.dll`) e o client expõe uma
API de objetos tipados. Por isso **não quebra em update**: o hook devolve getters, não
endereços. Detalhes completos em `docs/ROADMAP.md` (seção 0) — a superfície que o KBot deve
espelhar no pipe:

```
PlayerHandle: GetPosition(), GetHealthPercent(), GetLevel(), GetName(), GetId()
GameEngine:   IsOnline(), GetPing(), GetAttackingCreature(), Walk/Turn/Stop, TalkChannel/Private,
              RequestChangePokemon(slot), UseOnPokemon/UseOnPokeball, UseWith
Map:          GetSpectatorsInRange(c,r), GetTile(pos), GetMinimapColor(pos)
Creature:     GetType(), GetId(), GetName(), GetPosition(), GetHealthPercent(),
              IsMonster(), IsPlayer(), CanAttackCreature()
Pokebar:      GetPlayerPokeballs() -> [{name, health, uuid}], GetActiveSlot()
Pokemoves:    GetCurrentMoveCooldowns(), SendCastMove("mN")
Buff:         onPlayerBuffsReceived -> [{name, leftTime}]
```

Comparativo honesto:

| | KBot (hoje) | Kryon |
| --- | --- | --- |
| Leitura | offset fixo, fora do processo | API tipada, dentro (hook LuaJIT) |
| Atualiza o client | **quebra** (re-procurar offset) | não mexe |
| Posição | ✅ já lê (DX/GL) | `player:getPosition()` |
| Party/HP, criaturas, chat | ⬜ offsets pendentes | prontas na API |

Ler "de dentro" igual ao Kryon exigiria injetar uma DLL no processo Windows — esforço
maior, ainda fora deste ciclo. O caminho incremental é fechar os **offsets** (mesmo
padrão da posição) e, só depois, avaliar a injeção.

---

## 4. O que falta (ordem por desbloqueio)

1. **ETAPA 0 — posição viva** (já lido; revalidar offsets DX/GL no build atual no Windows).
2. **ETAPA 1 — party + HP** → destrava Cura/Socorro no real. Offsets de pokebar/active.
3. **ETAPA 2 — criaturas na tela** → **seam pronta** (`IScreenScanSource`); falta o
   transporte C++/visão que encha `GetScan` com a lista de criaturas. Acende Alvo/Combo.
4. **ETAPA 3+** — walk humanizado, motor de alertas, chat, captura, pesca (ROADMAP §2).

## 5. Verificação (roda em WSL, headless)

- `dotnet run --project KBot.Tests/KBot.Tests.csproj` → `All checks passed.` (inclui E da seam).
- `dotnet build KBot.App/KBot.App.csproj -p:EnableWindowsTargeting=true` → compila sem erro.
- Não testável aqui (WSL, sem o jogo aberto): captura live e leitura de memória reais —
  exigem o Windows com o client rodando para validar offsets e o scan de verdade.
