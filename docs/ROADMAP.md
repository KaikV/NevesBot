# Roadmap KBot → paridade com KryonBot

Documento de referência: como cada feature do KryonBot **funciona por baixo** (fonte Lua
em `/tmp/opencode/dec/`) e o caminho para trazê-la para o KBot. Cada etapa termina com um
**check de paridade** contra o fonte do Kryon.

---

## 0. O ponto que explica tudo

O KryonBot **não lê offsets de memória**. Ele injeta um *hook* (LuaJIT) dentro do client e o
client expõe uma **API de objetos tipados**. Tudo que o bot faz passa por quatro superfícies:

| Superfície | Papel | Exemplo |
| --- | --- | --- |
| `g_game.*` | engine: posição, batalha, itens, walk, talk, online | `player:getPosition()` |
| `g_map.*` + `getSpectators()` | mundo: quem está na tela, tile sob o mouse | `g_map.getSpectatorsInRange(pos,false,vx,vy)` |
| `player` (global) | o próprio personagem (posição + HP + level) | `player:getHealthPercent()` |
| `modules.game_pokebar / game_pokemoves / game_buffs` | party, moves, buffs | `gpb.getPlayerPokeballs()[i].health` |

Consequência direta: **no Kryon nada muda quando o client atualiza**, porque o hook devolve
objetos com getters, não endereços. Meu `KBot.Native` lê `base + 0x37454E0` (X), `+4` (Y),
`+8` (Z) — e quebrou no v02.63 exatamente por isso.

### O que o KBot precisa espelhar (a interface de sensor)
Não preciso copiar o hook; preciso que meu native pipe devolva **os mesmos dados**:

```
PlayerHandle:   GetPosition()->(x,y,z), GetHealthPercent()->0..100, GetLevel(), GetName(), GetId()
GameEngine:     IsOnline(), GetPing(), GetAttackingCreature()->Creature?, CancelAttack(),
                Walk/Turn/Stop, TalkChannel(mode,id,text), TalkPrivate(mode,name,msg),
                RequestChangePokemon(slot), UseOnPokemon/UseOnPokeball(id,-1), UseWith(...)
Map:            GetSpectators()->Creature[], GetSpectatorsInRange(c,r)->Creature[],
                GetTile(pos)->Tile?, GetMinimapColor(pos)
Creature:       GetType(), GetId(), GetName(), GetPosition(), GetHealthPercent(),
                GetSkull(), IsMonster(), IsPlayer(), IsLocalPlayer(), CanAttackCreature()
Pokebar:        GetPlayerPokeballs()->[{name, health(0..100), uuid}], GetActiveSlot()
Pokemoves:      GetCurrentMoveCooldowns()->[{name,spellword,currentCooldown}], SendCastMove("mN")
Buff:           onPlayerBuffsReceived -> [{name, leftTime}]
Eventos:        onCreatureHealthPercentChange, onInstanceEntry, onFlyControlsChange,
                "Your Pokemon is dead."
```

**Tipos de criatura:** `3 = SummonOwn` (meu pokémon, nunca alvo), `4 = SummonOther`
(pokémon de outro player, nunca alvo), resto = selvagem se `canAttackCreature()` ou `skull==0`.

**Regra de distância:** Chebyshev `max(|dx|,|dy|)`, sempre exigindo `z` igual.

---

## 1. Inventário de features do Kryon (como funciona)

Status: ✅ portado & testado · 🟧 portado, esperando sensor · ⬜ não começou

### Cavebot / Rota
| Feature | Fonte | Como funciona no Kryon | KBot |
| --- | --- | --- | --- |
| Walk por waypoints | `cavebot/walking.lua` | Sequência de ações; anda tile a tile, `getStepDuration(dir)` controla o passo; **anti-detecção**: drift suave na velocidade + ruído por passo (quebra o histograma de timing do detector) | 🟧 `RouteModule` + `CavebotNavigator`; falta walk contínuo + humanização |
| Recorder (auto-cavebot) | `cavebot/recorder.lua` | Andando, crava waypoint a cada N tiles (`recordDist` 1-9; 0 = sorteia 1-5/sessão pra quebrar assinatura); escada e "use" de item sempre gravados | ✅ gravador existe; precisa de **posição viva** pra cravar |
| Ações do waypoint | `cavebot/actions.lua` | `walk`, `wait`, `use`, `item`, `talk`, `stopAttacker`, `startAttacker`, `orderPoke`… editável por duplo clique | 🟧 estrutura ok, execução depende de sensor |
| Espera de respawn | `n4_rota.lua` | No 1º waypoint, `wpWait` segundos (respawn lento); usa `GetMinimapColor` pra saber se área zerou | ⬜ |
| Pular waypoint se monstro bloqueia | `cavebot.lua` | Se selvagem no caminho: 1 ping, esperar, pular o ponto | ⬜ |

