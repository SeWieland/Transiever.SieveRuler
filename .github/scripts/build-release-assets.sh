#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: build-release-assets.sh <version>}"
project="src/Transiever.SieveRuler.Cli/Transiever.SieveRuler.Cli.csproj"
artifacts="artifacts"
publish_root="$artifacts/publish"

mkdir -p "$artifacts" "$publish_root"

dotnet pack "$project" --configuration Release \
  -p:PackageVersion="$version" \
  --output out

for rid in win-x64 win-x86; do
  output="$publish_root/$rid"
  dotnet publish "$project" --configuration Release --runtime "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:Version="$version" \
    --output "$output"

  (
    cd "$output"
    zip -r "../../srtx-$rid.zip" .
  )
done

# .NET has no portable linux-x86 RID, so publishing one would fail.
rid="linux-x64"
output="$publish_root/$rid"
dotnet publish "$project" --configuration Release --runtime "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:Version="$version" \
  --output "$output"

tar -czf "$artifacts/srtx-$rid.tar.gz" -C "$output" .
