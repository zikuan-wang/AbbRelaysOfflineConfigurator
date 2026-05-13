using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Services;

public static class LegacyConversionExportService
{
    public static void ExportExcel(IEnumerable<LegacyConversionRowViewModel> rows, string path)
    {
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
        AddEntry(archive, "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="615-620转换清单" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        AddEntry(archive, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
        AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(rows));
    }

    private static string BuildWorksheet(IEnumerable<LegacyConversionRowViewModel> rows)
    {
        var sheetRows = new List<string[]>
        {
            new[]
            {
                "615/620\u8ba2\u8d27\u53f7",
                "\u88c5\u7f6e\u7c7b\u578b",
                "\u8f6c\u6362\u65b9\u5f0f",
                "REX615\u7ec4\u5408\u4ee3\u7801",
                "REX615\u8ba2\u8d27\u53f7",
                "\u72b6\u6001"
            }
        };

        sheetRows.AddRange(rows.Select(row => new[]
        {
            row.SourceOrderingCode,
            row.DeviceType,
            row.ConversionMode,
            row.CompositionCode,
            row.RexOrderingNumber,
            row.Status
        }));

        var sheetData = new StringBuilder();
        for (var rowIndex = 0; rowIndex < sheetRows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            sheetData.Append($"<row r=\"{rowNumber}\">");
            for (var columnIndex = 0; columnIndex < sheetRows[rowIndex].Length; columnIndex++)
            {
                var cellRef = $"{ColumnName(columnIndex + 1)}{rowNumber}";
                sheetData.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{Escape(sheetRows[rowIndex][columnIndex])}</t></is></c>");
            }

            sheetData.Append("</row>");
        }

        return $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cols>
            <col min="1" max="1" width="24" customWidth="1"/>
            <col min="2" max="2" width="28" customWidth="1"/>
            <col min="3" max="3" width="14" customWidth="1"/>
            <col min="4" max="4" width="95" customWidth="1"/>
            <col min="5" max="5" width="24" customWidth="1"/>
            <col min="6" max="6" width="36" customWidth="1"/>
          </cols>
          <sheetData>{{sheetData}}</sheetData>
        </worksheet>
        """;
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

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string value)
    {
        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
        xmlWriter.WriteString(value);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }
}
