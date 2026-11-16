#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Install .NET 10 SDK for macOS: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi
echo "Checking .NET SDK selection..."
dotnet --version
echo "Building portable Core library (not the Windows interface)..."
dotnet build src/Seekr.Core/Seekr.Core.csproj -c Release --nologo
echo "Checking duplicates, cleanup, undo and completion on temporary files..."
dotnet run --project tools/Seekr.PortableCheck -c Release
echo "Running query-parser tests only. Windows path/locking tests run on Windows."
dotnet test tests/Seekr.Core.Tests/Seekr.Core.Tests.csproj -c Release --filter FullyQualifiedName~SearchQueryTests --nologo
echo "Core checks complete. Run the full solution and WPF interface on Windows before release."
