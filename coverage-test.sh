#!/usr/bin/env bash
set -e

RESULTS_DIR="./coverage-results"
HTML_DIR="./coverage-html"

rm -rf "$RESULTS_DIR" "$HTML_DIR"

dotnet test tests/LegalManager.UnitTests/LegalManager.UnitTests.csproj \
  --settings tests/LegalManager.UnitTests/coverage.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory "$RESULTS_DIR"

reportgenerator \
  -reports:"$RESULTS_DIR/**/coverage.cobertura.xml" \
  -targetdir:"$HTML_DIR" \
  -reporttypes:Html

echo ""
echo "Coverage report generated at $HTML_DIR/index.html"

if command -v xdg-open &>/dev/null; then
  xdg-open "$HTML_DIR/index.html"
elif command -v open &>/dev/null; then
  open "$HTML_DIR/index.html"
elif command -v start &>/dev/null; then
  start "$HTML_DIR/index.html"
fi
