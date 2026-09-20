# NevesBot / KBot

Aplicativo WPF em .NET 10 com um núcleo C++ que abre e detecta o processo `PokeAlliance.exe`.
O aplicativo consulta o núcleo por named pipe (`KBot.NativePipe`). O projeto ainda está na fase
de conexão, configuração e edição de rotas: os módulos de automação não executam ações no jogo.

## Abrir e executar

1. Abra `NevesBot.slnx` ou `KBot.sln` no Visual Studio com .NET 10 e o toolset C++ v145.
2. Compile em **Debug | x64**.
3. Execute `KBot.App`. Na Visão geral, clique em **Iniciar núcleo**. O aplicativo procura
   `KBot.Native.exe` ao lado do aplicativo ou na saída `x64/Debug` da solução.
4. Com o cliente aberto, a Visão geral mostra o PID e a hora da última verificação.

O núcleo iniciado pelo aplicativo é encerrado quando a janela fecha. Se o núcleo já estiver
em execução por conta própria, o aplicativo apenas se conecta a ele.

No dashboard, **Abrir PokeAlliance** executa ou conecta ao cliente, espera a janela principal,
resolve o PID e o HWND, cria uma `GameSession` e faz a primeira captura da janela. O botão
**Capturar tela** repete a captura usando o HWND guardado na sessão.

## Recursos

- **Cavebot:** importe uma rota JSON, adicione, remova e reordene waypoints e salve no formato KBot.
- **Alvos:** configure a lista de criaturas em ordem de prioridade.
- **Cura e revive:** ajuste limites de HP, teclas de revive e comida e magias F1 a F9 com cooldown.
- **Alertas:** configure alertas e atalhos manuais.
- **Pesca, Captura e Coleta:** guarde as preferências e os atalhos de cada módulo.
- **Configurações:** veja o perfil completo, importe um perfil JSON e salve em
  `%LOCALAPPDATA%\KBot\profile.json`.

Todas as telas de módulo mostram o estado real da integração. As opções salvas ainda não
executam ações no jogo. Consulte [a matriz de recursos](docs/FEATURES.md).

## Verificação

```powershell
dotnet run --project KBot.Tests\KBot.Tests.csproj
dotnet build KBot.App\KBot.App.csproj -p:Platform=x64 -p:OutputPath=bin\SmokePreview\
dotnet run --project KBot.UiSmoke\KBot.UiSmoke.csproj -p:KBotAppOutput=SmokePreview -- "previews\features"
```

`KBot.Tests` valida o mapeamento JSON e o formato de rota. Para verificar arquivos de exemplo,
passe uma pasta de scripts JSON e um perfil JSON como argumentos opcionais. `KBot.UiSmoke`
renderiza as telas WPF para inspeção visual; com `--core`, também testa `PING` e `GET_STATUS`.
