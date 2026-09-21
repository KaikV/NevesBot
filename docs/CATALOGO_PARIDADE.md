# Paridade com o Catálogo de Módulos v0.2

Documento de acompanhamento do `NevesBot_Catalogo_Modulos_Referencia_v0.2.docx`.

## Estados

- **Implementado**: fluxo funcional e persistido.
- **Parcial**: regra e interface existem, mas parte da integração ainda depende do cliente.
- **Bloqueado por fonte**: o módulo permanece inativo quando o estado necessário é desconhecido.
- **Pendente de especificação**: a referência não confirma o comportamento; nenhuma regra rígida foi inventada.

## Matriz

| Módulo | Estado | Entrega atual | Dependência restante |
|---|---|---|---|
| Info / sessão | Implementado | Launcher para processo real, sessão de personagem e transições registradas no histórico | Validar logout e reconexão em execução real |
| Alvo | Parcial | Prioridade, ignorados, raridade, lock/lost, alcance, aproximação, intervalo, distância e seguimento em batalha | Transporte de criaturas da visão e comando real de seleção/ataque |
| Cura | Bloqueado por fonte | Prioridades, HP separado do jogador/Pokémon, cooldown e opção somente fora de batalha; valores desconhecidos nunca disparam | Leitura confirmada de HP, status e combate |
| Rota | Parcial | Editor, gravação, ordem, argumentos, delay, schema v2, ID e versão; START exige posição confiável; STOP cancela; perda da fonte suspende | Ações Talk/Use/Target e transições de andar confirmadas |
| Captura | Bloqueado por fonte | Delay padrão de 200 ms, prioridade por regra/shiny e proteção contra tentativa duplicada no mesmo cadáver | Identidade/propriedade do cadáver e transporte de itens/bolas |
| Ações | Parcial | Motor por prioridade (`IBotModule`), uma ação por tick e log central de sucesso, recusa e falha | Editor genérico de regras somente após confirmar os controles da referência |
| Itens | Bloqueado por fonte | Primitivas de comandos permanecem sem IDs mágicos | Inventário, item de mapa e ações subir/descer confirmadas |
| EndServer | Pendente de especificação | Configuração existente é marcada como bloqueada; último slot da Pokébar foi corrigido | Confirmar finalidade, comandos e perfil do servidor |
| GUI | Parcial | Painel operacional por módulos e estados visíveis | Estilos de alvo e overlay confirmados |
| Macro | Parcial | Pesca, anti AFK, vigia e resposta privada continuam isolados por módulo | Confirmar utilidades globais restantes |
| Alertas | Implementado localmente | Severidade, origem, código, contexto, timestamp, histórico limitado e deduplicação | Notificação externa opcional |

## Regras de segurança de dados

1. Estado desconhecido permanece `null` e bloqueia a regra dependente.
2. Diagnóstico usa `BLOQUEADO` ou `PARCIAL` quando a fonte ainda não existe.
3. Rota não envia movimento antes de `NativeOnline + ClientFound + HasPosition + ReaderStatus=READY`.
4. Captura não usa o estado de batalha como substituto para um cadáver confirmado.
5. EndServer e Itens não recebem offsets, IDs ou comandos presumidos.

## Persistência

- Perfil de automação: `SchemaVersion = 2`.
- Rotas: `kbot-route-v2` com `schemaVersion`, `routeId`, `name`, `version`, ordem, posição, ação, argumento e delay.
- Importação de rotas v1/legadas continua aceita e é migrada ao salvar.
