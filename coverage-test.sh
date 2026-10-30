#!/usr/bin/env bash
# Mesma cobertura da CI (unit + integration, relatório combinado).
# Requer: dotnet tool install --global dotnet-reportgenerator-globaltool
set -e

RESULTS_DIR="./coverage-results"
HTML_DIR="./coverage-html"

rm -rf "$RESULTS_DIR" "$HTML_DIR"

for proj in tests/LegalManager.UnitTests tests/LegalManager.IntegrationTests; do
  dotnet test "$proj" \
    --settings tests/coverage.runsettings \
    --collect:"XPlat Code Coverage" \
    --results-directory "$RESULTS_DIR"
done

reportgenerator \
  -reports:"$RESULTS_DIR/**/coverage.cobertura.xml" \
  -targetdir:"$HTML_DIR" \
  -reporttypes:"Html;TextSummary"

echo ""
cat "$HTML_DIR/Summary.txt"
echo ""
echo "Coverage report generated at $HTML_DIR/index.html"

if command -v xdg-open &>/dev/null; then
  xdg-open "$HTML_DIR/index.html"
elif command -v open &>/dev/null; then
  open "$HTML_DIR/index.html"
elif command -v start &>/dev/null; then
  start "$HTML_DIR/index.html"
fi
