# Freitas Boost

Otimizador de PC nativo para Windows, focado em FPS, frametime e latencia
competitiva em jogos.

O projeto usa somente **C# + .NET + WinUI 3 / Windows App SDK**.

## Funcionalidades

- **Limpeza de temporarios** - `%Temp%`, `Temp` do Windows, cache web e dumps de
  crash. `Prefetch` e Lixeira ficam preservados no fluxo pre-jogo.
- **Otimizar RAM** - libera memoria presa em processos (`EmptyWorkingSet`),
  devolvendo RAM ao sistema antes de jogar.
- **Encerrar processos** - lista apps em segundo plano por consumo de memoria e
  fecha os selecionados por PID, com protecao para processos criticos, Steam e CS2.
- **Modo FPS** - plano de energia de Alto Desempenho, Modo Jogo do Windows,
  Game DVR desligado e cache DNS limpo. Antes de aplicar, salva um snapshot para
  restaurar o estado anterior.
- **Boost consolidado** - limpeza, RAM e Modo FPS em uma unica chamada elevada,
  reduzindo prompts UAC no fluxo principal.
- **Historico local** - salva estados do Windows em `%APPDATA%\Freitas Boost`
  para restaurar depois, comparar snapshots e importar/exportar JSON.
- **Perfil CS2** - detecta GPU/Steam/CS2, mostra o estado do Windows e recomenda
  ajustes manuais com custo-beneficio entre FPS medio, 1% low e latencia.
- **Benchmark CS2 estimado** - calcula uma estimativa conservadora de FPS medio e
  1% low para 1080p competitivo a partir de CPU, GPU, RAM, energia e Game DVR.
- **Diagnostico** - copia um relatorio tecnico com specs, configuracoes, caminhos
  de backup, CS2 e logs recentes para suporte.

## Requisitos

- Windows 10/11
- .NET SDK compatível com o `TargetFramework` do projeto
- Windows App SDK
- Windows SDK 10.0.19041+

O app abre como usuario normal e solicita UAC somente quando uma acao sensivel
precisa do helper elevado `FreitasBoost.AdminHelper`.

## Estrutura

```text
native/
  src/
    FreitasBoost.App/          UI WinUI 3 e fluxo de tela
    FreitasBoost.Core/         modelos, logging e acoes de sistema
    FreitasBoost.AdminHelper/  helper elevado chamado via UAC
```

Componentes principais:

- `SystemInfoProvider` le RAM, CPU, permissao e plano de energia.
- `TempCleaner`, `MemoryOptimizer` e `ProcessManager` executam as otimizacoes.
- `FpsModeManager` e `StateHistoryStore` preservam snapshots em
  `%APPDATA%\Freitas Boost`.
- `AdminActionClient` chama o helper elevado por arquivos JSON temporarios.
- Logs tecnicos ficam em `%LOCALAPPDATA%\Freitas Boost\logs`.

PowerShell fica restrito a casos pontuais em que ainda e mais pratico/seguro,
como consulta de GPU via CIM no perfil CS2 e limpeza opcional da Lixeira no modo
deep clean.

## Rodando em desenvolvimento

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

Ou use o script reproduzivel, que prefere `.dotnet\dotnet.exe` quando existir e
mantem o cache NuGet dentro de `native\.nuget-packages`:

```powershell
.\scripts\build-native.ps1 -Configuration Debug
```

## Gerando build

```powershell
$env:NUGET_PACKAGES = "$PWD\native\.nuget-packages"
.\.dotnet\dotnet.exe publish native\src\FreitasBoost.App\FreitasBoost.App.csproj -c Release --self-contained false /p:NuGetAudit=false
```

Atalho:

```powershell
.\scripts\publish-native.ps1 -Configuration Release
```

O executavel principal sai em `native\src\FreitasBoost.App\bin\Release\...`.
O helper elevado e copiado para a subpasta `AdminHelper` durante o build.

O repositorio inclui `NuGet.config` apontando para `nuget.org`. Em ambientes onde
o cache global do NuGet fica lento ou bloqueado, use `NUGET_PACKAGES` local como
nos comandos acima.

## Aviso

Encerrar processos e alterar planos de energia/registro mexe no sistema. O app
salva snapshots antes de ajustes sensiveis e mostra confirmacoes visiveis, mas
use com consciencia.
