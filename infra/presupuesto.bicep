// Presupuesto mensual de TODA la suscripción Azure for Students (crédito USD 100/12 meses):
// avisa por correo antes de que el gasto agote el crédito y Azure deshabilite la suscripción.
// Gasto previsto 2026-09-23: ≈ USD 41.7/mes (demo ~36.3 + SQL db-intec-demo ~5.4). Sin coste.
// Se despliega con scripts/azure-monitoreo.ps1 (az deployment sub create).
targetScope = 'subscription'
param alertEmail string
param amount int = 45
param startDate string = '2026-09-01T00:00:00Z'

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'presupuesto-credito-estudiante'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate, endDate: '2027-09-01T00:00:00Z' }
    notifications: {
      real80: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 80, thresholdType: 'Actual', contactEmails: [ alertEmail ] }
      real100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, thresholdType: 'Actual', contactEmails: [ alertEmail ] }
      previsto100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, thresholdType: 'Forecasted', contactEmails: [ alertEmail ] }
    }
  }
}
