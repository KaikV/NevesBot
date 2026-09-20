Quero corrigir o fluxo de inicialização do KBot para ficar IGUAL ao comportamento mostrado no vídeo.

O detalhe mais importante é:

AO ABRIR O KBOT, O POKEALLIANCE DEVE SER INICIADO AUTOMATICAMENTE.

A interface principal do bot NÃO deve aparecer imediatamente.

Ela só deve aparecer depois que o usuário fizer login e entrar efetivamente em um personagem dentro do jogo.

==================================================
FLUXO EXATO DESEJADO
====================

Usuário executa:

KBot.exe

↓

KBot inicia em modo Bootstrap/Launcher.

↓

KBot verifica configuração do PokeAlliance.

↓

Se já estiver configurado:

abre automaticamente o launcher/cliente do PokeAlliance.

NÃO exigir que o usuário clique:

"Abrir PokeAlliance".

↓

PokeAlliance abre.

↓

KBot fica rodando em background ou exibindo apenas uma tela pequena de inicialização.

Exemplo:

KBOT

Iniciando PokeAlliance...

ou:

KBOT
Aguardando o jogo...

↓

KBot detecta processo.

↓

obtém PID.

↓

obtém HWND.

↓

estabelece GameSession.

MAS:

NÃO ABRIR A INTERFACE PRINCIPAL DO BOT AINDA.

==================================================
TELA DE LOGIN NÃO LIBERA O BOT
==============================

Se o PokeAlliance estiver:

* no launcher;
* atualizando;
* na tela de login;
* na seleção de personagem;
* carregando;

o KBot deve continuar em:

WAITING_FOR_CHARACTER

A interface completa:

Dashboard
Target
Cura
Captura
Coleta
Pesca
Rota
Alertas
Configurações

NÃO deve aparecer ainda.

==================================================
PERSONAGEM IN-GAME
==================

O KBot precisa possuir um estado separado:

GameProcessDetected

e:

CharacterLoggedIn

Essas duas coisas NÃO são iguais.

Estados sugeridos:

Starting

LaunchingClient

WaitingForProcess

ProcessDetected

WaitingForWindow

ClientConnected

WaitingForLogin

WaitingForCharacter

CharacterDetected

Ready

Disconnected

Error

Fluxo:

PokeAlliance_dx.exe encontrado
↓
ProcessDetected

janela encontrada
↓
ClientConnected

tela de login
↓
WaitingForLogin

usuário entra na conta
↓
WaitingForCharacter

usuário seleciona personagem
↓
carregamento

personagem realmente entra no mapa
↓
CharacterDetected

SÓ ENTÃO:

Ready

↓

mostrar interface completa do KBot.

==================================================
COMO SABER QUE ENTROU NO PERSONAGEM
===================================

Analise primeiro o que o projeto atualmente consegue observar.

Não invente um detector falso.

Crie uma abstração:

CharacterSessionDetector

ou:

InGameStateDetector

Responsabilidade:

determinar:

Unknown

NotLoggedIn

CharacterSelection

Loading

InGame

Não basear a lógica apenas em:

"processo existe".

Não basear somente em:

"janela existe".

Precisamos de uma condição confiável que represente:

PERSONAGEM ESTÁ DENTRO DO JOGO.

Estude o cliente e a infraestrutura atual para determinar a melhor fonte disponível.

Pode utilizar informações legítimas que o KBot já consiga observar externamente ou através do ClientReader que estamos construindo.

Se ainda não existir uma forma confiável:

implemente a arquitetura e marque o detector como:

Unknown

até existir evidência real.

Não inventar offset.

Não inventar endereço.

==================================================
INTERFACE DURANTE INICIALIZAÇÃO
===============================

Não quero mostrar a interface completa atual enquanto o personagem não estiver logado.

Criar uma tela simples de Bootstrap.

Exemplo:

---

KBOT

PokeAlliance

● Cliente iniciado

Aguardando login...

---

Depois:

---

KBOT

PokeAlliance

● Cliente conectado

Aguardando personagem...

---

Depois que detectar InGame:

pequena transição:

Personagem detectado.

Iniciando KBot...

↓

abrir interface principal.

==================================================
SE NÃO TIVER POKEALLIANCE CONFIGURADO
=====================================

Somente na PRIMEIRA execução:

KBot abre:

CONFIGURAR POKEALLIANCE

[ Localizar launcher ]

Usuário seleciona.

Salvar configuração.

Depois:

iniciar automaticamente.

Nas próximas execuções:

usuário abre KBot.exe

↓

PokeAlliance abre automaticamente.

Não perguntar novamente.

==================================================
SE POKEALLIANCE JÁ ESTIVER ABERTO
=================================

