namespace Combustible.Api.Endpoints;

// Los atributos de validación traen frases en inglés (o «El campo Code…») con el nombre interno.
// La interfaz muestra el texto tal cual, así que aquí se cambia por una frase en español.
public static class ValidationText
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "el código",
        ["Name"] = "el nombre",
        ["FullName"] = "el nombre completo",
        ["NationalId"] = "la cédula",
        ["Position"] = "el cargo",
        ["Email"] = "el correo",
        ["Mobile"] = "el teléfono",
        ["Plate"] = "la placa",
        ["InternalCode"] = "la ficha",
        ["Make"] = "la marca",
        ["Model"] = "el modelo",
        ["Year"] = "el año",
        ["Kind"] = "el tipo",
        ["TankCapacity"] = "la capacidad del tanque",
        ["Odometer"] = "el odómetro",
        ["Password"] = "la contraseña",
        ["DisplayName"] = "el nombre para mostrar",
        ["Role"] = "el rol",
        ["Prefix"] = "el prefijo",
        ["ValidityDays"] = "la vigencia",
        ["WarningHours"] = "las horas de aviso",
        ["MaxActiveTicketsPerVehicle"] = "el máximo de tickets activos",
        ["AuthorizedQuantity"] = "la cantidad autorizada",
        ["Quantity"] = "la cantidad",
        ["Reason"] = "el motivo",
        ["Notes"] = "las observaciones",
        ["Qr"] = "el código QR",
        ["SupplierRnc"] = "el RNC del suplidor",
        ["SupplierName"] = "el nombre del suplidor",
        ["Invoice"] = "la factura",
        ["Observations"] = "las observaciones",
        ["DifferenceReason"] = "el motivo de la diferencia",
        ["Capacity"] = "la capacidad",
        ["CriticalLevel"] = "el nivel crítico",
        ["HistorySize"] = "la cantidad de despachos a promediar",
        ["Frequency"] = "la frecuencia",
        ["Rule"] = "la regla de cantidad",
        ["Version"] = "la versión del registro",
        ["RecoveryCode"] = "el código de recuperación",
        ["request"] = "este dato",
    };

    public static string ForPerson(string? member, string? message)
    {
        var label = Labels.GetValueOrDefault(member ?? "") ?? "este dato";
        var text = message ?? string.Empty;
        if (Has(text, "e-mail", "correo electrónico")) return "El correo no tiene un formato válido.";
        if (Has(text, "phone number", "número de teléfono")) return "El teléfono no tiene un formato válido.";
        if (Has(text, "regular expression", "expresión regular")) return $"{Capitalize(label)} no tiene el formato esperado.";
        if (Has(text, "maximum length", "minimum length", "longitud")) return $"{Capitalize(label)} no tiene una longitud válida.";
        if (Has(text, "must be between", "debe estar entre")) return $"{Capitalize(label)} está fuera del rango permitido.";
        if (Has(text, "required", "obligatorio")) return $"Completa {label}.";
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("The ", StringComparison.Ordinal) || text.StartsWith("El campo ", StringComparison.Ordinal))
            return $"Revisa {label}.";
        return text;
    }

    private static bool Has(string text, params string[] parts) =>
        parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
