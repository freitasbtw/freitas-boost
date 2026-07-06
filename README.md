# Freitas Boost

Otimizador de PC focado em FPS, frametime e latencia competitiva em jogos.
O repositorio agora mantem duas implementacoes:

- **WinUI 3 / .NET 10 LTS** em `native/` - nova base nativa para Windows.
- **Electron** em `src/` - versao original preservada durante a migracao.

## Funcionalidades

- **Limpeza de temporarios** - `%Temp%`, `Temp` do Windows, cache web e dumps
  de crash. `Prefetch` e Lixeira ficam preservados no fluxo pre-jogo.
- **Otimizar RAM** - libera memoria presa em processos (`EmptyWorkingSet`),
  devolvendo RAM ao sistema antes de jogar.
- **Encerrar processos** - lista apps em segundo plano por consumo de memoria
  e fecha os selecionados por PID, com protecao para processos criticos, Steam
  e CS2.
- **Modo FPS** - plano de energia de Alto Desempenho, Modo Jogo do Windows,
  Game DVR desligado e cache DNS limpo. Antes de aplicar, salva um snapshot
  para restaurar o estado anterior.
- **Historico local** - salva estados do Windows em `%APPDATA%\Freitas Boost`
  para restaurar depois, mesmo fechando e abrindo o app novamente.
- **Perfil CS2** - detecta GPU/Steam/CS2, mostra o estado do Windows e recomenda
  ajustes manuais com custo-beneficio entre FPS medio, 1% low e latencia.

## Perfil competitivo para CS2

O app separa ganho de FPS de ganho de latencia. Em CS2 competitivo, algumas
opcoes como **NVIDIA Reflex: Enabled + Boost** podem valer mesmo quando custam
alguns FPS, porque reduzem a fila de renderizacao e melhoram resposta de
entrada.

O perfil CS2 trata esses ajustes como recomendacao manual: o app nao injeta nada
no jogo nem altera arquivos do CS2 automaticamente. Para comparar com criterio,
use o mesmo mapa/cenario e observe FPS medio, 1% lows, frametime e latencia
percebida. Ajustes como HAGS e limitador de FPS devem ser testados A/B por
hardware e driver.

## Historico local

O app nao depende de login, cadastro, banco de dados ou servico externo. O
historico fica em um arquivo JSON local dentro de `%APPDATA%\Freitas Boost`.
O app salva snapshots automaticos antes do Modo FPS e tambem permite salvar o
estado atual manualmente pela interface.

## Requisitos

- Windows 10/11
- Para a versao nativa: .NET 10 SDK, Windows App SDK e Windows SDK 10.0.19041+
- Para a versao Electron legada: Node.js 18+ (testado com v24)

> Observacao: o app nativo roda como usuario normal e solicita UAC somente para
> acoes sensiveis por meio do helper `FreitasBoost.AdminHelper`.

## Estrutura nativa

```text
native/
  src/
    FreitasBoost.App/          UI WinUI 3 e fluxo de tela
    FreitasBoost.Core/         modelos, logging e acoes de sistema
    FreitasBoost.AdminHelper/  helper elevado chamado via UAC
```

A versao WinUI substitui a ponte Electron/Node por servicos C#:

- `SystemInfoProvider` usa APIs/registro/powercfg para status do sistema.
- `TempCleaner`, `MemoryOptimizer` e `ProcessManager` executam as otimizacoes em C#.
- `FpsModeManager` e `StateHistoryStore` preservam snapshots em `%APPDATA%\Freitas Boost`.
- `AdminActionClient` chama o helper elevado por arquivos JSON temporarios.
- Logs tecnicos ficam em `%LOCALAPPDATA%\Freitas Boost\logs`.

PowerShell ficou restrito a casos pontuais em que ainda e mais pratico/seguro:
consulta de GPU via CIM no perfil CS2 e limpeza opcional da Lixeira no modo
deep clean.

## Rodando a versao nativa em desenvolvimento

Se `dotnet` estiver no PATH:

```powershell
dotnet restore native\FreitasBoost.Native.slnx /p:NuGetAudit=false /p:RestoreUseStaticGraphEvaluation=true
dotnet build native\FreitasBoost.Native.slnx -c Debug --no-restore /p:NuGetAudit=false
dotnet run --project native\src\FreitasBoost.App\FreitasBoost.App.csproj -c Debug --no-build
```

Se estiver usando um SDK local extraido em `.dotnet`:

```powershell
$env:NUGET_PACKAGES = "$PWD\native\.nuget-packages"
.\.dotnet\dotnet.exe restore native\FreitasBoost.Native.slnx /p:NuGetAudit=false /p:RestoreUseStaticGraphEvaluation=true
.\.dotnet\dotnet.exe build native\FreitasBoost.Native.slnx -c Debug --no-restore /p:NuGetAudit=false
.\.dotnet\dotnet.exe run --project native\src\FreitasBoost.App\FreitasBoost.App.csproj -c Debug --no-build
```

## Gerando build nativo

```powershell
$env:NUGET_PACKAGES = "$PWD\native\.nuget-packages"
dotnet publish native\src\FreitasBoost.App\FreitasBoost.App.csproj -c Release --self-contained false /p:NuGetAudit=false
```

O executavel principal sai em `native\src\FreitasBoost.App\bin\Release\...`.
O helper elevado e copiado para a subpasta `AdminHelper` durante o build.

O repositorio inclui `NuGet.config` apontando para `nuget.org`. Em ambientes onde
o cache global do NuGet fica lento ou bloqueado, use `NUGET_PACKAGES` local como
nos comandos acima.

## Rodando a versao Electron legada

```powershell
npm install
npm start
```

Por padrao, `npm start` solicita elevacao via UAC e abre o Electron como
administrador. Para rodar sem elevacao durante desenvolvimento de UI:

```powershell
npm run start:dev
```

Algumas otimizacoes (limpar `Windows\Temp`, alterar plano de energia e restaurar
estado do Windows) precisam de privilegios de administrador. O app tambem mostra
um indicador no canto superior direito quando for aberto sem elevacao.

## Gerando o instalador Electron legado (.exe)

```powershell
npm run dist
```

O instalador NSIS sera gerado em `dist/`. A versao empacotada ja solicita
elevacao de administrador automaticamente.

## Arquitetura Electron legada

```text
src/
  main/
    main.js            processo principal do Electron + handlers IPC
    powershell.js      executa os scripts .ps1 e devolve JSON
    scripts/*.ps1      cada otimizacao isolada em um script PowerShell
  preload/preload.js   ponte segura (contextBridge) para o renderer
  renderer/            interface (HTML + CSS + JS puro, sem build)
```

O renderer nunca acessa Node diretamente: tudo passa por canais IPC expostos
no `preload.js` (`contextIsolation` ligado, `nodeIntegration` desligado).

## Aviso

Encerrar processos e alterar planos de energia/registro mexe no sistema. Os
servicos nativos e scripts legados trazem protecoes para nao tocar em processos
criticos, mas use com consciencia.
