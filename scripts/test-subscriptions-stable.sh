#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

TEST_FILTER="FullyQualifiedName~SubscriptionTests|FullyQualifiedName~SubscriptionServiceTests|FullyQualifiedName~BotUpdateHandlerTests"

dotnet build-server shutdown >/dev/null || true

# Remove transient restore artifacts to avoid occasional stale-file collisions in obj/.
find src tests -type f \( -name "*.nuget.g.props" -o -name "*.nuget.g.targets" -o -name "project.assets.json" \) -delete

dotnet restore DayTrace.sln -m:1 /p:RestoreUseStaticGraphEvaluation=false

dotnet test tests/DayTrace.Tests/DayTrace.Tests.csproj \
  --no-restore \
  --disable-build-servers \
  -m:1 \
  --filter "$TEST_FILTER" \
  -v minimal
