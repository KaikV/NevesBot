Componente / Botão,O que faz,Parâmetros e Configuração Ideal
Reviver Sozinho,Usa o item de revive automaticamente quando o Pokémon morre.,Ative a flag. Selecione o item (Verde/Vermelho). Clique no ícone do Pokémon padrão da sua barra (não precisa trocar para cada Pokémon).
Auto Medicine,Remove status de controle de grupo (CC) como Sleep ou paralisia.,"Trade-off: Curar Status espera o combo bater para limpar (economiza item, mas arriscado). Medicine All limpa no instante do hit. Recomendação: Use Medicine All ativado para não travar a caminhada."
Regen Player,Cura a vida do seu personagem usando uma skill de cura do Pokémon.,Defina para 99%. Selecione o Pokémon e a respectiva skill. Crucial: Deixe Só fora de batalha LIGADO para evitar cancelamento de animação no meio de um wave de monstros.
Auto Potion,Usa poções de vida baseadas no HP restante.,"Para setups otimizados mid/end-game com Regen ativo, é secundário. Se usar, defina o item via aba Pick Item."
Auto Summon,Puxa o Pokémon de volta instantaneamente caso o jogo ou a Pokebar buguem.,Ative e selecione o Pokémon principal da hunt (ex: Steelix). Funciona como fail-safe.


Componente / Botão,O que faz,Parâmetros e Configuração Ideal
Atacar 1 por 1,Foca em atacar monstros individualmente no target.,"Trade-off: Seguro para contas low-level (sem Pokémon de AoE), mas destrói a XP/hora. Recomendação: DESLIGADO a partir do level 60."
Combo Juntar,Quantidade de monstros na tela necessários para iniciar o combo.,Geralmente 5 ou 6. Depende da densidade da cave.
Combo em Área,Define o raio de tiles ao redor do seu Pokémon que será monitorado.,Padrão recomendado: 2 (Cobre uma área 2x2 ao redor).
Agrupar + Combo,Condição estrita: só comba quando a área definida estiver estourada de monstros (ex: 8 em volta).,Recomendação: LIGADO. Evita que você inicie o combo prematuramente e tome hitkill de monstros que ficaram fora da área de dano.
Combo esperar borda,Delay que obriga o bot a esperar monstros visíveis de longe chegarem perto antes de soltar as skills.,Use valores entre 1500ms e 3000ms. Mais que isso diminui sua XP/h.
Combo se vida <,Panic button. Quebra as regras de quantidade e comba imediatamente se a vida cair criticamente.,Recomendado: 35% para o combo e 35% para Parar se vida <.
Delay antes de revive,O tempo total de animação das skills do seu combo.,"Ajuste rigorosamente. Ex: 1250ms. Se for muito rápido, ele tenta reviver antes do combo acabar e falha. Se muito lento, toma dano extra."


Componente / Botão,O que faz,Parâmetros e Configuração Ideal
Gravar Rota,Registra os tiles clicados.,"Regra de ouro: Faça o caminho em loop (volte ao início) e pare a gravação em um tile seguro, sem sobrepor o tile inicial exato."
Voltar a andar com,Ignora monstros restantes para não atrasar a rota.,"Recomendado: 1. Se sobrar 1 bicho vivo após o combo, o bot segue viagem em vez de gastar um combo/revive inteiro nele."
Destravar se travado,Teleporta o Pokémon para o jogador se ele ficar enroscado em blocos/paredes.,Use 10s. Impede que o personagem fique parado na cave tomando hit.
Ação durante Lure,Define a postura do Pokémon enquanto você anda juntando a wave.,"Trade-off: Front puxa monstros de longe melhor, mas falha no Pokestop de precisão. Melhor Tile procura área limpa. Recomendação: Use Ordem Inteligente para o bot nunca posicionar o Pokémon colado na parede, maximizando a área do combo."
Buff ao Lurar,Usa skills de suporte enquanto anda.,Insira a flag do teclado (ex: M9 ou F9) se o Pokémon tiver buff de defesa ou velocidade constante.


Componente / Botão,O que faz,Parâmetros e Configuração Ideal
Capturar Shinies,Arremessa pokebolas em shinies ignorando as regras de combo.,LIGADO. Deixe Delay de captura em 0 (delega o timing ao cliente do jogo).
Configurar Pokémon,Adiciona monstros específicos à lista de catch.,Adicione (ADD) -> Selecione a Pokébola -> Selecione a sprite do corpo morto.
Regra de Fuga (Alertas),Programa uma ação complexa ao identificar um jogador.,"Em Alertas -> Marcar Aqui: Marque um waypoint fora da rota de farm (escondido). Ao ver jogador, mande ele enviar uma mensagem humanizada (""opa, mals"", ""to saindo""), andar até a safe zone e aplicar o Logout Automático."



