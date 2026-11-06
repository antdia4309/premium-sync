#!/usr/bin/env bash
# Starts all three services and the dashboard. Ctrl+C stops everything.
set -e
dotnet build --nologo -v q
[ -d dashboard/node_modules ] || (cd dashboard && npm install --no-audit --no-fund)
trap 'kill 0' EXIT
(cd src/PremiumSync.BillingService && dotnet run --no-build --launch-profile http) &
(cd src/PremiumSync.PolicyService && dotnet run --no-build --launch-profile http) &
(cd src/PremiumSync.Reconciliation && dotnet run --no-build --launch-profile http) &
(cd dashboard && npm run dev --silent) &
echo "Dashboard:      http://localhost:5173"
echo "Policy:         http://localhost:5001/scalar"
echo "Billing:        http://localhost:5002/scalar"
echo "Reconciliation: http://localhost:5003/scalar"
wait
