#!/usr/bin/env bash
set -euo pipefail

# Build KSP-sOS for KSP 1.12.5. The output is a managed DLL and is portable
# across Windows, macOS, and Linux; only the KSP reference assemblies are
# platform-specific and are read from the local KSP installation.

if [[ -n "${KSP_MANAGED:-}" ]]; then
    managed="$KSP_MANAGED"
elif [[ -n "${KSP_DIR:-}" ]]; then
    managed="$KSP_DIR/KSP_x64_Data/Managed"
else
    case "$(uname -s)" in
        Darwin)
            managed="${HOME}/Library/Application Support/Steam/steamapps/common/Kerbal Space Program/KSP.app/Contents/Resources/Data/Managed"
            ;;
        Linux)
            managed="${HOME}/.steam/steam/steamapps/common/Kerbal Space Program/KSP_x64_Data/Managed"
            ;;
        *)
            echo "Set KSP_MANAGED to KSP 1.12.5's Managed directory." >&2
            exit 1
            ;;
    esac
fi

if [[ ! -f "$managed/Assembly-CSharp.dll" ]]; then
    echo "KSP 1.12.5 assemblies not found: $managed" >&2
    echo "Set KSP_MANAGED to the game's KSP_x64_Data/Managed directory." >&2
    exit 1
fi

mkdir -p GameData/KSPsOS/Plugins

mcs -target:library -sdk:4.7 -langversion:latest \
    -out:GameData/KSPsOS/Plugins/KSPsOS.dll \
    -r:"$managed/Assembly-CSharp.dll" \
    -r:"$managed/UnityEngine.dll" \
    -r:"$managed/UnityEngine.CoreModule.dll" \
    -r:"$managed/UnityEngine.IMGUIModule.dll" \
    -r:"$managed/UnityEngine.InputLegacyModule.dll" \
    -r:"$managed/UnityEngine.TextRenderingModule.dll" \
    -r:"$managed/UnityEngine.UI.dll" \
    src/*.cs

echo "Built GameData/KSPsOS/Plugins/KSPsOS.dll for KSP 1.12.5"
