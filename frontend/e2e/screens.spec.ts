import { test, expect } from "@playwright/test";

// Recorre todas las pantallas del administrador y falla ante cualquier error de
// ejecución, respuesta 5xx de la API o pantalla sin título.
const screens: [string, string][] = [
  ["Solicitudes", "Solicitudes"],
  ["Tickets", "Tickets"],
  ["Programaciones", "Programaciones"],
  ["Inventario", "Inventario"],
  ["Reportes", "Reportes"],
  ["Notificaciones", "Notificaciones"],
  ["Departamentos", "Departamentos"],
  ["Empleados", "Empleados"],
  ["Vehículos", "Vehículos"],
  ["Parámetros", "Parámetros de tickets"],
  ["Integraciones", "Integraciones"],
  ["Auditoría", "Auditoría"],
  ["Usuarios", "Usuarios"],
  ["Mi seguridad", "Seguridad"],
];

test("todas las pantallas cargan sin errores y los reportes consultan", async ({
  page,
}, testInfo) => {
  const email = process.env.BOOTSTRAP_EMAIL;
  const password = process.env.BOOTSTRAP_PASSWORD;
  if (!email || !password)
    throw new Error(
      "Ejecuta scripts/probar-interfaz.ps1 para cargar credenciales locales sin imprimirlas.",
    );
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("response", (response) => {
    if (response.url().includes("/api/") && response.status() >= 500)
      errors.push(`${response.status()} ${response.url()}`);
  });
  await page.goto("/");
  await page.getByLabel("Correo electrónico").fill(email);
  await page.getByLabel("Contraseña", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Panel", exact: true })).toBeVisible();
  await page.screenshot({ path: `../artifacts/panel-${testInfo.project.name}.png`, fullPage: true });
  // El administrador no despacha (política dispatch: solo Despachador, ADR-016).
  await expect(page.getByRole("button", { name: "Despacho", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Cierre diario", exact: true })).toHaveCount(0);

  for (const [nav, heading] of screens) {
    await page.getByRole("button", { name: nav, exact: false }).first().click();
    await expect(page.getByRole("heading", { name: heading, exact: true })).toBeVisible();
    await expect(page.getByRole("status").filter({ hasText: "Cargando" })).toHaveCount(0, { timeout: 10000 });
  }

  await page.getByRole("button", { name: "Reportes", exact: true }).click();
  for (const tab of ["Tickets", "Despachos", "Movimientos de inventario", "Consumo"]) {
    await page.getByRole("group", { name: "Tipo de reporte" }).getByRole("button", { name: tab }).click();
    await page.getByRole("button", { name: "Ver reporte" }).click();
    await expect(page.getByText(/Sin resultados|Resultado/).first()).toBeVisible();
    await expect(page.getByRole("status").filter({ hasText: "Consultando" })).toHaveCount(0, { timeout: 10000 });
  }
  await page.screenshot({ path: `../artifacts/reportes-${testInfo.project.name}.png`, fullPage: true });

  await page.goto(`/ticket/${"0".repeat(32)}.${"A".repeat(22)}`);
  await expect(page.getByText("Este enlace no corresponde a ningún ticket")).toBeVisible();

  expect(errors).toEqual([]);
});