Auto Hunt (Instâncias)	Clica automaticamente para entrar em dungeons assim que libera vaga.	Inspecione o ID da porta com Ctrl+Shift. Coloque o Recarregar a cada em 15 segundos. Trade-off: Se colocar 2 segundos, seu client fará flood de requisições de entrada/saída, chamando atenção dos logs do servidor.
Ativar Item Auto	Religa consumíveis (Charms de XP, Food, etc) quando acabam.	Selecione o item, escolha Usar em mim. Deixe ligado. Evita perda de XP passiva por esquecer de renovar buff.
Importar Config	Carrega um preset JSON/Hash (ex: setup do amigo).	Fica na aba Ajustes. Garante reutilização de passos.
Atalhos (Hotkeys)	Associa teclas de atalho para os módulos core.	Configure botões dedicados para: 1. Mostrar/Esconder UI do Bot. 2. Ligar/Desligar Bot Geral. 3. Ligar/Desligar Cavebot (Rota).


Flag / Parâmetro,O que faz,Trade-off / Pitfall,Recomendação
Ignorar Inacessíveis,Pula monstros que estejam do outro lado de uma parede ou buraco.,Evita que o bot fique andando contra a parede tentando dar target.,LIGADO.
Velocidade de Andar,Adiciona um delay fixo a cada passo (em ms).,Deixar muito alto quebra a XP/h. Ajuda em hunts com respawn muito lento.,0 ms (andar liso).
Atraso por Monstro,Lentidão dinâmica: o bot anda mais devagar de acordo com o nº de bichos na tela.,"Ajuda o Pokémon a não ""descolar"" do personagem em lures massivos.",25ms a 100ms para hunts pesadas; 0 ms em fáceis.
Esperar Novo Pokémon,"Se o bot identificar um bicho chegando de longe, ele pausa o combo e espera ele entrar na área.","Economiza Revives agrupando todos no mesmo combo, mas diminui o ritmo do farm.",3000ms a 5000ms (apenas em áreas de risco de morte).
Skill Defensiva,Usa habilidades como Roar do Steelix quando pressionado.,Requer configuração de gatilho (ex: +3 selvagens na tela) e o cooldown exato (ex: 40000ms).,Utilizar apenas para Pokémon tankers como engage.
Distância Segura (Revive),O boneco anda X tiles para longe do monstro antes de conjurar o Revive.,Pitfall: Recuar pode lurar monstros de outras salas e causar morte instantânea (trap).,0 a 2 tiles. Evite usar em caves apertadas


Componente,Função e Comportamento,Recomendação de Uso
Auto Fly,Clica no minimapa e o personagem voa de Pidgeot/etc até o destino usando pathfinding global.,Excelente para fugir rapidamente ou cruzar continentes 100% AFK. Deixe as flags de Auto Fly ligadas.
Auto Anúncio (Trade),Envia mensagens comerciais programadas no canal selecionado a cada 5 minutos.,Automação de venda de loot. Especifique o nome do canal exatamente como no jogo.
Girar (Anti-GM),"Se um Game Master (GM) aparecer e pedir verificação (ex: ""gire o personagem""), o bot rotaciona os tiles automaticamente.","Perigoso: Se você rodar em 5 contas na mesma tela e todas girarem em sincronia perfeita, o GM dará ban na farm inteira. Use com cautela e intervalos variados."



4. Aba Guild & Ajustes (End-Game e Sistemas)
Gerenciamento passivo de recursos e comportamento estrito da interface.

Sistema de Guild (Auto Missões):

Pegar Missão Automático: Seleciona alvos como Primal assim que a janela de 24h resetar.

Auto Ticket: Usa tickets de reset de missão de guilda no inventário para emendar quests seguidas sem intervenção. Trade-off: Pode torrar todo seu estoque de tickets em quests de baixa recompensa se não filtrado.

Sistema de Pesca: Clica automaticamente nos ícones de peixe espalhados pela água. O próprio desenvolvedor recomenda não focar nisso no Poke Alliance, dado o baixo retorno financeiro comparado ao farm bruto.

Aproximação do Pokémon (Ordem): Determina a velocidade com que o comando Order é clicado para trazer o Pokémon para perto. Recomendado: 500ms a 1000ms. Menos que isso causará bloqueio por spam no servidor.

Auto Reconnect: Reloga após um kick ou queda de servidor. Pitfall: Se o servidor voltar travado (lag) ou você nascer no meio de um respawn bruto sem interface carregada, seu bot ficará morrendo em loop infinito. Recomendação: Deixar DESLIGADO, exceto se operar scripts blindados em áreas 100% safe.

Comportamento do Pokémon: Define se a IA de ataque nativa será Ofensiva, Defensiva ou Híbrida. Para bots focados em AoE combo (dano em área estático), mantenha em Ofensivo.


1. Aba Inicial (Controles Globais)
Painel de acesso rápido para ativar/desativar módulos em tempo real sem precisar navegar profundamente na UI.

Ligar/Desligar Módulos Core: Chaves (Toggles) para ativar Auto Combo, Auto Lure, e Sistemas de Cura rapidamente. Útil para pausar o bot ao conversar com NPCs ou organizar o inventário sem precisar fechar o software.

Proxy System: O bot suporta Proxies SOCKS/HTTP via IP.

Por que usar? Essencial para multiboxing (rodar 2+ contas). Evita IP tracking pelos administradores. O criador afirma que até proxies baratos (US$ 0,30) funcionam, desde que isolem a conexão.

2. Painel de Alvo (Delays Avançados e Movimento)
Configurações de fine-tuning que afetam diretamente o algoritmo de pathfinding (caminho) e targeting em situações de risco..