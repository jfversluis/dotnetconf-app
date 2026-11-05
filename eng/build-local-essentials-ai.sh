#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd -P)"
cd "$root"

revision=e616af94cc1642e782b1deac433eb6a582cdce92
version=11.0.0-local.e616af94
source_dir="${ESSENTIALS_AI_SOURCE_DIR:-$root/artifacts/essentials-ai-source}"
feed="$root/artifacts/essentials-ai-feed"
targets="$root/eng/essentials-ai-local.targets"

case "$(uname -s)" in
  Darwin) apple=true ;;
  Linux) apple=false; version="$version.linux" ;;
  *) echo "This temporary package build supports macOS and Linux." >&2; exit 1 ;;
esac

if [[ ! -d "$source_dir/.git" ]]; then
  if [[ -e "$source_dir" ]]; then
    echo "The source directory already exists and is not a Git checkout: $source_dir" >&2
    exit 1
  fi
  git init -q "$source_dir"
  git -C "$source_dir" remote add origin https://github.com/dotnet/maui-labs.git
  git -C "$source_dir" fetch --quiet --depth 1 origin "$revision"
  git -C "$source_dir" checkout --quiet --detach FETCH_HEAD
fi

if [[ "$(git -C "$source_dir" rev-parse HEAD)" != "$revision" ]] ||
   [[ -n "$(git -C "$source_dir" status --porcelain --untracked-files=normal)" ]]; then
  echo "The Essentials.AI source checkout must be clean and at $revision." >&2
  exit 1
fi

project="$source_dir/src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj"
mkdir -p "$feed"
common=(-p:DirectoryBuildTargetsPath="$targets" -p:EssentialsAILocalApple="$apple")

dotnet restore "$project" --configfile "$source_dir/NuGet.config" "${common[@]}" -p:PackageVersion="$version"
dotnet pack "$project" --no-restore -c Release -o "$feed" "${common[@]}" -p:PackageVersion="$version"

# CI builds the complete package on macOS and also supplies the Linux-specific
# version; Linux developers can build that version without Apple workloads.
if [[ "$apple" == true ]]; then
  dotnet pack "$project" --no-build --no-restore -c Release -o "$feed" "${common[@]}" -p:PackageVersion="$version.linux"
fi

package="$feed/Microsoft.Maui.Essentials.AI.$version.nupkg"
contents="$(unzip -Z1 "$package")"
for required in \
  'lib/net10.0/Microsoft.Maui.Essentials.AI.dll' \
  'lib/net10.0-android36.0/genai-prompt-1.0.0-beta5.aar' \
  'lib/net10.0-android36.0/genai-common-1.0.0-beta5.aar'; do
  if ! grep -Fx "$required" <<<"$contents" >/dev/null; then
    echo "Local package is missing $required." >&2
    exit 1
  fi
done
if [[ "$apple" == true ]] && ! grep -Fx 'lib/net10.0-ios26.0/Microsoft.Maui.Essentials.AI.resources.zip' <<<"$contents" >/dev/null; then
  echo "Local package is missing the iOS native framework." >&2
  exit 1
fi

assets="$source_dir/artifacts/obj/Microsoft.Maui.Essentials.AI/project.assets.json"
notices="$root/src/DotNetConf.App/Resources/Raw"
python3 "$root/eng/verify-local-essentials-notices.py" "$package" "$assets" "$notices"
if [[ "$apple" == true ]]; then
  python3 "$root/eng/verify-local-essentials-notices.py" \
    "$feed/Microsoft.Maui.Essentials.AI.$version.linux.nupkg" "$assets" "$notices"
fi

echo "Built local Essentials.AI from $revision: $package"
