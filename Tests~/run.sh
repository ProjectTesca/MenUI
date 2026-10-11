#!/usr/bin/env bash
# Runs MenUI's headless tests (reactivity and layout) on the .NET runtime that
# ships with the Unity editor — no Unity instance needed.
#   UNITY_EDITOR_DATA=/path/to/Editor/Data Packages/io.tesca.menui/Tests~/run.sh
# Run from the Unity project root (it reads Library/ScriptAssemblies).
set -euo pipefail
U="${UNITY_EDITOR_DATA:-$HOME/Unity/Hub/Editor/2022.3.22f1/Editor/Data}"
NET=$(ls -d "$U"/NetCoreRuntime/shared/Microsoft.NETCore.App/* | head -1)
PACKAGE="Packages/io.tesca.menui"
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

{
  echo -nostdlib; echo -noconfig; echo -langversion:9; echo -target:exe
  echo -main:MenUI.Harness; echo -define:MENUI_HEADLESS; echo -nowarn:0169,0649,0414,1701,1702; echo "-out:$OUT/Tests.dll"
  for f in "$NET"/*.dll; do
    case $(basename "$f") in Microsoft.DiaSymReader*|mscordaccore*|clr*|*Native*) ;; *) echo "-r:$f";; esac
  done
  for m in UnityEngine.CoreModule UnityEngine.TextRenderingModule UnityEngine.UIModule; do
    echo "-r:$U/Managed/UnityEngine/$m.dll"
  done
  echo "-r:Library/ScriptAssemblies/Unity.TextMeshPro.dll"
  echo "-r:Library/ScriptAssemblies/UnityEngine.UI.dll"
  # Device sources (VRChat SDK) and the behaviour itself stay out: the rest must not need them.
  find "$PACKAGE/Runtime" -name '*.cs' -not -path '*Verification*' -not -name MenUIApp.cs -not -name MaterialApp.cs -not -name DesktopPointerSource.cs -not -name WebImages.cs
  echo "$PACKAGE/Tests~/HeadlessTests.cs"
} > "$OUT/build.rsp"

"$U/NetCoreRuntime/dotnet" "$U/DotNetSdkRoslyn/csc.dll" -nologo "@$OUT/build.rsp" | grep -v warning || true
cp "$U/Managed/UnityEngine/UnityEngine.CoreModule.dll" "$U/Managed/UnityEngine/UnityEngine.SharedInternalsModule.dll" "$OUT/"
VERSION=$(basename "$NET")
echo "{ \"runtimeOptions\": { \"tfm\": \"net6.0\", \"framework\": { \"name\": \"Microsoft.NETCore.App\", \"version\": \"$VERSION\" } } }" > "$OUT/Tests.runtimeconfig.json"
"$U/NetCoreRuntime/dotnet" "$OUT/Tests.dll"
