namespace DatesErp.Core.Interfaces.Services;

/// <summary>سطر رصيد في مخزن محدد — المفتاح الكامل للرصيد كما في StockBalance (صنف/مادة + دفعة + عميل + عبوة).</summary>
public class StockByWarehouseDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public int? ProductId { get; set; }
    public int? MaterialId { get; set; }
    public string ProductName { get; set; }
    public string MaterialName { get; set; }
    public int? LotId { get; set; }
    public string LotCode { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; }
    public int? PackagingTypeId { get; set; }
    public string PackagingName { get; set; }
    public double QtyKg { get; set; }
    public int PackageCount { get; set; }
}

/// <summary>بند تحويل مخزني (إدخال وعرض).</summary>
public class WarehouseTransferItemDto
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? MaterialId { get; set; }
    public int? LotId { get; set; }
    public int? CustomerId { get; set; }
    public int? PackagingTypeId { get; set; }
    public double QtyKg { get; set; }
    public int PackageCount { get; set; }
    public string Notes { get; set; }
    // للعرض فقط — تُملأ عند القراءة
    public string ItemName { get; set; }
    public string LotCode { get; set; }
    public string CustomerName { get; set; }
    public string PackagingName { get; set; }
}

/// <summary>سند تحويل مخزني كامل.</summary>
public class WarehouseTransferDto
{
    public int Id { get; set; }
    public string DocumentNumber { get; set; }
    /// <summary>dd/MM/yyyy</summary>
    public string TransferDate { get; set; }
    public int SourceWarehouseId { get; set; }
    public string SourceWarehouseName { get; set; }
    public int DestinationWarehouseId { get; set; }
    public string DestinationWarehouseName { get; set; }
    public string Status { get; set; }
    public string StatusAr { get; set; }
    public bool IsApproved { get; set; }
    public string Notes { get; set; }
    public string CancelReason { get; set; }
    public double TotalQtyKg { get; set; }
    public int TotalPackages { get; set; }
    public List<WarehouseTransferItemDto> Items { get; set; } = new();
}

/// <summary>
/// §تحويل مخزني بين المخازن. المسودة لا تمس المخزون؛ الاعتماد ينفّذ (صادر من المصدر + وارد للمستلم)
/// لكل بند في معاملة واحدة عبر PostStockMovement (منع السالب + منع التكرار)؛ الإلغاء يعكس الحركات.
/// </summary>
public interface IWarehouseTransferService
{
    /// <summary>أرصدة مخزن محدد (أو كل المخازن) — الأرصدة غير الصفرية فقط.</summary>
    List<StockByWarehouseDto> GetStockByWarehouse(int? productId, int? warehouseId, int? lotId);
    List<WarehouseTransferDto> GetTransfers();
    WarehouseTransferDto GetTransfer(int id);
    /// <summary>إنشاء مسودة جديدة. التاريخ dd/MM/yyyy.</summary>
    OpResult SaveTransfer(string transferDate, int sourceWarehouseId, int destinationWarehouseId, List<WarehouseTransferItemDto> items, string notes);
    /// <summary>تعديل مسودة قائمة (غير معتمدة) بنفس رقمها.</summary>
    OpResult UpdateTransfer(int id, string transferDate, int sourceWarehouseId, int destinationWarehouseId, List<WarehouseTransferItemDto> items, string notes);
    OpResult ApproveTransfer(int id);
    /// <summary>إلغاء: مسودة ← ملغية بلا أثر؛ معتمد ← عكس الحركات ثم ملغي.</summary>
    OpResult CancelTransfer(int id, string reason);
    OpResult DeleteDraft(int id);
}
