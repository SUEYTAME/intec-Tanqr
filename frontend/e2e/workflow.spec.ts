import { randomBytes, randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { expect, test, type BrowserContext, type Page, type APIRequestContext } from "@playwright/test";

const apiBase = "http://127.0.0.1:5080";
const password = `Qa-${randomBytes(24).toString("base64url")}aA1!`;
const suffix = randomUUID().slice(0, 8).toUpperCase();
const nationalId = Array.from(randomBytes(11), (byte) => String(byte % 10)).join("");
const today = new Intl.DateTimeFormat("en-CA", {
  timeZone: "America/Santo_Domingo",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
}).format(new Date());

type Created = { id: string; [key: string]: unknown };
type Session = { accessToken: string };
const workflowContexts: BrowserContext[] = [];

test.afterEach(async () => {
  await Promise.all(workflowContexts.splice(0).map((context) => context.close()));
});

async function call<T>(
  request: APIRequestContext,
  path: string,
  token: string,
  body?: object,
): Promise<T> {
  const response = await request.fetch(`${apiBase}${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: { Authorization: `Bearer ${token}` },
    ...(body === undefined ? {} : { data: body }),
  });
  const text = await response.text();
  if (!response.ok())
    throw new Error(`${path} returned ${response.status()}: ${text}`);
  return (text ? JSON.parse(text) : undefined) as T;
}

async function postResult<T>(
  request: APIRequestContext,
  path: string,
  token: string,
  body: object,
) {
  const response = await request.post(`${apiBase}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
    data: body,
  });
  return { response, body: (await response.json().catch(() => ({}))) as T };
}

async function apiLogin(
  request: APIRequestContext,
  email: string,
  secret: string,
) {
  const response = await request.post(`${apiBase}/api/auth/login`, {
    data: { email, password: secret },
  });
  expect(response.ok(), `login for ${email}`).toBeTruthy();
  return (await response.json()) as Session;
}

async function loginUi(page: Page, email: string) {
  await page.goto("/");
  await page.getByLabel("Correo electrónico").fill(email);
  await page.getByLabel("Contraseña", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page.locator("main h1")).toBeVisible();
}

async function requestTicket(page: Page, quantity: number) {
  await page.getByRole("button", { name: "Solicitudes", exact: true }).click();
  await page.getByRole("button", { name: "+ Nueva solicitud" }).click();
  await page.locator('select[name="employeeId"]').selectOption({ label: `QA Empleado ${suffix} (QA${suffix})` });
  await page.locator('select[name="vehicleId"]').selectOption({ label: `QA-${suffix} · ficha ${suffix} · tanque 60 gal` });
  await page.locator('select[name="departmentId"]').selectOption({ label: `QA Departamento ${suffix}` });
  await page.locator('select[name="fuelTypeId"]').selectOption({ label: `QA Gasolina ${suffix}` });
  await page.getByLabel("Cantidad autorizada (galones)").fill(String(quantity));
  await page.getByRole("button", { name: "Registrar solicitud" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Solicitud registrada" })).toBeVisible();
}

async function approveLatest(page: Page) {
  const row = page.getByRole("row").filter({ hasText: `QA Empleado ${suffix}` }).first();
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Aprobar" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Aprobar y emitir ticket" }).click();
  await expect(page.getByText("TICKET EMITIDO")).toBeVisible();
  const title = await page.locator(".result-card h2").textContent();
  const ticketNumber = title?.replace(/^Ticket\s+/, "").trim();
  expect(ticketNumber).toBeTruthy();
  return ticketNumber!;
}

async function readTicketQr(page: Page, ticketNumber: string) {
  await page.getByRole("button", { name: "Cerrar aviso" }).click();
  await page.getByRole("button", { name: "Tickets", exact: true }).click();
  await page.getByLabel("Buscar por número, código corto o placa").fill(ticketNumber);
  await page.getByRole("button", { name: "Buscar", exact: true }).click();
  await page.getByRole("button", { name: `Ver ticket ${ticketNumber}` }).click();
  await page.getByRole("button", { name: "Ver QR", exact: true }).click();
  const image = page.getByRole("img", { name: `Código QR del ticket ${ticketNumber}` });
  await expect(image).toBeVisible();
  await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
  const qr = await image.evaluate(async (img: HTMLImageElement) => {
    const { decodeImage } = await import("/src/scanner.ts");
    const blob = await (await fetch(img.src)).blob();
    return decodeImage(blob);
  });
  expect(qr).toMatch(/^IC1\.[0-9a-f]{32}\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]+$/);

  const [qrDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Descargar QR", exact: true }).click(),
  ]);
  expect((await readFile(await qrDownload.path())).subarray(0, 8)).toEqual(
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
  );
  const [ticketPdf] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Descargar PDF", exact: true }).click(),
  ]);
  expect((await readFile(await ticketPdf.path())).subarray(0, 5).toString()).toBe("%PDF-");
  await expect(page.getByText("El correo quedó guardado en la bandeja de pruebas. No llegó a un buzón real.")).toBeVisible();
  await expect(page.getByText("El SMS quedó guardado en la bandeja de pruebas. No se envió a un teléfono.")).toBeVisible();
  await expect(page.getByText(/SMTP |Twilio HTTP|Queued mail/)).toHaveCount(0);
  return qr!;
}

