#!/usr/bin/env bash
# Sobe o host de teste do celular em quatro cenarios e roda o teste visual. Uso: rodar.sh <pasta-de-saida>
set -euo pipefail
export TZ=America/Sao_Paulo # horarios das imagens iguais aos do celular
saida="${1:-artifacts/celular}"
raiz="$(mktemp -d)"
aqui="$(cd "$(dirname "$0")" && pwd)"
projeto="$aqui/../Spobras.Celular.Navegador"
dotnet build "$projeto" -c Release -v q
pids=()
porta=5300
for cenario in pronto novo movido semrede; do
  porta=$((porta + 1))
  SPOBRAS_CELULAR_RAIZ="$raiz/$cenario" SPOBRAS_CELULAR_CENARIO="$cenario" SPOBRAS_CELULAR_HOJE="2026-11-12T19:07:00-03:00" \
    ASPNETCORE_URLS="http://127.0.0.1:$porta" dotnet run --project "$projeto" -c Release --no-build > "$raiz/$cenario.log" 2>&1 &
  pids+=($!)
done
trap 'kill ${pids[*]} 2>/dev/null || true' EXIT
for p in 5301 5302 5303 5304; do
  for _ in $(seq 1 60); do curl -sf "http://127.0.0.1:$p/" > /dev/null && break; sleep 1; done
done
node "$aqui/telas.js" "$saida" http://127.0.0.1:5301/ http://127.0.0.1:5302/ http://127.0.0.1:5303/ http://127.0.0.1:5304/