Se KBot iniciar e encontrar PokeAlliance rodando:

NÃO abrir outro.

Conectar ao processo existente.

Depois verificar:

personagem já está InGame?

SIM:

abrir interface do KBot imediatamente.

NÃO:

mostrar Bootstrap:

"Aguardando personagem..."

==================================================
SE O PERSONAGEM DESLOGAR
========================

Também quero tratar isso.

Exemplo:

KBot está Ready.

↓

usuário faz logout do personagem.

↓

volta para Character Selection.

KBot detecta:

InGame → NotInGame

Então:

pausar imediatamente módulos de automação.

Não fechar PokeAlliance.

Não perder GameSession.

Mostrar novamente estado:

Aguardando personagem...

Quando outro personagem entrar:

detectar novamente.

↓

reativar sessão.

==================================================
SE O JOGO FECHAR
================

PokeAlliance fecha.

↓

GameSession = Disconnected

↓

ClientReader para.

↓

módulos param.

↓

voltar para tela Bootstrap.

Mostrar:

PokeAlliance foi fechado.

[ ABRIR NOVAMENTE ]

Neste caso pode existir botão manual.

Mas na inicialização normal:

PokeAlliance deve abrir automaticamente.

==================================================
SEPARAÇÃO IMPORTANTE
====================

Quero estes conceitos independentes:

LauncherSession

representa launcher/patcher.

GameSession

representa processo real do jogo.

CharacterSession

representa personagem realmente dentro do jogo.

Fluxo:

LauncherSession
↓
GameSession
↓
CharacterSession
↓
KBot Ready

A interface principal depende de:

CharacterSession.IsInGame == true

e NÃO apenas:

GameSession.IsConnected == true.

==================================================
ARQUITETURA DE ESTADOS
======================

Algo semelhante a:

KBotLifecycle

Booting

ClientNotConfigured

LaunchingClient

WaitingForProcess

WaitingForGame

WaitingForLogin

WaitingForCharacter

Ready

Disconnected

Error

A UI deve reagir ao KBotLifecycle.

Evite espalhar vários bools pela aplicação.

==================================================
COMPORTAMENTO VISUAL
====================

ANTES DO PERSONAGEM:

não mostrar dashboard completo.

Mostrar apenas:

Logo KBot

PokeAlliance

status atual

loading

opções mínimas:

Cancelar
Configurações
Alterar cliente

DEPOIS DO PERSONAGEM:

abrir a interface completa.

Exemplo:

Navbar

Dashboard
Alvo
Cura
Captura
Coleta
Pesca
Rota
Alertas
Configurações

==================================================
OBJETIVO FINAL
==============

Quero reproduzir exatamente esta experiência:

1. dou dois cliques no KBot.exe;

2. KBot inicia;

3. PokeAlliance abre automaticamente;

4. KBot fica aguardando;

5. faço login normalmente;

6. seleciono personagem;

7. entro no jogo;

8. KBot percebe que existe um personagem ativo;

9. interface completa do KBot aparece automaticamente;

10. posso usar o bot.

Não quero:

abrir KBot
↓
clicar Abrir PokeAlliance
↓
clicar Conectar
↓
abrir Dashboard manualmente.

Quero:

KBot
↓
PokeAlliance automaticamente
↓
login
↓
personagem
↓
interface do KBot automaticamente.

==================================================
AUDITORIA ANTES DE IMPLEMENTAR
==============================

Antes de alterar código, analise o projeto atual e responda:

1. já conseguimos abrir PokeAlliance automaticamente?

2. já conseguimos diferenciar launcher do cliente real?

3. já conseguimos detectar PID?

4. já conseguimos detectar HWND?

5. já existe GameSession?

6. existe alguma forma REAL atualmente de detectar:

tela de login

versus

personagem InGame?

7. o que falta para implementar CharacterSession?

8. quais arquivos precisam ser modificados?

Depois implemente o fluxo que já for possível sem inventar dados.

Se CharacterSession ainda depender de descobrir um estado interno confiável, prepare toda a arquitetura e deixe claramente documentado o ponto exato que falta.

==================================================
IMPORTANTE
==========

Preserve:

ProcessManager
Named Pipe
C# ↔ C++
PID
status polling

que já estejam funcionando.

Não recriar infraestrutura.

Não implementar bypass ou evasão de anti-cheat.

O objetivo desta tarefa é exclusivamente reproduzir o FLUXO DE INICIALIZAÇÃO E CICLO DE VIDA mostrado no vídeo.

No final compile toda a solution, corrija erros e informe:

IMPLEMENTADO

PARCIAL

FALTANDO

e principalmente:

COMO O KBOT DETERMINA ATUALMENTE QUE O PERSONAGEM ESTÁ IN-GAME.
