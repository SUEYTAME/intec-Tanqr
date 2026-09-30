using System.Globalization;
using System.Text;
using Combustible.Infrastructure.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace Combustible.UnitTests;

public sealed class DocumentTests
{
    private static readonly (string Label, string Value)[] Summary =
    [
        ("Periodo", "2026-09-01 a 2026-09-30"),
        ("Registros", "90"),
    ];

    [Theory]
    [InlineData(12)]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(7)]
    public void Wide_report_tables_fit_and_repeat_headers_across_pages(int columnCount)
    {
        var report = CreateReport("Reporte QA", columnCount, 90, "WIDEHEADER");
        using var pdf = Open(DocumentRenderer.TablePdf(report));

        AssertTableFitsOnEveryPage(pdf);
        AssertHeadersAndLastRowSurvivePagination(pdf, "WIDEHEADER", "ENDROW089");
    }

    [Fact]
    public void Close_report_detail_table_fits_and_repeats_header_across_pages()
    {
        var summary = new TableDocument("Acta de cierre", Summary,
            ["Estación", "Día"], [["QA Estación", "2026-09-30"]]);
        var details = CreateReport("Despachos confirmados", 7, 90, "CLOSEHEADER");
        using var pdf = Open(DocumentRenderer.TablePdf(summary, details));

        AssertTableFitsOnEveryPage(pdf);
        AssertHeadersAndLastRowSurvivePagination(pdf, "CLOSEHEADER", "ENDROW089");
    }

    [Fact]
    public void Three_column_consumption_report_fits_a_portrait_page()
    {
        var report = CreateReport("Consumo", 3, 4, "CONSUMPTIONHEADER");
        using var pdf = Open(DocumentRenderer.TablePdf(report));

        Assert.Single(pdf.Pages);
        Assert.True(pdf.Pages[0].Height.Point > pdf.Pages[0].Width.Point,
            "A narrow report should remain in portrait orientation.");
        AssertPageGeometryFits(pdf.Pages[0]);
    }

    [Fact]
    public void Wide_report_wraps_long_unbroken_header_and_body_inside_cells_without_changing_csv()
    {
        const string longHeader = "UNBROKENHEADERSEQUENCEFORPDF";
        const string longValue = "UNBROKENBODYSEQUENCEWITHMANYCHARACTERS";
        var headers = new[]
        {
            "Fecha y hora", "Ticket", "Estación", "Tanque", "Empleado", "Vehículo",
            longHeader, "Combustible", "Autorizado (gal)", "Servido (gal)", "Diferencia (gal)", "Operador",
        };
        var row = new object?[]
        {
            "2026-09-30 09:52", "COM-2026-000001", "DEMO - Estación Campus", "DEMO-C-GO",
            "DEMO - Empleado QA", "DEMO004", longValue, "DEMO - Gasolina Regular", 15m, 13.5m, 1.5m,
            "DEMO - Despachador Campus",
        };
        var report = new TableDocument("Reporte de despachos", Summary, headers,
            new IReadOnlyList<object?>[] { row });
        var csv = DocumentRenderer.Csv(report);

        using var pdf = Open(DocumentRenderer.TablePdf(report));

        Assert.Single(pdf.Pages);
        Assert.Equal(csv, DocumentRenderer.Csv(report));
        var csvText = Encoding.UTF8.GetString(csv);
        Assert.Contains(longHeader, csvText, StringComparison.Ordinal);
        Assert.Contains(longValue, csvText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u200B", csvText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00AD", csvText, StringComparison.Ordinal);

        using var measure = XGraphics.CreateMeasureContext(new XSize(1000, 1000),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var regular = new XFont("Sans", 7.5);
        var bold = new XFont("Sans", 7.5, XFontStyleEx.Bold);
        var cellBlocks = ReadTableTextBlocks(pdf.Pages[0]);
        AssertWrappedCellFits(cellBlocks, longHeader, measure, bold);
        AssertWrappedCellFits(cellBlocks, longValue, measure, regular);
    }

    [Fact]
    public void Ticket_pdf_contains_the_qr_image_instead_of_an_error_placeholder()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var ticket = new TicketDocument("COM-QA-000001", "SHORT01", "Empleado QA", "EMP-QA",
            "QA001", "VEH-QA", "Departamento QA", "Diésel", 10m, now, now.AddDays(1),
            "signed-ticket-payload-for-pdf-test", "https://example.test/ticket/COM-QA-000001");
        using var pdf = Open(DocumentRenderer.TicketPdf(ticket));

        Assert.Single(pdf.Pages);
        var page = pdf.Pages[0];
        var xObjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        var imageObjects = xObjects is null
            ? []
            : xObjects.Elements
                .Select(entry => xObjects.Elements.GetDictionary(entry.Key))
                .Where(value => value?.Elements.GetName("/Subtype").TrimStart('/') == "Image")
                .ToArray();
        Assert.NotEmpty(imageObjects);

        var visibleText = ReadText(page);
        Assert.DoesNotContain("Image has no valid type.", visibleText, StringComparison.Ordinal);
    }

    private static TableDocument CreateReport(string title, int columnCount, int rowCount, string markerHeader)
    {
        var headers = Enumerable.Range(0, columnCount)
            .Select(index => index == 0 ? markerHeader : $"COL{index + 1:00}")
            .ToArray();
        var rows = Enumerable.Range(0, rowCount)
            .Select(row => (IReadOnlyList<object?>)Enumerable.Range(0, columnCount)
                .Select(column => row == rowCount - 1 && column == 0
                    ? "ENDROW089"
                    : $"R{row:000}C{column:00}")
                .Cast<object?>()
                .ToArray())
            .ToArray();
        return new TableDocument(title, Summary, headers, rows);
    }

    private static PdfDocument Open(byte[] bytes) =>
        PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    private static void AssertTableFitsOnEveryPage(PdfDocument pdf)
    {
        Assert.True(pdf.Pages.Count > 1, "The generated report should exercise table pagination.");
        foreach (PdfPage page in pdf.Pages)
            AssertPageGeometryFits(page);
    }

    private static void AssertPageGeometryFits(PdfPage page)
    {
        var rightMostTableEdge = RightMostPathX(page);
        Assert.True(rightMostTableEdge > 0, "The PDF page should contain rendered table geometry.");
        Assert.True(rightMostTableEdge <= page.Width.Point + 0.5,
            $"Table geometry extends to x={rightMostTableEdge:F2} pt on a {page.Width.Point:F2} pt page.");
    }

    private static void AssertHeadersAndLastRowSurvivePagination(PdfDocument pdf, string header, string lastRow)
    {
        for (var index = 0; index < pdf.Pages.Count; index++)
        {
            var pageText = ReadText(pdf.Pages[index]);
            Assert.Contains(header, pageText, StringComparison.Ordinal);
            if (index == 0) Assert.Contains("R000C01", pageText, StringComparison.Ordinal);
            if (index == pdf.Pages.Count - 1) Assert.Contains(lastRow, pageText, StringComparison.Ordinal);
        }
    }

    private static string ReadText(PdfPage page) => string.Concat(
        ContentReader.ReadContent(page)
            .OfType<COperator>()
            .Where(operation => operation.Name == "Tj")
            .SelectMany(operation => operation.Operands.OfType<CString>())
            .Select(value => value.Value));

    private static double RightMostPathX(PdfPage page)
    {
        var maximum = double.NegativeInfinity;
        foreach (var operation in ContentReader.ReadContent(page).OfType<COperator>())
        {
            var numbers = operation.Operands
                .Select(value => double.TryParse(value.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number) ? number : (double?)null)
                .ToArray();
            if (operation.Name == "re" && numbers.Length >= 4 && numbers[0] is { } x && numbers[2] is { } width)
                maximum = Math.Max(maximum, x + width);
            else if (operation.Name is "m" or "l" && numbers.Length >= 2 && numbers[0] is { } pointX)
                maximum = Math.Max(maximum, pointX);
        }
        return maximum;
    }

    private static void AssertWrappedCellFits(IEnumerable<CellTextBlock> blocks, string expected,
        XGraphics measure, XFont font)
    {
        var block = Assert.Single(blocks, item => NormalizePdfText(item.Runs.Select(run => run.Text))
            .Contains(expected, StringComparison.Ordinal));
        Assert.True(block.Runs.Count > 1,
            $"Expected '{expected}' to be split into multiple rendered text runs.");
        foreach (var run in block.Runs)
        {
            var width = measure.MeasureString(run.Text, font).Width;
            Assert.True(run.X >= block.Left + 2.5,
                $"Text run '{run.Text}' starts outside its table cell at x={run.X:F2} pt (left={block.Left:F2} pt).");
            Assert.True(run.X + width <= block.Right - 2.5 + 0.75,
                $"Text run '{run.Text}' ends outside its table cell at x={run.X + width:F2} pt (right={block.Right:F2} pt).");
        }
    }

    private static string NormalizePdfText(IEnumerable<string> runs) => string.Concat(runs)
        .Replace("\u200B", string.Empty, StringComparison.Ordinal)
        .Replace("\u00AD", string.Empty, StringComparison.Ordinal);

    private static List<CellTextBlock> ReadTableTextBlocks(PdfPage page)
    {
        var rectangles = new List<(double Left, double Right)>();
        var rawBlocks = new List<List<TextRun>>();
        List<TextRun>? runs = null;
        double lineX = 0;
        double startingX = 0;
        foreach (var operation in ContentReader.ReadContent(page).OfType<COperator>())
        {
            var numbers = operation.Operands
                .Select(value => double.TryParse(value.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number) ? number : (double?)null)
                .ToArray();
            if (operation.Name == "re" && numbers.Length >= 4 && numbers[0] is { } x && numbers[2] is { } width)
                rectangles.Add((x, x + width));
            else if (operation.Name == "BT")
            {
                runs = [];
                lineX = 0;
                startingX = 0;
            }
            else if (operation.Name == "ET")
            {
                if (runs is { Count: > 0 }) rawBlocks.Add(runs);
                runs = null;
            }
            else if (runs is not null && operation.Name == "Td" && numbers.Length >= 2 && numbers[0] is { } dx && numbers[1] is { } dy)
            {
                if (dy != 0)
                {
                    if (dx != 0) startingX = lineX = dx;
                    else lineX = startingX;
                }
                else lineX += dx;
            }
            else if (runs is not null && operation.Name == "Tj")
            {
                var text = string.Concat(operation.Operands.OfType<CString>().Select(value => value.Value));
                if (text.Length > 0) runs.Add(new TextRun(text, lineX));
            }
        }

        return rawBlocks.Select(block =>
        {
            var firstX = block[0].X;
            var bounds = rectangles
                .Where(rectangle => firstX >= rectangle.Left && firstX < rectangle.Right)
                .OrderBy(rectangle => rectangle.Right - rectangle.Left)
                .FirstOrDefault();
            return new CellTextBlock(bounds.Left, bounds.Right, block);
        }).ToList();
    }

    private sealed record CellTextBlock(double Left, double Right, IReadOnlyList<TextRun> Runs);
    private sealed record TextRun(string Text, double X);
}
