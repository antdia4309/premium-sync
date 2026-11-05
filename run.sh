#!/usr/bin/env bash
# Starts all three services. Ctrl+C stops them all.
set -e
dotnet build --nologo -v q
trap 'kill 0' EXIT
(cd src/PremiumSync.BillingService && dotnet run --no-build --launch-profile http) &
(cd src/PremiumSync.PolicyService && dotnet run --no-build --launch-profile http) &
(cd src/PremiumSync.Reconciliation && dotnet run --no-build --launch-profile http) &
echo "Policy:         http://localhost:5001/scalar"
echo "Billing:        http://localhost:5002/scalar"
echo "Reconciliation: http://localhost:5003/scalar"
wait