### Combate / Alvo (aba Alvo)
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| Detectar batalha | `main.lua:1469,8736` | Não há boolean: `emAcao = getAttackingCreature() ~= nil` + nº de selvagens da varredura | 🟧 precisa de `GetAttackingCreature` + spectators |
| Varredura de tela (visão) | `main.lua:995-1062` | `_cbScreen`: iter `getSpectatorsInRange`, separa `mine` (SummonOwn) de `wilds` (monstros atacáveis com HP>0); memo por tick (~10x/s) | ✅ decisão no ScreenScan.cs (teste headless); espera offset de criaturas p/ soltar no real |
| Combo em área vs 1x1 | `n1_combate.lua` | Combina todos os alvos da varredura; "1 by 1" prioriza o da lista de alvos primeiro | 🟧 `TargetingModule` decide, sem varredura ainda |
| Seleção de alvo (shiny/raro) | `main.lua:1285-1390` (`sofaRareWild`) | Substring no nome (Shiny/Elite/Ancient) = prioridade total; senão o mais PERTO no alcance (Chebyshev, mesmo z) | ✅ decisão no TargetSelection.cs + TargetingModule (teste headless A–D); mira vira cmd `aim` pendente |
| Lista de alvos (prioridade) | `main_endgame.lua` | Ordena por nome na lista; `pokestopCmd` congela via chat `"!pokestop"` | ✅ `EndgameDetect.cs` (puro) + `EndgameModule`; teste headless A–H (rotação, fainted-skip, offline/stale, relure, tank-swap/potion, gating); safe-spot lido do perfil |
| Soltar poke sozinho | `main.lua:3843` | `getMana()` sempre -1 no PA; usa presença de SummonOwn no mapa; `requestChangePokemon(slot)` | 🟧 decide, sem ler campo |
| Cast de move | `main.lua:1222` | `modules.game_pokemoves.sendCastMove("mN")`; fallback `talkChannel(1,0,"mN")` (barra pode virar vazia pós-swap) | 🟧 hotkey F9-F12 via pipe (equivalente ok) |
| Cooldown de skill | `main.lua:458` | `getCurrentMoveCooldowns()` -> `currentCooldown<=0` = pronto; fallback lê widgets `Progress1..12` | 🟧 cooldown em segundos (mais simples) |
| Lure / posicionamento | `n2_lure.lua` | Afasta-se pra monstro seguir; lê item de chão via `g_map.getTile` | ⬜ |

### Cura / Revive (aba Cura)
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| Potion (vida) | `main.lua:1068` | `useOnPokemon(item, -1)` quando `hp < threshold` | 🟧 `HealingModule` decide, sem HP real |
| Medicine (status) | main | cura status abaixo de `CureAtPercent` | 🟧 idem |
| Auto-revive | `main.lua:3112` | `requestPokebarReviveByItemId(item, slot)` quando poke morto na barra | 🟧 idem |
| **Socorro** (nunca ficar sem poke) | `n9_socorro.lua` | Rede última linha: se há segundos sem NADA em campo (pelo mapa, não pela barra que mente), solta poke. Caso real: barra mostrava vivo (recolhido) e o auto-revive não agiu em poke não-morto → morreu em 20s | ✅ decisão no SocorroModule (prioridade 95, teste headless); espera offset de pokebar para soltar no real |
| Caixa-preta da vida | `n8_caixapreta.lua` | Fica calada até a vida CAIR; junta o episódio (dano seguido sem pausa 1,5s) e grava 1 linha: quanto perdeu, tempo, se havia poke, quantos selvagens, o que o bot fazia, quando cada socorro agiu | ⬜ (óimo p/ depurar cura) |

