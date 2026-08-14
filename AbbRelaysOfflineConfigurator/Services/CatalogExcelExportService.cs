using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AbbRelaysOfflineConfigurator.Services;

public static class CatalogExcelExportService
{
    public static void Export(
        string path,
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<double>? columnWidths = null)
    {
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("No columns are defined for export.");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        AddEntry(archive, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        AddEntry(archive, "xl/workbook.xml", BuildWorkbook(sheetName));
        AddEntry(archive, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
        AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(headers, rows, columnWidths));
    }

    private static string BuildWorkbook(string sheetName) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="{{EscapeAttribute(SafeSheetName(sheetName))}}" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private static string BuildWorksheet(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<double>? columnWidths)
    {
        var allRows = new List<IReadOnlyList<string>> { headers };
        allRows.AddRange(rows);

        var sheetData = new StringBuilder();
        for (var rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            sheetData.Append($"<row r=\"{rowNumber}\">");
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var value = columnIndex < allRows[rowIndex].Count ? allRows[rowIndex][columnIndex] : "";
                var cellRef = $"{ColumnName(columnIndex + 1)}{rowNumber}";
                sheetData.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeText(value)}</t></is></c>");
            }

            sheetData.Append("</row>");
        }

        return $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          {{BuildColumns(headers.Count, columnWidths)}}
          <sheetData>{{sheetData}}</sheetData>
        </worksheet>
        """;
    }

    private static string BuildColumns(int columnCount, IReadOnlyList<double>? columnWidths)
    {
        var builder = new StringBuilder("<cols>");
        for (var index = 0; index < columnCount; index++)
        {
            var width = columnWidths is not null && index < columnWidths.Count
                ? columnWidths[index]
                : 18d;
            builder.Append(FormattableString.Invariant(
                $"<col min=\"{index + 1}\" max=\"{index + 1}\" width=\"{width:0.##}\" customWidth=\"1\"/>"));
        }

        builder.Append("</cols>");
        return builder.ToString();
    }

    private static string ColumnName(int number)
    {
        var name = "";
        while (number > 0)
        {
            number--;
            name = (char)('A' + number % 26) + name;
            number /= 26;
        }

        return name;
    }

    private static string SafeSheetName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Sheet1" : value.Trim();
        foreach (var invalidChar in new[] { '\\', '/', '?', '*', '[', ']', ':' })
        {
            name = name.Replace(invalidChar, '-');
        }

        return name.Length <= 31 ? name : name[..31];
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string EscapeText(string value)
    {
        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
        xmlWriter.WriteString(value);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }

    private static string EscapeAttribute(string value)
    {
        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
        xmlWriter.WriteString(value);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }
}
