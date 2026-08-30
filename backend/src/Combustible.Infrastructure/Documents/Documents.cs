using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Combustible.Domain;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using QRCoder;

namespace Combustible.Infrastructure.Documents;

public sealed record TicketDocument(string Number, string ShortCode, string EmployeeName, string EmployeeCode,
    string VehiclePlate, string VehicleCode, string Department, string FuelType, decimal Quantity,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string QrPayload, string Url);

// Celdas tipadas: string, decimal, long, int o DateTimeOffset. Cada formato las representa a su manera.
public sealed record TableDocument(string Title, IReadOnlyList<(string Label, string Value)> Summary,
    IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

public static class DocumentRenderer
{
    private static readonly Lock FontLock = new();
    private static bool _fontsReady;

    public static byte[] QrPng(string payload) =>
        PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, 8);

    public static byte[] TicketPdf(TicketDocument ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var document = NewDocument("Ticket " + ticket.Number);
        var section = document.LastSection;
        Heading(section, "INTEC — Ticket digital de combustible", 16);
        Heading(section, ticket.Number, 13);
        var image = section.AddImage("base64:" + Convert.ToBase64String(QrPng(ticket.QrPayload)));
        image.Width = Unit.FromCentimeter(6.5);
        image.LockAspectRatio = true;
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(5));
        table.AddColumn(Unit.FromCentimeter(11));
        foreach (var (label, value) in new[]
        {
            ("Código corto", ticket.ShortCode),
            ("Empleado", $"{ticket.EmployeeName} ({ticket.EmployeeCode})"),
            ("Vehículo", $"{ticket.VehiclePlate} — ficha {ticket.VehicleCode}"),
            ("Departamento", ticket.Department),
            ("Combustible", ticket.FuelType),
            ("Cantidad autorizada", ticket.Quantity.ToString("0.000", CultureInfo.InvariantCulture) + " gal"),
            ("Emitido", BusinessClock.Format(ticket.IssuedAt)),
            ("Vence", BusinessClock.Format(ticket.ExpiresAt)),
        })
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(label).Format.Font.Bold = true;
            row.Cells[1].AddParagraph(value);
        }
        section.AddParagraph().AddLineBreak();
        section.AddParagraph("Ticket de un solo uso. Presente este código QR y su cédula en la estación. " +
            "El despacho no puede exceder la cantidad autorizada.");
        section.AddParagraph("Consulta segura: " + ticket.Url).Format.Font.Size = 8;
        section.AddParagraph("Hora de República Dominicana (America/Santo_Domingo).").Format.Font.Size = 8;
        return Render(document);
    }

    // La primera tabla aporta título y resumen; las siguientes se añaden como secciones (p. ej. el acta
    // de cierre: tanques y despachos confirmados).
    public static byte[] TablePdf(TableDocument table, params TableDocument[] more)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(more);
        var document = NewDocument(table.Title);
        var section = document.LastSection;
        if (new[] { table }.Concat(more).Any(x => x.Headers.Count > 6)) section.PageSetup.Orientation = Orientation.Landscape;
        Heading(section, table.Title, 14);
        foreach (var (label, value) in table.Summary)
        {
            var paragraph = section.AddParagraph();
            paragraph.AddFormattedText(label + ": ", TextFormat.Bold);
            paragraph.AddText(value);
        }
        section.AddParagraph();
        AddGrid(section, table);
        foreach (var extra in more)
        {
            section.AddParagraph();
            Heading(section, extra.Title, 11);
            AddGrid(section, extra);
        }
        return Render(document);
    }

    private static void AddGrid(Section section, TableDocument table)
    {
        if (table.Headers.Count == 0) return;
        var usable = (section.PageSetup.Orientation == Orientation.Landscape ? 27.94 : 21.59) - 3.0;
        var grid = section.AddTable();
        grid.Borders.Width = 0.4;
        grid.Format.Font.Size = 7.5;
        foreach (var _ in table.Headers) grid.AddColumn(Unit.FromCentimeter(usable / table.Headers.Count));
        var header = grid.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Colors.LightGray;
        for (var i = 0; i < table.Headers.Count; i++) header.Cells[i].AddParagraph(table.Headers[i]).Format.Font.Bold = true;
        foreach (var values in table.Rows)
        {
            var row = grid.AddRow();
            for (var i = 0; i < table.Headers.Count; i++) row.Cells[i].AddParagraph(Text(values[i]));
        }
        if (table.Rows.Count == 0) section.AddParagraph("Sin registros para los filtros indicados.");
    }

    public static byte[] Csv(TableDocument table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', table.Headers.Select(Escape)));
        foreach (var row in table.Rows) builder.AppendLine(string.Join(',', row.Select(value => Escape(Text(value)))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static byte[] Xlsx(TableDocument table)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Reporte");
        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        var line = 2;
        foreach (var (label, value) in table.Summary)
        {
            sheet.Cell(line, 1).Value = label;
            sheet.Cell(line, 2).Value = value;
            line++;
        }
        line++;
        var headerRow = line;
        for (var i = 0; i < table.Headers.Count; i++) sheet.Cell(line, i + 1).Value = table.Headers[i];
        sheet.Row(line).Style.Font.Bold = true;
        foreach (var row in table.Rows)
        {
            line++;
            for (var i = 0; i < row.Count; i++)
            {
                var cell = sheet.Cell(line, i + 1);
                switch (row[i])
                {
                    case decimal number: cell.Value = number; cell.Style.NumberFormat.Format = "0.000"; break;
                    case long number: cell.Value = number; break;
                    case int number: cell.Value = number; break;
                    case DateTimeOffset instant:
                        cell.Value = BusinessClock.ToLocal(instant).DateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case DateOnly day: cell.Value = day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    case null: break;
                    default: cell.Value = row[i]!.ToString(); break;
                }
            }
        }
        sheet.SheetView.FreezeRows(headerRow);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("0.000", CultureInfo.InvariantCulture),
        DateTimeOffset instant => BusinessClock.Format(instant),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value)
    {
        // Evita que Excel interprete celdas como fórmulas (inyección CSV).
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal)) value = "'" + value;
        return value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static Document NewDocument(string title)
    {
        EnsureFonts();
        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = "INTEC Combustible";
        document.Styles[StyleNames.Normal]!.Font.Name = SystemFontResolver.Family;
        document.Styles[StyleNames.Normal]!.Font.Size = 10;
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.AddText(title + " — página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();
        return document;
    }

    private static void Heading(Section section, string text, double size)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = true;
        paragraph.Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream);
        return stream.ToArray();
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady) return;
            GlobalFontSettings.FontResolver = SystemFontResolver.Create();
            _fontsReady = true;
        }
    }
}