test("flujo integral aislado: solicitud, ticket QR, despacho, inventario, cierre y reportes", async ({ browser, page, request }) => {
  test.setTimeout(60_000);
  const adminEmail = process.env.BOOTSTRAP_EMAIL;
  const adminPassword = process.env.BOOTSTRAP_PASSWORD;
  expect(adminEmail).toBeTruthy();
  expect(adminPassword).toBeTruthy();
  const admin = await apiLogin(request, adminEmail!, adminPassword!);

  const department = await call<Created>(request, "/api/departamentos/", admin.accessToken, {
    code: `QA${suffix}`, name: `QA Departamento ${suffix}`, active: true,
  });
  const fuel = await call<Created>(request, "/api/combustibles/", admin.accessToken, {
    code: `Q${suffix}`, name: `QA Gasolina ${suffix}`, active: true,
  });
  const station = await call<Created>(request, "/api/estaciones/", admin.accessToken, {
    code: `Q${suffix}`, name: `QA Estación ${suffix}`, active: true,
  });
  const tank = await call<Created>(request, "/api/tanques/", admin.accessToken, {
    code: `Q${suffix}`, stationId: station.id, fuelTypeId: fuel.id,
    capacity: 100, criticalLevel: 5, active: true,
  });
  await call<Created>(request, "/api/empleados/", admin.accessToken, {
    code: `QA${suffix}`, fullName: `QA Empleado ${suffix}`, nationalId,
    departmentId: department.id, position: "Prueba de calidad", email: `employee-${suffix.toLowerCase()}@localhost.test`,
    mobile: "+18095550123", active: true,
  });
  await call<Created>(request, "/api/vehiculos/", admin.accessToken, {
    plate: `QA-${suffix}`, internalCode: suffix, make: "QA", model: "Demo", year: 2026,
    kind: "Sedán", departmentId: department.id, tankCapacity: 60, odometer: 100, active: true,
  });

  const accounts = [
    { email: `supervisor-${suffix.toLowerCase()}@localhost.test`, displayName: `QA Supervisor ${suffix}`, role: "Supervisor" },
    { email: `requester-${suffix.toLowerCase()}@localhost.test`, displayName: `QA Consulta ${suffix}`, role: "Consulta" },
    { email: `dispatcher-${suffix.toLowerCase()}@localhost.test`, displayName: `QA Despachador ${suffix}`, role: "Despachador" },
  ];
  for (const account of accounts)
    await call(request, "/api/usuarios/", admin.accessToken, { ...account, password });
  const [supervisor, dispatcher] = await Promise.all([
    apiLogin(request, accounts[0].email, password),
    apiLogin(request, accounts[2].email, password),
  ]);

  // CP-007 / CP-055: registrar una entrada real de inventario por la interfaz.
  await loginUi(page, accounts[0].email);
  await page.getByRole("button", { name: "Inventario", exact: true }).click();
  await page.getByRole("group", { name: "Secciones de inventario" }).getByRole("button", { name: "Recepciones" }).click();
  await page.getByLabel("Tipo").selectOption("Receipt");
  await page.getByLabel("RNC del suplidor").fill("123456789");
  await page.getByLabel("Nombre del suplidor").fill(`QA Proveedor ${suffix}`);
  await page.getByLabel("Factura").fill(`QA-${suffix}`);
  await page.getByLabel("Cantidad (galones)").fill("12");
  await page.getByLabel("Fecha de recepción").fill(today);
  await page.getByLabel("Tanque").selectOption(tank.id);
  await page.getByRole("button", { name: "Registrar recepción" }).click();
  await expect(page.getByText(`QA Proveedor ${suffix}`)).toBeVisible();

  // La cuenta Consulta crea la solicitud; el Supervisor aprueba y emite el ticket.
  await loginUi(page, accounts[1].email);
  await requestTicket(page, 10);
  await loginUi(page, accounts[0].email);
  await page.getByRole("button", { name: "Solicitudes", exact: true }).click();
  const firstNumber = await approveLatest(page);
  const firstQr = await readTicketQr(page, firstNumber);
  const detailStatus = page.locator(".facts").getByText(/Creado|Enviado|Pendiente de entrega|Próximo a vencer/);
  await expect(detailStatus).toBeVisible();
  const managerListContext = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
  workflowContexts.push(managerListContext);
  const managerListPage = await managerListContext.newPage();
  await loginUi(managerListPage, accounts[0].email);
  await managerListPage.getByRole("button", { name: "Tickets", exact: true }).click();
  await managerListPage.getByLabel("Buscar por número, código corto o placa").fill(firstNumber);
  await managerListPage.getByRole("button", { name: "Buscar", exact: true }).click();
  const managerRow = managerListPage.getByRole("row").filter({ hasText: firstNumber });
  const activeStatus = managerRow.locator("td").nth(1);
  await expect(activeStatus).toContainText(/Creado|Enviado|Pendiente de entrega|Próximo a vencer/);

  // La API también rechaza una cantidad superior a la autorizada sin tocar stock.
  const over = await postResult<{ error?: string }>(request, "/api/despachos/", dispatcher.accessToken, {
    qr: firstQr, tankId: tank.id, quantity: 11, identityConfirmed: true,
    odometer: null, differenceReason: null, observations: null,
  });
  expect(over.response.status()).toBe(422);
  expect(over.body.error).toContain("autorizado");
  let inventory = await call<{ tanks: { id: string; balance: number }[] }>(request, "/api/inventario/", dispatcher.accessToken);
  expect(inventory.tanks.find((item) => item.id === tank.id)?.balance).toBe(12);

  // Despachador independiente valida el QR visible y confirma identidad.
  const dispatchContext = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
  workflowContexts.push(dispatchContext);
  const dispatchPage = await dispatchContext.newPage();
  await loginUi(dispatchPage, accounts[2].email);
  await dispatchPage.getByRole("button", { name: "Validar código" }).waitFor();
  await dispatchPage.getByLabel(/Código QR \(lector externo o texto\)/).fill(firstQr);
  await dispatchPage.getByRole("button", { name: "Validar código" }).click();
  await expect(dispatchPage.getByText("TICKET VÁLIDO")).toBeVisible();
  // Validar/leer el QR no lo consume; el consumo sucede al confirmar el despacho.
  await expect(activeStatus).toContainText(/Creado|Enviado|Pendiente de entrega|Próximo a vencer/);
  await dispatchPage.getByLabel("Verifiqué la cédula del portador").check();
  await dispatchPage.getByLabel("Tanque").selectOption(tank.id);
  await dispatchPage.getByLabel("Galones despachados").fill("10");
  await dispatchPage.getByRole("button", { name: "Confirmar despacho" }).click();
  await expect(dispatchPage.getByText("DESPACHO REGISTRADO")).toBeVisible();
  // Otra sesión con la lista abierta conserva su instantánea hasta que se actualiza.
  await expect(activeStatus).toContainText(/Creado|Enviado|Pendiente de entrega|Próximo a vencer/);
  const currentTicket = await call<{ items: { status: string }[] }>(
    request, `/api/tickets/?q=${encodeURIComponent(firstNumber)}`, supervisor.accessToken,
  );
  expect(currentTicket.items[0]?.status).toBe("Consumed");
  await page.bringToFront();
  await expect(page.locator(".facts dd").getByText("Consumido", { exact: true })).toBeVisible({ timeout: 20000 });
  await managerListPage.bringToFront();
  await expect(activeStatus).toHaveText("Consumido", { timeout: 20000 });
  inventory = await call(request, "/api/inventario/", dispatcher.accessToken);
  expect(inventory.tanks.find((item) => item.id === tank.id)?.balance).toBe(2);
  let movements = await call<{ items: { kind: string; quantity: number }[] }>(
    request, `/api/inventario/movimientos?tankId=${tank.id}`, dispatcher.accessToken,
  );
  expect(movements.items.filter((item) => item.kind === "Dispatch")).toHaveLength(1);
  expect(movements.items.find((item) => item.kind === "Dispatch")?.quantity).toBe(-10);

  // Reutilizar el QR consumido se rechaza y no crea un segundo movimiento.
  await dispatchPage.getByRole("button", { name: "Despachar otro ticket" }).click();
  await dispatchPage.getByLabel(/Código QR \(lector externo o texto\)/).fill(firstQr);
  await dispatchPage.getByRole("button", { name: "Validar código" }).click();
  await expect(dispatchPage.getByRole("alert").filter({ hasText: /consumido|despachado/i })).toBeVisible();
  movements = await call(request, `/api/inventario/movimientos?tankId=${tank.id}`, dispatcher.accessToken);
  expect(movements.items.filter((item) => item.kind === "Dispatch")).toHaveLength(1);

  // Issue a second valid ticket, then prove a stock shortfall cannot mutate the ledger.
  await loginUi(page, accounts[1].email);
  await requestTicket(page, 5);
  await loginUi(page, accounts[0].email);
  await page.getByRole("button", { name: "Solicitudes", exact: true }).click();
  const secondNumber = await approveLatest(page);
  const secondQr = await readTicketQr(page, secondNumber);
  const shortDispatch = await postResult<{ error?: string }>(request, "/api/despachos/", dispatcher.accessToken, {
    qr: secondQr, tankId: tank.id, quantity: 5, identityConfirmed: true,
    odometer: null, differenceReason: null, observations: null,
  });
  expect(shortDispatch.response.status()).toBe(422);
  expect(shortDispatch.body.error).toMatch(/existencia insuficiente/i);
  inventory = await call(request, "/api/inventario/", dispatcher.accessToken);
  expect(inventory.tanks.find((item) => item.id === tank.id)?.balance).toBe(2);
  movements = await call(request, `/api/inventario/movimientos?tankId=${tank.id}`, dispatcher.accessToken);
  expect(movements.items.filter((item) => item.kind === "Dispatch")).toHaveLength(1);
  // CP-035/036: cierre con conteo físico, PDF de acta y duplicado rechazado.
  await dispatchPage.getByRole("button", { name: "Cierre diario", exact: true }).click();
  await dispatchPage.getByLabel("Estación").selectOption(station.id);
  await dispatchPage.getByLabel("Día operativo").fill(today);
  await dispatchPage.getByRole("button", { name: "Ver previo" }).click();
  await expect(dispatchPage.getByText(/1 despachos confirmados/)).toBeVisible();
  await dispatchPage.getByLabel(`Medido en el tanque Q${suffix}`).fill("2");
  await dispatchPage.getByRole("button", { name: "Cerrar el día" }).click();
  await dispatchPage.getByRole("dialog").getByRole("button", { name: "Cerrar el día" }).click();
  await expect(dispatchPage.getByRole("heading", { name: "Cierre registrado" })).toBeVisible();
  const [closePdf] = await Promise.all([
    dispatchPage.waitForEvent("download"),
    dispatchPage.getByRole("button", { name: "Descargar acta PDF" }).click(),
  ]);
  expect((await readFile(await closePdf.path())).subarray(0, 5).toString()).toBe("%PDF-");
  const duplicate = await postResult<{ error?: string }>(request, "/api/cierres/", dispatcher.accessToken, {
    stationId: station.id, day: today, counts: [{ tankId: tank.id, counted: 2 }], notes: null,
  });
  expect(duplicate.response.status()).toBe(409);
  const afterCloseReceipt = await postResult<{ error?: string }>(request, "/api/inventario/recepciones", supervisor.accessToken, {
    kind: "Receipt", supplierRnc: "123456789", supplierName: `QA Proveedor ${suffix}`,
    invoice: `AFTER-${suffix}`, quantity: 1, receivedOn: today, tankId: tank.id,
  });
  expect(afterCloseReceipt.response.status()).toBe(422);
  inventory = await call(request, "/api/inventario/", dispatcher.accessToken);
  expect(inventory.tanks.find((item) => item.id === tank.id)?.balance).toBe(2);
  movements = await call(request, `/api/inventario/movimientos?tankId=${tank.id}`, dispatcher.accessToken);
  expect(movements.items.filter((item) => item.kind === "Dispatch")).toHaveLength(1);

  // CP-034/055: filtrar despachos consumidos y descargar los tres formatos.
  await loginUi(page, accounts[0].email);
  await page.getByRole("button", { name: "Reportes", exact: true }).click();
  await page.getByRole("group", { name: "Tipo de reporte" }).getByRole("button", { name: "Despachos" }).click();
  await page.getByLabel("Estado del ticket").selectOption("Consumed");
  await page.getByRole("button", { name: "Ver reporte" }).click();
  await expect(page.getByRole("cell", { name: firstNumber })).toBeVisible();
  await expect(page.getByRole("cell", { name: secondNumber })).toHaveCount(0);
  const expectedDownloads = [
    { label: "Exportar CSV", signature: "csv" },
    { label: "Exportar XLSX", signature: "xlsx" },
    { label: "Exportar PDF", signature: "pdf" },
  ];
  for (const item of expectedDownloads) {
    const [download] = await Promise.all([
      page.waitForEvent("download"),
      page.getByRole("button", { name: item.label }).click(),
    ]);
    const bytes = await readFile(await download.path());
    if (item.signature === "csv") expect(bytes.toString("utf8")).toContain(firstNumber);
    if (item.signature === "xlsx") expect(bytes.subarray(0, 4).toString("hex")).toBe("504b0304");
    if (item.signature === "pdf") expect(bytes.subarray(0, 5).toString()).toBe("%PDF-");
  }
});
