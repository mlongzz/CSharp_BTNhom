using System.Globalization;
using Bookstore.Application;
using System.IO.Compression;
using System.Xml.Linq;

namespace Bookstore.Models;

internal static class ReportWorkbook
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string SpreadsheetMime = "application/vnd.openxmlformats-officedocument.spreadsheetml";

    public static byte[] Export(ReportsViewModel model, BookstoreReport report)
    {
        var sheets = new List<Sheet>
        {
            new("Tổng quan", [
                ["Từ ngày", model.From.ToString("dd/MM/yyyy")], ["Đến ngày", model.Through.ToString("dd/MM/yyyy")],
                ["Doanh thu từ đơn hoàn tất trong kỳ (₫)", report.Revenue.Sum(day => day.Revenue)],
                ["Đơn mới hôm nay", report.NewOrdersToday], ["Đơn đang chờ xác nhận", report.PendingOrders],
                ["Số lượng thực nhập trong kỳ", report.ReceivedQuantity], ["Tiền hàng thực nhập (₫)", report.ReceivingCost]
            ])
        };
        if (model.Section is ReportSection.Overview or ReportSection.Revenue)
            sheets.Add(new("Doanh thu", Rows(["Ngày", "Đơn hoàn tất", "Doanh thu (₫)"],
                report.Revenue.Select(day => new object[] { day.Day, day.CompletedOrders, day.Revenue }))));
        if (model.Section is ReportSection.Overview or ReportSection.Orders)
            sheets.Add(new("Đơn hàng", Rows(["Trạng thái", "Số đơn"],
                report.Orders.Select(order => new object[] { ReportsViewModel.Status(order.Status), order.Count }))));
        if (model.Section is ReportSection.Overview or ReportSection.TopBooks)
            sheets.Add(new("Sách bán chạy", Rows(["Sách", "Số lượng"],
                report.TopBooks.Select(book => new object[] { book.Title, book.Quantity }))));
        if (model.Section is ReportSection.Overview or ReportSection.Inventory)
            sheets.Add(new("Tồn hiện tại", Rows(["Sách", "Tồn", "Ngưỡng", "Cảnh báo"],
                report.Inventory.Select(book => new object[]
                {
                    book.Title, book.Stock, book.Minimum, book.Stock < book.Minimum ? "Sắp hết" : "Đủ tồn"
                }))));

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", new XElement(Types + "Types",
                new XElement(Types + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                ContentType("/xl/workbook.xml", "sheet.main+xml"),
                sheets.Select((sheet, index) => ContentType($"/xl/worksheets/sheet{index + 1}.xml", "worksheet+xml"))));
            Write(archive, "_rels/.rels", new XElement(Package + "Relationships", Relationship(1, "officeDocument", "xl/workbook.xml")));
            Write(archive, "xl/workbook.xml", new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Relationships),
                new XElement(Main + "sheets", sheets.Select((sheet, index) => new XElement(Main + "sheet",
                    new XAttribute("name", sheet.Name), new XAttribute("sheetId", index + 1),
                    new XAttribute(Relationships + "id", $"rId{index + 1}"))))));
            Write(archive, "xl/_rels/workbook.xml.rels", new XElement(Package + "Relationships",
                sheets.Select((sheet, index) => Relationship(index + 1, "worksheet", $"worksheets/sheet{index + 1}.xml"))));
            for (var index = 0; index < sheets.Count; index++)
            {
                Write(archive, $"xl/worksheets/sheet{index + 1}.xml", new XElement(Main + "worksheet",
                    new XElement(Main + "cols",
                        new XElement(Main + "col", new XAttribute("min", 1), new XAttribute("max", 1),
                            new XAttribute("width", 48), new XAttribute("customWidth", 1)),
                        new XElement(Main + "col", new XAttribute("min", 2), new XAttribute("max", 4),
                            new XAttribute("width", 19), new XAttribute("customWidth", 1))),
                    new XElement(Main + "sheetData", sheets[index].Rows.Select((row, rowIndex) =>
                        new XElement(Main + "row", new XAttribute("r", rowIndex + 1),
                            row.Select((value, column) => Cell(value, $"{(char)('A' + column)}{rowIndex + 1}")))))));
            }
        }
        return buffer.ToArray();
    }

    private static IEnumerable<object[]> Rows(object[] header, IEnumerable<object[]> data) => data.Prepend(header);

    private static XElement ContentType(string name, string type) =>
        new(Types + "Override", new XAttribute("PartName", name), new XAttribute("ContentType", $"{SpreadsheetMime}.{type}"));

    private static XElement Relationship(int id, string type, string target) => new(Package + "Relationship",
        new XAttribute("Id", $"rId{id}"), new XAttribute("Type", $"{Relationships}/{type}"), new XAttribute("Target", target));

    private static XElement Cell(object value, string reference)
    {
        var cell = new XElement(Main + "c", new XAttribute("r", reference));
        if (value is int or long)
        {
            cell.Add(new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
        }
        else
        {
            var text = value.ToString() ?? "";
            cell.Add(new XAttribute("t", "inlineStr"), new XElement(Main + "is",
                new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        }

        return cell;
    }

    private static void Write(ZipArchive archive, string path, XElement content)
    {
        using var stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
        new XDocument(content).Save(stream);
    }

    private sealed record Sheet(string Name, IEnumerable<object[]> Rows);
}