### Captura (aba Captura)
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| Catch shiny | `0_AB_catch.lua` | Criatura morrendo cujo nome começa com prefixo shiny (shiny/ancient/elder/giant) → bola shiny | ✅ decisão no CatchSelection.cs (substring no nome + janela de morte); transport do corpse ainda falta |
| Catch por corpse | `0_AB_catch.lua` | Lista `{nome, corpseId, ballId}`; quando corpse do id aparece no tile, usa a bola (`useWith(ball, corpse, 0)`) | ✅ decisão (exata por id + só se EU matei + bola ≥100) em CatchSelection.cs; CatchModule emite `catch:bola:<id>` ou silêncio |

### Pesca (aba Pesca)
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| Auto-pesca | `0_AD_fish.lua` | Lança vara no **ponto de pesca mais próximo** (item `waterId` no chão, raio 1..12 tiles); cadência **ping-aware** (nunca menor que `max(base, ping)`, default 13s — o ritmo que o servidor responde); pausa quando ≥ N selvagens no range (recheque 1x/s com backoff); cede o turno se catch/loot estiver ocupado | 🟧 port da decisão em FishingGate.cs (settle 3s / cadência / pausa maxPoke / cross-busy / clamp raio) + FishingModule emite `fish`, teste headless; **falta**: leitura da vara no slot 2 e do tile d'água (transport) |

### Alertas (aba Alertas) — motor `sofaAL`
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| Motor de regras | `main.lua` (sofaAL) | Não são features soltas: UM motor com **tipo de gatilho × ação**. Ações: som em loop (volume, mutar jogo), pausar cavebot/target/loot, deslogar e voltar em X min, cooldown, log. Novos tipos herdam tudo | ⬜ arquitetura ainda não no KBot |
| Tipos de alerta | `n3_alarmes.lua` | SAIU / SUPRIMENTO / MORTE / NIVEL / CAPTUROU. Cada tipo é um DETECTOR DE BORDA (só dispara na transição; cooldown 4s). Prova de captura = MENSAGEM DO SERVIDOR no chat ("You caught a Pokemon! (Shiny X)"), não o corpo sumir | ✅ decisão em AlertDetect.cs + AlertsModule (morte/nivel/saiu/suprimento 3-leituras/capturado) teste headless; QUEDA-ao-voltar e Telegram ainda sem transport |
| Telegram | `n3_alarmes.lua` | `POST https://licenca.kryonbots.com.br/pa/notify` com chatId/token; aceita comandos do celular (nasce ligado) | ⬜ |
| Modelos prontos | `n3_alarmes.lua` | Botão aplica regras comuns pré-montadas | ⬜ |
| **Responder PM** | `nF9_responderpm.lua` | 1x por pessoa; normaliza texto (acentos/repetidos); regras por prioridade (pergunta > saudação); janela de coleta (2ª msg mescla, não estende prazo); delay 3-6s sorteado; frases editáveis; GM **não** responde | ✅ `PmResponderService` (falta gancho onTalk + transporte talkPrivate) |
| **Saída educada** | `nE_saida.lua` | Ao deslogar: fala frase local humanizada, anda até o **ponto de deslog** (mesma mecânica do cavebot, com escada), aí desloga | ⬜ |
| **Vigia / puxão** | `nF8_vigia.lua` | Detecção de **teleporte admin** (coordenadas mudam brusco) = o sinal mais honesto; para TUDO antes e toca alarme volume máximo | ✅ `VigiaTracker` puro (saltos, carimbo esperado/origem, 10s de graça, pontos ensinados) + `VigiaModule` prioridade 90; pendente: canal de fala de GM ("mensagem no mapa") e som/parada do jogo |

