import { test, expect } from "@playwright/test";
import { createHmac, randomBytes, randomUUID } from "node:crypto";

test.use({ screenshot: "off" });

// Algoritmo TOTP independiente del servidor, como una aplicación autenticadora.
function totp(secret: string): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let value = 0;
  let bits = 0;
  const key: number[] = [];
  for (const char of secret) {
    value = (value << 5) | alphabet.indexOf(char);
    bits += 5;
    if (bits >= 8) {
      bits -= 8;
      key.push((value >> bits) & 255);
    }
  }
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hash = createHmac("sha1", Buffer.from(key)).update(counter).digest();
  const number = hash.readUInt32BE(hash[19]! & 15) & 0x7fffffff;
  return String(number % 1000000).padStart(6, "0");
}

test("MFA: alta, recuperación, regeneración y baja desde Mi cuenta", async ({
  page,
  request,
}) => {
  const adminLogin = await request.post("/api/auth/login", {
    data: {
      email: process.env.BOOTSTRAP_EMAIL,
      password: process.env.BOOTSTRAP_PASSWORD,
    },
  });
  expect(adminLogin.ok()).toBeTruthy();
  const headers = {
    Authorization: `Bearer ${(await adminLogin.json()).accessToken}`,
  };
  const email = `qa-mfa-${randomUUID()}@example.test`;
  const password = randomBytes(24).toString("base64");
  const created = await request.post("/api/usuarios/", {
    headers,
    data: {
      email,
      password,
      displayName: "Prueba UI MFA",
      role: "Consulta",
    },
  });
  expect(created.status()).toBe(201);
  const id = (await created.json()).id;
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  async function login(recovery?: string) {
    await page.getByLabel("Correo electrónico").fill(email);
    await page.getByLabel("Contraseña", { exact: true }).fill(password);
    if (recovery) {
      await page
        .getByText("Usar código de autenticación", { exact: true })
        .click();
      await page
        .getByLabel("Código de recuperación (si no tienes el autenticador)")
        .fill(recovery);
    }
    await page
      .getByRole("button", { name: "Iniciar sesión", exact: true })
      .click();
    await page
      .getByRole("button", { name: "Mi seguridad", exact: true })
      .click();
  }
  try {
    await page.goto("/");
    await login();
    await page.getByLabel("Confirma tu contraseña").fill(password);
    await page.getByRole("button", { name: "Preparar autenticador" }).click();
    await expect(page.locator("code.setup-key")).toBeVisible();
    const secret = (await page.locator("code.setup-key").textContent())!;
    await page.getByLabel("Código de seis dígitos").fill(totp(secret));
    await page.getByRole("button", { name: "Confirmar y activar" }).click();
    await expect(page.locator("pre.setup-key")).toBeVisible();
    const recovery = (await page.locator("pre.setup-key").textContent())!.split(
      "\n",
    )[0]!;
    await page
      .getByRole("button", { name: "He guardado los códigos · Iniciar sesión" })
      .click();
    await login(recovery);
    await page.getByLabel("Contraseña actual").fill(password);
    await page.getByLabel("Código del autenticador").fill(totp(secret));
    await page.getByRole("button", { name: "Confirmar cambio" }).click();
    await expect(
      page.getByText("Códigos reemplazados.", { exact: false }),
    ).toBeVisible();
    const replacements = (await page
      .locator("pre.setup-key")
      .textContent())!.split("\n");
    await page
      .getByRole("button", { name: "He guardado los códigos · Iniciar sesión" })
      .click();
    await login(replacements[0]);
    await page.getByLabel("Operación").selectOption("disable");
    await page.getByLabel("Contraseña actual").fill(password);
    await page.getByLabel("O un código de recuperación").fill(replacements[1]!);
    await page.getByRole("button", { name: "Confirmar cambio" }).click();
    await expect(
      page.getByText("Autenticación en dos pasos desactivada.", {
        exact: false,
      }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Volver a iniciar sesión" }).click();
    await login();
    await expect(page.getByText("No activada", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Cerrar sesión" }).click();
    expect(errors).toEqual([]);
  } finally {
    const disabled = await request.put(`/api/usuarios/${id}/acceso`, {
      headers,
      data: { role: "Consulta", active: false },
    });
    expect(disabled.status()).toBe(204);
    await request.post("/api/auth/logout", { headers });
  }
});