// PDFsharp necesita archivos TTF. No se incluyen fuentes en el repositorio: se usan las del
// sistema (o PDF_FONT_REGULAR / PDF_FONT_BOLD). Si no hay ninguna, falla con un error claro.
internal sealed class SystemFontResolver(string regular, string bold) : IFontResolver
{
    public const string Family = "Sans";

    private static readonly (string Regular, string Bold)[] Candidates =
    [
        (@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf"),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"),
        ("/usr/share/fonts/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf"),
    ];

    public static SystemFontResolver Create()
    {
        var regular = Environment.GetEnvironmentVariable("PDF_FONT_REGULAR");
        var bold = Environment.GetEnvironmentVariable("PDF_FONT_BOLD");
        if (!string.IsNullOrEmpty(regular))
        {
            if (!File.Exists(regular)) throw new InvalidOperationException($"PDF_FONT_REGULAR no existe: {regular}");
            return new SystemFontResolver(regular, !string.IsNullOrEmpty(bold) && File.Exists(bold) ? bold : regular);
        }
        foreach (var (candidateRegular, candidateBold) in Candidates)
            if (File.Exists(candidateRegular))
                return new SystemFontResolver(candidateRegular, File.Exists(candidateBold) ? candidateBold : candidateRegular);
        throw new InvalidOperationException("No hay fuente TTF para generar PDF. Instala fonts-dejavu-core o define PDF_FONT_REGULAR.");
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "Sans-Bold" : "Sans-Regular");

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(faceName == "Sans-Bold" ? bold : regular);
}
