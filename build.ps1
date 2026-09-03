<#
    Builds FireWarriors.dll.

    This machine has no .NET SDK and no Visual Studio. The only in-box C#
    compiler (Framework64\v4.0.30319\csc.exe) understands C# 5 only, and this
    mod's source is C# 6 (string interpolation throughout). The game itself,
    though, ships Roslyn 2.10 in its Managed folder (it embeds RoslynCSharp
    for runtime scripting), so we drive that instead.

    That Roslyn is a netstandard build: compiling *against* it would need
    System.Runtime reference facades which aren't installed here, so every
    Roslyn call below is late-bound. At runtime the CLR unifies those facades
    to mscorlib, which is why this works.

    Usage:
        .\build.ps1              # build to .\build\
        .\build.ps1 -Deploy      # build, then copy into BepInEx\plugins
#>
param(
    [switch]$Deploy,
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\This Land Is My Land"
)

$ErrorActionPreference = "Stop"

$managed = Join-Path $GameDir "This Land Is My Land_Data\Managed"
$core    = Join-Path $GameDir "BepInEx\core"
foreach ($d in @($managed, $core)) {
    if (-not (Test-Path $d)) { throw "Not found: $d  (wrong -GameDir?)" }
}

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "build"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outDll = Join-Path $outDir "FireWarriors.dll"
# src\ in the source repository; alongside the script in the game-folder copy.
$srcCs = Join-Path $root "src\FireWarriorsPlugin.cs"
if (-not (Test-Path $srcCs)) { $srcCs = Join-Path $root "FireWarriorsPlugin.cs" }
if (-not (Test-Path $srcCs)) { throw "Source not found: looked for src\FireWarriorsPlugin.cs and FireWarriorsPlugin.cs under $root" }

# Roslyn's own dependencies live only in the game's Managed folder. Preload
# them, and keep the fallback resolver dead simple - a handler that itself
# calls LoadFrom re-enters for the same name, and without a flat guard that
# recurses until the stack overflows.
$global:FwManaged   = $managed
$global:FwResolving = @{}
foreach ($dep in @("System.Collections.Immutable", "System.Reflection.Metadata")) {
    $p = Join-Path $managed "$dep.dll"
    if (Test-Path $p) { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null }
}
$resolver = [System.ResolveEventHandler]{
    param($sender, $eventArgs)
    $n = (New-Object System.Reflection.AssemblyName $eventArgs.Name).Name
    if ($global:FwResolving.ContainsKey($n)) { return $null }
    $p = Join-Path $global:FwManaged "$n.dll"
    if (-not (Test-Path $p)) { return $null }
    $global:FwResolving[$n] = $true
    try { return [System.Reflection.Assembly]::LoadFrom($p) } catch { return $null }
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

[System.Reflection.Assembly]::LoadFrom("$managed\Microsoft.CodeAnalysis.dll")        | Out-Null
[System.Reflection.Assembly]::LoadFrom("$managed\Microsoft.CodeAnalysis.CSharp.dll") | Out-Null

$refs = @(
    "$managed\mscorlib.dll"
    "$managed\System.dll"
    "$managed\System.Core.dll"
    "$managed\UnityEngine.dll"
    "$managed\UnityEngine.CoreModule.dll"
    "$managed\UnityEngine.AIModule.dll"
    "$managed\UnityEngine.ParticleSystemModule.dll"
    "$managed\UnityEngine.PhysicsModule.dll"
    "$managed\UnityEngine.IMGUIModule.dll"
    "$managed\UnityEngine.TextRenderingModule.dll"
    "$managed\UnityEngine.AnimationModule.dll"
    "$managed\Assembly-CSharp.dll"
    "$managed\Assembly-CSharp-firstpass.dll"
    "$core\BepInEx.dll"
    "$core\0Harmony.dll"
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "Missing reference assembly: $r" } }

$mdRefs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
foreach ($r in $refs) { $mdRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($r)) }

$parseOpts = New-Object Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$text = [Microsoft.CodeAnalysis.Text.SourceText]::From(
    [System.IO.File]::ReadAllText($srcCs), [System.Text.Encoding]::UTF8)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text, $parseOpts, $srcCs)

$opts = New-Object Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
    [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$opts = $opts.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)

# CS1701/CS1702: the game's Unity and BepInEx assemblies reference mscorlib
# 2.0.0.0 while we compile against the 4.0.0.0 one sitting next to them. The
# runtime unifies those - it is exactly what the game itself does at load -
# so these are pure noise, and there are thousands of them.
$suppress = New-Object 'System.Collections.Generic.Dictionary[string,Microsoft.CodeAnalysis.ReportDiagnostic]'
$suppress.Add('CS1701', [Microsoft.CodeAnalysis.ReportDiagnostic]::Suppress)
$suppress.Add('CS1702', [Microsoft.CodeAnalysis.ReportDiagnostic]::Suppress)
$opts = $opts.WithSpecificDiagnosticOptions($suppress)

$trees = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]'
$trees.Add($tree)

$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create("FireWarriors", $trees, $mdRefs, $opts)

# Roslyn has no Emit(string path) overload - emit into a stream ourselves.
$fs = [System.IO.File]::Create($outDll)
try {
    $result = $comp.Emit($fs, $null, $null, $null, $null, $null, [System.Threading.CancellationToken]::None)
} finally { $fs.Dispose() }

$errors = @()
$warns  = @()
foreach ($d in $result.Diagnostics) {
    if ($d.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error)   { $errors += $d }
    if ($d.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning) { $warns  += $d }
}
foreach ($w in $warns)  { "WARN : $($w.ToString())" }
foreach ($e in $errors) { "ERROR: $($e.ToString())" }

if (-not $result.Success) { throw "Build failed ($($errors.Count) error(s))" }

$built = Get-Item $outDll
"Built $($built.FullName) ($($built.Length) bytes)"

if ($Deploy) {
    # BepInEx loads plugin DLLs from bytes rather than mapping them, so the file
    # is not locked while the game runs - but the running game keeps the OLD code
    # until restarted. Refuse to deploy over a live session so that never
    # silently happens.
    if (Get-Process -Name "This Land Is My Land" -ErrorAction SilentlyContinue) {
        throw "The game is running. Close it first, or the new DLL will not be the one in use."
    }
    $target = Join-Path $GameDir "BepInEx\plugins\FireWarriors.dll"
    Copy-Item $outDll $target -Force
    "Deployed to $target"
}
