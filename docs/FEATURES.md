# Recursos do KBot

O KBot organiza a configuração dos módulos em um perfil local. Cada módulo tem sua própria
tela no menu lateral. A tela **Configurações** reúne todas as opções e importa perfis JSON.

| Módulo | Disponível na interface | Execução no jogo |
| --- | --- | --- |
| Cavebot | Importar, editar, reordenar e salvar waypoints JSON | Pendente: leitura de posição e navegação |
| Alvos | Lista ordenada de criaturas | Pendente: lista de batalha e seleção de alvo |
| Cura e revive | HP para revive, HP fora da batalha, teclas de revive e comida, magias F1–F9 com cooldown | Pendente: leitura de HP/estado e envio de teclas |
| Alertas | Preferência de alertas e atalhos manuais de pausa/revive | Decisão pronta (morte, nível, jogador saiu, suprimento, capturou); pendente: leitura de chat e de inventário |
| Pesca | Preferência, ponto em pixels e atalho de pesca | Decisão de cadência pronta (ritmo do servidor, pausa por selvagens); pendente: leitura da vara e do ponto d'água |
| Captura | Preferência e atalho | Decisão pronta (bola por corpo + shiny); pendente: leitura dos corpos na tela |
| Coleta | Preferência e atalho | Pendente: reconhecimento dos objetos |

As opções são salvas em `%LOCALAPPDATA%\KBot\profile.json`. Salvar uma preferência não inicia
automação. O núcleo C++ detecta o processo `PokeAlliance.exe` e responde a `PING` e `GET_STATUS`.

## Ciclo da sessão

O dashboard segue `KBot → abre/conecta PokeAlliance → localiza PID → localiza HWND → cria
GameSession → captura a janela`. A sessão mantém o processo e o HWND para as próximas capturas;
ela não envia comandos ao jogo.

## Formato de rota

O importador aceita arquivos de rota JSON com `Waypoints` serializados como strings JSON e o
formato atual do KBot:

```json
{
  "format": "kbot-route-v1",
  "waypoints": [
    {
      "name": "Entrada",
      "position": { "x": 4066, "y": 3458, "z": 5 },
      "action": "Walk"
    }
  ]
}
```

O editor aceita `Walk`, `Wait`, `Talk`, `Use`, `StopAttacker`, `StartAttacker` e
`OrderPokemon`. Essas ações são armazenadas no arquivo; a execução da rota ainda depende
da integração com o cliente do jogo.
