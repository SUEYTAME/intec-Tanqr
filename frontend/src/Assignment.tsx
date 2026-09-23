import type { Assignment, Catalogs } from "./lib";
import { formatGallons } from "./lib";

// Empleado, vehículo, departamento y combustible. El departamento solo puede ser
// el del empleado o el del vehículo (regla del servidor), así que se limita a esos.
export function AssignmentFields({
  catalogs,
  value,
  onChange,
}: {
  catalogs: Catalogs;
  value: Assignment;
  onChange: (value: Assignment) => void;
}) {
  const employee = catalogs.employees.find((e) => e.id === value.employeeId);
  const vehicle = catalogs.vehicles.find((v) => v.id === value.vehicleId);
  const allowed = new Set(
    [employee?.departmentId, vehicle?.departmentId].filter(Boolean),
  );
  const departments = catalogs.departments.filter(
    (d) => allowed.has(d.id) && (d.active || d.id === value.departmentId),
  );
  const pick = (next: Partial<Assignment>) => {
    const merged = { ...value, ...next };
    const nextEmployee = catalogs.employees.find(
      (e) => e.id === merged.employeeId,
    );
    const nextVehicle = catalogs.vehicles.find((v) => v.id === merged.vehicleId);
    const valid = [nextEmployee?.departmentId, nextVehicle?.departmentId];
    if (!valid.includes(merged.departmentId))
      merged.departmentId =
        nextEmployee?.departmentId ?? nextVehicle?.departmentId ?? "";
    onChange(merged);
  };
  return (
    <>
      <label>
        Empleado
        <select
          name="employeeId"
          required
          value={value.employeeId}
          onChange={(e) => pick({ employeeId: e.target.value })}
        >
          <option value="">Selecciona un empleado</option>
          {catalogs.employees
            .filter((e) => e.active || e.id === value.employeeId)
            .map((e) => (
              <option key={e.id} value={e.id}>
                {e.fullName} ({e.code})
              </option>
            ))}
        </select>
      </label>
      <label>
        Vehículo
        <select
          name="vehicleId"
          required
          value={value.vehicleId}
          onChange={(e) => pick({ vehicleId: e.target.value })}
        >
          <option value="">Selecciona un vehículo</option>
          {catalogs.vehicles
            .filter((v) => v.active || v.id === value.vehicleId)
            .map((v) => (
              <option key={v.id} value={v.id}>
                {v.plate} · ficha {v.internalCode} · tanque{" "}
                {formatGallons(v.tankCapacity)}
              </option>
            ))}
        </select>
      </label>
      <label>
        Departamento
        <select
          name="departmentId"
          required
          value={value.departmentId}
          onChange={(e) => pick({ departmentId: e.target.value })}
        >
          <option value="">
            {allowed.size === 0
              ? "Elige empleado o vehículo primero"
              : "Selecciona un departamento"}
          </option>
          {departments.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Combustible
        <select
          name="fuelTypeId"
          required
          value={value.fuelTypeId}
          onChange={(e) => pick({ fuelTypeId: e.target.value })}
        >
          <option value="">Selecciona un combustible</option>
          {catalogs.fuels
            .filter((f) => f.active || f.id === value.fuelTypeId)
            .map((f) => (
              <option key={f.id} value={f.id}>
                {f.name}
              </option>
            ))}
        </select>
      </label>
    </>
  );
}
