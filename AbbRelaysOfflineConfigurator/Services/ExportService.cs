using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using AbbRelaysOfflineConfigurator.Models;

namespace AbbRelaysOfflineConfigurator.Services;

public static class ExportService
{
    public static void ExportWord(ExportSnapshot snapshot, string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddEntry(archive, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddEntry(archive, "word/document.xml", BuildWordDocument(snapshot));
    }

    public static void ExportExcel(ExportSnapshot snapshot, string path)
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
              <sheets><sheet name="REX615" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        AddEntry(archive, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
        AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(snapshot));
    }

    public static void ExportPdf(ExportSnapshot snapshot, string path)
    {
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();

        var document = new PdfDocument();
        document.Info.Title = "ABB REX615 配置";
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        var graphics = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Microsoft YaHei", 16, XFontStyleEx.Bold);
        var headerFont = new XFont("Microsoft YaHei", 11, XFontStyleEx.Bold);
        var bodyFont = new XFont("Microsoft YaHei", 9, XFontStyleEx.Regular);
        var y = 42d;

        try
        {
            DrawLine(graphics, "ABB REX615 配置", titleFont, 40, ref y, page.Width.Point - 80);
            y += 8;
            foreach (var line in BuildTextLines(snapshot))
            {
                var font = line.EndsWith('：') ? headerFont : bodyFont;
                DrawLine(graphics, line, font, 40, ref y, page.Width.Point - 80);
                if (y <= page.Height.Point - 50)
                {
                    continue;
                }

                graphics.Dispose();
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                graphics = XGraphics.FromPdfPage(page);
                y = 42;
            }
        }
        finally
        {
            graphics.Dispose();
        }

        document.Save(path);
    }

    private static string BuildWordDocument(ExportSnapshot snapshot)
    {
        var body = new StringBuilder();
        foreach (var line in BuildTextLines(snapshot))
        {
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                .Append(Escape(line))
                .Append("</w:t></w:r></w:p>");
        }

        return $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            {{body}}
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134"/></w:sectPr>
          </w:body>
        </w:document>
        """;
    }

    private static string BuildWorksheet(ExportSnapshot snapshot)
    {
        var rows = new List<(string, string)>
        {
            ("组合代码", snapshot.CombinationCode),
            ("订货号", snapshot.OrderingNumber),
            ("状态", snapshot.Status),
            ("在线校验", snapshot.OnlineStatus)
        };
        rows.Add(("", ""));
        rows.Add(("I/O 摘要", snapshot.IoSummary.Count == 0
            ? "无"
            : string.Join("；", snapshot.IoSummary.Select(item => $"{item.Name}={item.Value}"))));
        rows.Add(("当前已选择 APP 摘要", snapshot.SelectedAppSummary));
        rows.Add(("", ""));
        rows.Add(("选项组", "选项"));
        rows.AddRange(snapshot.Selections.Select(selection => (selection.GroupName, $"{selection.Id}: {selection.Description}")));
        rows.Add(("", ""));
        rows.Add(("当前选型 APP 保护功能清单", ""));
        if (snapshot.AppFunctions.Count == 0)
        {
            rows.Add(("无", ""));
        }
        else
        {
            foreach (var appGroup in snapshot.AppFunctions.GroupBy(function => function.AppId))
            {
                rows.Add((appGroup.Key, ""));
                rows.AddRange(appGroup.Select(function => (
                    function.FunctionCode,
                    $"{FormatAnsi(function.Ansi)}{function.ChineseName} / {function.EnglishName}")));
            }
        }
        rows.Add(("", ""));
        rows.Add(("槽位", "板卡"));
        rows.AddRange(snapshot.Slots.Select(slot => (slot.SlotId, $"{slot.Code}: {slot.Description}")));
        rows.Add(("", ""));
        rows.Add(("校验消息", snapshot.Messages.Count == 0 ? "无" : string.Join("；", snapshot.Messages)));

        var sheetData = new StringBuilder();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var number = rowIndex + 1;
            sheetData.Append(CELLROW(number, row.Item1, row.Item2));
        }

        return $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cols><col min="1" max="1" width="18" customWidth="1"/><col min="2" max="2" width="90" customWidth="1"/></cols>
          <sheetData>{{sheetData}}</sheetData>
        </worksheet>
        """;

        static string CELLROW(int row, string first, string second) =>
            $"<row r=\"{row}\"><c r=\"A{row}\" t=\"inlineStr\"><is><t>{Escape(first)}</t></is></c><c r=\"B{row}\" t=\"inlineStr\"><is><t>{Escape(second)}</t></is></c></row>";
    }

    private static IReadOnlyList<string> BuildTextLines(ExportSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"组合代码：{snapshot.CombinationCode}",
            $"订货号：{snapshot.OrderingNumber}",
            $"状态：{snapshot.Status}",
            $"在线校验：{snapshot.OnlineStatus}",
            "",
            "I/O 摘要：",
            snapshot.IoSummary.Count == 0
                ? "无"
                : string.Join("；", snapshot.IoSummary.Select(item => $"{item.Name}={item.Value}")),
            "",
            "当前已选择 APP 摘要：",
            snapshot.SelectedAppSummary,
            "",
            "选型配置："
        };

        lines.AddRange(snapshot.Selections.Select(selection => $"{selection.GroupName}：{selection.Id} - {selection.Description}"));
        lines.Add("");
        lines.Add("当前选型 APP 保护功能清单：");
        if (snapshot.AppFunctions.Count == 0)
        {
            lines.Add("无");
        }
        else
        {
            foreach (var appGroup in snapshot.AppFunctions.GroupBy(function => function.AppId))
            {
                lines.Add($"{appGroup.Key}：");
                lines.AddRange(appGroup.Select(function =>
                    $"  {function.FunctionCode} - {FormatAnsi(function.Ansi)}{function.ChineseName} / {function.EnglishName}"));
            }
        }
        lines.Add("");
        lines.Add("槽位分配：");
        lines.AddRange(snapshot.Slots.Select(slot => $"{slot.SlotId}：{slot.Code} - {slot.Description}"));
        lines.Add("");
        lines.Add("校验消息：");
        lines.AddRange(snapshot.Messages.Count == 0 ? ["无"] : snapshot.Messages);
        return lines;
    }

    private static string FormatAnsi(string ansi) =>
        string.IsNullOrWhiteSpace(ansi) ? "" : $"ANSI {ansi} - ";

    private static void DrawLine(XGraphics graphics, string text, XFont font, double x, ref double y, double width)
    {
        foreach (var line in WrapLine(graphics, string.IsNullOrWhiteSpace(text) ? " " : text, font, width))
        {
            graphics.DrawString(line, font, XBrushes.Black, new XRect(x, y, width, 16), XStringFormats.TopLeft);
            y += 16;
        }
    }

    private static IEnumerable<string> WrapLine(XGraphics graphics, string text, XFont font, double width)
    {
        var current = "";
        foreach (var ch in text)
        {
            var candidate = current + ch;
            if (graphics.MeasureString(candidate, font).Width > width && current.Length > 0)
            {
                yield return current;
                current = ch.ToString();
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string value) => SecurityElementEscape(value);

    private static string SecurityElementEscape(string value)
    {
        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
        xmlWriter.WriteString(value);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }
}
