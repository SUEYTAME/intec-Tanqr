import { expect, test } from "@playwright/test";
import { randomBytes, randomUUID } from "node:crypto";

type Role = "Supervisor" | "Despachador" | "Auditor" | "Consulta";

const expected = {
  Administrador: {
    heading: "Panel",
    navigation: [
      "Tablero", "Solicitudes", "Tickets", "Programaciones", "Inventario", "Reportes", "Notificaciones",
      "Departamentos", "Empleados", "Vehículos", "Parámetros", "Integraciones", "Auditoría", "Usuarios", "Mi seguridad",
    ],
  },
  Supervisor: {
    heading: "Panel",
    navigation: [
      "Tablero", "Solicitudes", "Tickets", "Programaciones", "Inventario", "Reportes", "Notificaciones",
      "Departamentos", "Empleados", "Vehículos", "Mi seguridad",
    ],
  },
  Despachador: {
    heading: "Despacho",
    navigation: ["Tickets", "Despacho", "Cierre diario", "Mi seguridad"],
  },
  Auditor: {
    heading: "Panel",
    navigation: ["Tablero", "Reportes", "Auditoría", "Mi seguridad"],
  },
  Consulta: {
    heading: "Solicitudes",
    navigation: ["Solicitudes", "Tickets", "Mi seguridad"],
  },
} as const;

test("cada rol ve solamente los módulos que le corresponden", async ({ page, request }) => {
  const adminEmail = process.env.BOOTSTRAP_EMAIL;
  const adminPassword = process.env.BOOTSTRAP_PASSWORD;
  if (!adminEmail || !adminPassword)
    throw new Error("Ejecuta scripts/probar-interfaz.ps1 para cargar credenciales locales sin imprimirlas.");

  const adminLogin = await request.post("/api/auth/login", {
    data: { email: adminEmail, password: adminPassword },
  });
  expect(adminLogin.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await adminLogin.json()).accessToken}` };
  const password = randomBytes(24).toString("base64");
  const users: { id: string; email: string; role: Role }[] = [];

  try {
    for (const role of ["Supervisor", "Despachador", "Auditor", "Consulta"] as const) {
      const email = `qa-menu-${role.toLowerCase()}-${randomUUID()}@example.test`;
      const response = await request.post("/api/usuarios/", {
        headers,
        data: { email, password, displayName: `Prueba menú ${role}`, role },
      });
      expect(response.status()).toBe(201);
      users.push({ id: (await response.json()).id, email, role });
    }

    const cases = [
      { role: "Administrador" as const, email: adminEmail, password: adminPassword },
      ...users.map((user) => ({ role: user.role, email: user.email, password })),
    ];
    for (const current of cases) {
      await page.goto("/");
      await page.getByLabel("Correo electrónico").fill(current.email);
      await page.getByLabel("Contraseña", { exact: true }).fill(current.password);
      await page.getByRole("button", { name: "Iniciar sesión", exact: true }).click();
      await expect(page.getByRole("heading", { name: expected[current.role].heading, exact: true })).toBeVisible();
      await expect.poll(async () =>
        (await page.locator("nav button").allTextContents()).map((label) =>
          label.trim().replace(/ \((?:\d+|\?)\)$/, ""),
        ),
      ).toEqual(expected[current.role].navigation);
      await page.getByRole("button", { name: "Cerrar sesión", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Bienvenido" })).toBeVisible();
    }
  } finally {
    for (const user of users) {
      let version: string | undefined;
      for (let pageNumber = 1; !version; pageNumber++) {
        const list = await request.get(`/api/usuarios/?page=${pageNumber}`, { headers });
        expect(list.ok()).toBeTruthy();
        const data = (await list.json()) as { items: { id: string; version: string }[] };
        expect(data.items.length, "usuario de prueba no encontrado").toBeGreaterThan(0);
        version = data.items.find((item) => item.id === user.id)?.version;
      }
      const disabled = await request.put(`/api/usuarios/${user.id}/acceso`, {
        headers,
        data: { role: user.role, active: false, version },
      });
      expect(disabled.status()).toBe(204);
    }
    await request.post("/api/auth/logout", { headers });
  }
});