### Extras
| Feature | Fonte | Como funciona | KBot |
| --- | --- | --- | --- |
| **Compartilhar** | `nB_compartilhar.lua` | Cria código curto com a config, outro cola e aplica | ✅ `ConfigShareService` (KPB1) |
| **Conferir / diagnóstico** | `n6_conferir.lua` | Mostra o que está pronto/pendente por feature | ✅ `ConfigDiagnosticsService` |
| Auto-hunts | `nC_autohunts.lua` | Usando a porta (item 50390) abre janela de instância; server manda pacote 1587 só quando se usa a porta → reabrir É o reload; entra na primeira vazia | ⬜ |
| Buffs / usar itens | `nD_extras.lua` | Usa item de buff (xp/charm/loot), lê tempo restante em `modules/game_buffs/playerbuffs.lua`, reusa quando acaba | ⬜ |
| Inspetor ID | `n5_inspetor.lua` | Ctrl+Shift sobre tile/item → etiqueta com ID (+ x,y,z no tile) | ⬜ |
| Shiny ícone | `n7_shiny_icone.lua` | `setActivatedShinyIcon(true)` + detecção visual | ⬜ |
| Guild daily | `nH_guild.lua` | `sendGuildResetDaily`, `sendGuildRequestData`, `sendDailyQuestSelection` | ⬜ |
| Voar | `nA_voar.lua` | `flyAction` + `onFlyControlsChange` | ⬜ |
| Antiafk | `nL_antiafk.lua` | passo ao lado após ficar parado + volta agendada; alterna o lado; não anda contra parede; freio de ocupado (combo/mercado) | ✅ `AntiAfkTracker` (puro, testado) + `AntiAfkModule`; walkability dos tiles ainda é leitura pendente (degrada p/ livre) |
| Remoto | `nF_remoto.lua`, `0_A2_remoto` | estado resumido via pacote (level, pos, hp) | ⬜ |
| Hotkeys | `zz_hotkeys.lua` | atalhos globais (último a carregar) | 🟧 |
| Idioma | `0_A0_idioma.lua` | i18n PT/EN (1833 refs) | ✅ (UI já em PT) |

> **Loot/Coleta foi removido do Kryon** ("não vamos usar mais", `0_AC_loot.lua`). A aba Coleta
> no KBot pode ficar como stub — não é mais paridade obrigatória.

---

## 2. Roadmap em etapas

Cada etapa: **objetivo → como confiro no Kryon → aceite**. Só avanço quando o passo anterior
passou no teste headless e (quando depender de runtime) no seu Windows.

### ETAPA 0 — Destravar leitura de posição (gargalo de tudo)
- **Objetivo:** `KBot.Native` devolver `(x,y,z)` reais de novo.
- **Como confiro no Kryon:** posição vem de `player:getPosition()` (Chebyshev, exige `z`).
  Precisa bater com o minimap do jogo nos dois scans.
- **Plano:** rodar o Caçador de Offsets que já existe (`TryScanForPosition` + `Dump`) com
  `PokeAlliance_dx.exe`; 2 scans (parado / +1 tile); o que sobrar = offset novo. Validar a
  chain GL `base+0x0027D168→ptr→X/Y/Z` como fallback. Gravar em `ClientReader.h`.
- **Aceite:** app mostra posição viva igual ao minimap; INICIAR e GRAVAR ROTAM passam a andar.

### ETAPA 1 — Ler party + HP (destrava Cura/Socorro)
- **Status:** **DECISÃO pronta** (commit `556a212`): `SocorroModule` + estado
  `Pokebar/ActivePokebarSlot/FieldHasPoke/WildsNearby` no `GameState`; resolver já
  mapeia `summon` → pendente (offset-bound). **Falta o transporte C++** — offsets de
  pokebar/active ainda não existem (ETAPA 0 mostra o caminho: caçador de offset).
- **Objetivo:** `Pokebar.GetPlayerPokeballs()` (name/health/uuid por slot) + `GetActiveSlot()`
  + HP do pokémon ativo.
- **Como confiro:** `main.lua:3010` (`getPlayerPokeballs`), `n9_socorro.lua:108` (`vivo/morto`
  por slot), `main.lua:3044` (`getActiveSlot` é a fonte autoritativa de "quem tá em campo").
  Cross-check: barra **mente** (fica stale) → cruzar com HP da criatura SummonOwn no mapa.
- **Aceite:** HealingModule usa HP real; teste headless com fake pokebar ✓ (A–E em
  `RunSocorroChecks`); no Windows o Socorro solta poke quando o campo fica vazio
  (espera calibração de offset para acender no real).

### ETAPA 2 — Ler criaturas na tela (varredura / visão)
- **Status:** ✅ decisão pronta (ScreenScan.cs, teste headless A–D); falta transporte
  C++ (offsets de criaturas não existem ainda). `GameStateProvider` já consome o scan:
  `EnemyCount` + `FieldHasPoke` + `WildsNearby` viram reais quando a varredura existir.
