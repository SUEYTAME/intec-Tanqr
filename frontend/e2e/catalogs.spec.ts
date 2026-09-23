import { test, expect } from "@playwright/test";

test("login, crear, editar y desactivar un departamento; cerrar sesión", async ({
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
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Bienvenido" })).toBeVisible();
  await page.screenshot({
    path: `../artifacts/login-${testInfo.project.name}.png`,
    fullPage: true,
  });
  await page.getByLabel("Correo electrónico").fill(email);
  await page.getByLabel("Contraseña", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(
    page.getByRole("heading", { name: "Panel", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Departamentos", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Departamentos", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Nuevo registro" }).click();
  const code = `QA-${Date.now()}-${testInfo.project.name}`;
  const name = `Prueba UI ${testInfo.project.name}`;
  await page.getByLabel("Código", { exact: true }).fill(code);
  await page.getByLabel("Nombre", { exact: true }).fill(name);
  await page.getByRole("button", { name: "Guardar registro" }).click();
  await expect(page.getByRole("status")).toContainText("Registro guardado");
  const row = page.getByRole("row").filter({ hasText: code });
  await expect(row).toContainText(name);
  await row.getByRole("button", { name: "Editar" }).click();
  await page.getByLabel("Nombre", { exact: true }).fill(`${name} finalizada`);
  await page.getByLabel("Registro activo").uncheck();
  await page.getByRole("button", { name: "Guardar registro" }).click();
  await expect(row).toContainText("Inactivo");
  await page.screenshot({
    path: `../artifacts/catalogo-${testInfo.project.name}.png`,
    fullPage: true,
  });
  await page.getByRole("button", { name: "Empleados", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Empleados", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Vehículos", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Vehículos", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Usuarios", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Usuarios", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Auditoría", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Auditoría", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Bienvenido" })).toBeVisible();
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => localStorage.length)).toBe(0);
  expect(await page.evaluate(() => sessionStorage.length)).toBe(0);
});