- **Objetivo:** `Map.GetSpectatorsInRange(center, rx, ry)` retornando Criatura com tipo/HP/pos.
- **Como confiro:** `main.lua:977-1062` (`sofaVisionSpecs` + `_cbScreen`): separa `mine`
  (SummonOwn=3) de `wilds` (monstros atacáveis HP>0); `visX=10/visY=5` (tela 21×11); memo por tick.
- **Aceite:** TargetingModule seleciona pelo range e lista de prioridade; combo vs 1x1 correto
  no teste headless; "detectar batalha" passa a usar `attackingCreature` + nº de wilds.

### ETAPA 3 — Walk contínuo + anti-detecção (cavebot de verdade)
- **Status:** ✅ decisão pronta — `CavebotRecorder.cs` (regra de gravar rota, teste headless A–E).
  Faltam o transporte C++ de caminhada (prewalk/timing humanizado) e a gravação UI.
- **Objetivo:** andar tile a tile com timing humanizado, não "anda/para".
- **Como confiro:** `cavebot/walking.lua` (stepDur + drift suave + ruído ≤ ~28ms/passo) e
  `recorder.lua` (distância 1-9, sorteio por sessão).
- **Aceite:** GRAVAR ROTAM grava a cada N tiles conforme o campo; INICIAR percorre rota liso;
  escada vira waypoint.

### ETAPA 4 — Motor de alertas (arquitetura gatilho×ação)
- **Objetivo:** substituir alertas soltos por 1 motor (tipo de gatilho × ação com som/pausar/
  deslogar/cooldown/telegram), como `sofaAL`.
- **Como confiro:** `main.lua` (sofaAL) + `n3_alarmes.lua` (tipos SAIU/SUPRIMENTO/MORTE/QUEDA,
  telegram, modelos prontos). "Queda" avisa só ao voltar.
- **Depende de:** ETAPA 1 (HP/morte) e leitura de chat (ETAPA 5).
- **Aceite:** tipos novos herdam ações de graça; queda detectada no relógio `_G`/equivalente.

### ETAPA 5 — Leitura de chat (gancho de mensagens)
- **Objetivo:** native pipe começar a entregar eventos de chat (modo 4 = PM de jogador, modo 5 =
  meu PM, 14/15 = GM), pra habilitar Responder-PM real, Saída educada, alerta de PM.
- **Como confiro:** `nF9_responderpm.lua:300` (`onTalk(name,level,mode,text)`; responde **só
  modo 4**), `nE_saida.lua` (local).
- **Aceite:** Responder PM fecha o loop (onTalk → PmResponderService → talkPrivate(5)); saída
  educada anda ao ponto e desloga.

### ETAPA 6 — Captura (shiny + por corpse)
- **Objetivo:** detectar corpse no tile (`g_map.getTile` → item id) e lançar bola.
- **Como confiro:** `0_AB_catch.lua` (prefixo shiny; lista nome/corpseId/ballId; `useWith`).
- **Depende de:** ETAPA 2 (varredura) + leitura de tile.
- **Aceite:** shiny morrendo é capturado com a bola certa.

### ETAPA 7 — Pesca por tile
- **Objetivo:** lançar vara no tile d'água fixo; parar quando N selvagens no range.
- **Como confiro:** `0_AD_fish.lua` (tile do jogo, não pixel; `getSpectatorsInRange`).
- **Depende de:** ETAPA 0 + 2.
- **Status:** decisão pura (gate de lance: settle/cadência ping-aware/pausa maxPoke/cross-busy) em FishingGate.cs testada headless; falta a leitura da vara e do item d'água no tile.

### ETAPA 8 — Features de apoio (auto-hunts, buffs, inspetor, voar, antiafk, guild, caixa-preta)
- Cada uma isolada, mesmo padrão: espelhar a fonte, testar headless a decisão, integrar ao runtime.

---

## 3. Ordem sugerida (maior desbloqueio primeiro)

1. **ETAPA 0** (posição) → destrava INICIAR + GRAVAR ROOTA imediatamente.
2. **ETAPA 1** (party/HP) → Cura + Socorro passam a reagir.
3. **ETAPA 2** (varredura) → Alvo/Combo funcionam de verdade.
4. **ETAPA 3** (walk) → cavebot fica liso.
5. **ETAPA 4+5** (alertas + chat) → Responder-PM, Saída, Vigia fecham o loop.
6. **ETAPA 6,7,8** → captura, pesca, extras.

Cada uma termina com o check acima contra o fonte em `/tmp/opencode/dec/`.
