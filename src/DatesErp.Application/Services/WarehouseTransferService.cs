using DatesErp.Core.Common;
using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Domain.Enums;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §تحويل مخزني بين المخازن (سند WHT). يعتمد على البنية المخزنية القائمة فقط:
/// StockBalances (الرصيد الجاري) + InventoryTransactions (الحركات) عبر ServiceBase.PostStockMovement —
/// لا جدول رصيد جديد ولا منطق حركات مكرر. صلاحياته على مورد «inventory».
///  • مسودة: تُحفظ البنود فقط، لا أثر على المخزون.
///  • اعتماد: لكل بند حركة صادر من المصدر + وارد للمستلم بمرجع (WarehouseTransfer, رقم السند)
///    في معاملة واحدة — أي فشل (رصيد غير كافٍ/تكرار) يُلغي كل شيء ولا يبقى رصيد غير متطابق.
///  • إلغاء معتمد: عكس الحركتين بمرجع «رقم السند#REV» ثم الحالة ملغي. لا حذف لمعتمد.
/// </summary>
public class WarehouseTransferService : ServiceBase, IWarehouseTransferService
{
    private const string Module = "inventory";
    private const string Scheme = "WHT";

    public WarehouseTransferService(DatesErpDbContext db, ICurrentSession session, INumberingService numbering)
        : base(db, session, numbering) { }

    // ═══════════════════ قراءة ═══════════════════

    public List<StockByWarehouseDto> GetStockByWarehouse(int? productId, int? warehouseId, int? lotId)
    {
        Require(Module, "View");
        var q = Db.StockBalances.AsNoTracking().AsQueryable();
        if (warehouseId != null) q = q.Where(b => b.WarehouseId == warehouseId);
        if (productId != null) q = q.Where(b => b.ProductId == productId);
        if (lotId != null) q = q.Where(b => b.LotId == lotId);
        var rows = q.Where(b => b.QtyKg > 0.0005 || b.PackageCount > 0).ToList();
        if (rows.Count == 0) return new List<StockByWarehouseDto>();

        var whNames = Db.Warehouses.AsNoTracking().ToDictionary(w => w.Id, w => w.WarehouseNameAr);
        var prodIds = rows.Where(r => r.ProductId != null).Select(r => r.ProductId.Value).Distinct().ToList();
        var matIds = rows.Where(r => r.MaterialId != null).Select(r => r.MaterialId.Value).Distinct().ToList();
        var lotIds = rows.Where(r => r.LotId != null).Select(r => r.LotId.Value).Distinct().ToList();
        var custIds = rows.Where(r => r.CustomerId != null).Select(r => r.CustomerId.Value).Distinct().ToList();
        var pkgIds = rows.Where(r => r.PackagingTypeId != null).Select(r => r.PackagingTypeId.Value).Distinct().ToList();
        var prods = Db.Products.AsNoTracking().Where(p => prodIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.ProductNameAr);
        var mats = Db.AuxiliaryMaterials.AsNoTracking().Where(m => matIds.Contains(m.Id)).ToDictionary(m => m.Id, m => m.MaterialNameAr);
        var lots = Db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionary(l => l.Id, l => l.LotCode);
        var custs = Db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToDictionary(c => c.Id, c => c.CustomerName);
        var pkgs = Db.PackagingTypes.AsNoTracking().Where(p => pkgIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.PackageNameAr);

        return rows.Select(b => new StockByWarehouseDto
        {
            WarehouseId = b.WarehouseId,
            WarehouseName = whNames.TryGetValue(b.WarehouseId, out var wn) ? wn : $"#{b.WarehouseId}",
            ProductId = b.ProductId,
            MaterialId = b.MaterialId,
            ProductName = b.ProductId != null && prods.TryGetValue(b.ProductId.Value, out var pn) ? pn : null,
            MaterialName = b.MaterialId != null && mats.TryGetValue(b.MaterialId.Value, out var mn) ? mn : null,
            LotId = b.LotId,
            LotCode = b.LotId != null && lots.TryGetValue(b.LotId.Value, out var lc) ? lc : null,
            CustomerId = b.CustomerId,
            CustomerName = b.CustomerId != null && custs.TryGetValue(b.CustomerId.Value, out var cn) ? cn : null,
            PackagingTypeId = b.PackagingTypeId,
            PackagingName = b.PackagingTypeId != null && pkgs.TryGetValue(b.PackagingTypeId.Value, out var pk) ? pk : null,
            QtyKg = b.QtyKg,
            PackageCount = b.PackageCount
        })
        .OrderBy(r => r.ProductName ?? r.MaterialName).ThenBy(r => r.LotCode)
        .ToList();
    }

    public List<WarehouseTransferDto> GetTransfers()
    {
        Require(Module, "View");
        var docs = Db.WarehouseTransfers.AsNoTracking().Include(t => t.Items)
            .OrderByDescending(t => t.Id).Take(500).ToList();
        var wh = Db.Warehouses.AsNoTracking().ToDictionary(w => w.Id, w => w.WarehouseNameAr);
        return docs.Select(d => Map(d, wh, withItems: false)).ToList();
    }

    public WarehouseTransferDto GetTransfer(int id)
    {
        Require(Module, "View");
        var d = Db.WarehouseTransfers.AsNoTracking().Include(t => t.Items).FirstOrDefault(t => t.Id == id);
        if (d == null) return null;
        var wh = Db.Warehouses.AsNoTracking().ToDictionary(w => w.Id, w => w.WarehouseNameAr);
        return Map(d, wh, withItems: true);
    }

    private WarehouseTransferDto Map(WarehouseTransfer d, Dictionary<int, string> wh, bool withItems)
    {
        var dto = new WarehouseTransferDto
        {
            Id = d.Id,
            DocumentNumber = d.DocumentNumber,
            TransferDate = UiFormat.D(d.TransferDate),
            SourceWarehouseId = d.SourceWarehouseId,
            SourceWarehouseName = wh.TryGetValue(d.SourceWarehouseId, out var s) ? s : $"#{d.SourceWarehouseId}",
            DestinationWarehouseId = d.DestinationWarehouseId,
            DestinationWarehouseName = wh.TryGetValue(d.DestinationWarehouseId, out var t) ? t : $"#{d.DestinationWarehouseId}",
            Status = d.Status,
            StatusAr = DocStatuses.ToArabic(d.Status),
            IsApproved = d.IsApproved,
            Notes = d.Notes,
            CancelReason = d.CancelReason,
            TotalQtyKg = (double)d.Items.Sum(i => i.QtyKg),
            TotalPackages = d.Items.Sum(i => i.PackageCount)
        };
        if (!withItems) return dto;
        foreach (var i in d.Items.OrderBy(i => i.Id))
        {
            dto.Items.Add(new WarehouseTransferItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                MaterialId = i.MaterialId,
                LotId = i.LotId,
                CustomerId = i.CustomerId,
                PackagingTypeId = i.PackagingTypeId,
                QtyKg = (double)i.QtyKg,
                PackageCount = i.PackageCount,
                Notes = i.Notes,
                ItemName = i.ProductId != null
                    ? Db.Products.AsNoTracking().Where(p => p.Id == i.ProductId).Select(p => p.ProductNameAr).FirstOrDefault()
                    : Db.AuxiliaryMaterials.AsNoTracking().Where(m => m.Id == i.MaterialId).Select(m => m.MaterialNameAr).FirstOrDefault(),
                LotCode = Db.Lots.AsNoTracking().Where(l => l.Id == i.LotId).Select(l => l.LotCode).FirstOrDefault(),
                CustomerName = Db.Customers.AsNoTracking().Where(c => c.Id == i.CustomerId).Select(c => c.CustomerName).FirstOrDefault(),
                PackagingName = Db.PackagingTypes.AsNoTracking().Where(p => p.Id == i.PackagingTypeId).Select(p => p.PackageNameAr).FirstOrDefault()
            });
        }
        return dto;
    }

    // ═══════════════════ حفظ/تعديل المسودة ═══════════════════

    public OpResult SaveTransfer(string transferDate, int sourceWarehouseId, int destinationWarehouseId,
        List<WarehouseTransferItemDto> items, string notes)
    {
        Require(Module, "Create");
        var err = ValidateHeaderAndItems(sourceWarehouseId, destinationWarehouseId, items, out var clean);
        if (err != null) return OpResult.Fail(err);

        return RunOp(() =>
        {
            var doc = new WarehouseTransfer
            {
                DocumentNumber = Numbering.Next(Scheme),
                TransferDate = UiFormat.TryParseDate(transferDate, out var d) ? d : DateTime.Now,
                SourceWarehouseId = sourceWarehouseId,
                DestinationWarehouseId = destinationWarehouseId,
                Notes = notes,
                Status = DocStatuses.Draft,
                CreatedBy = Session.UserId,
                CreatedDate = DateTime.Now
            };
            foreach (var i in clean) doc.Items.Add(ToEntity(i));
            Db.WarehouseTransfers.Add(doc);
            Db.SaveChanges();
            return OpResult.Success(
                $"تم حفظ مسودة التحويل ({clean.Count} بند، {clean.Sum(x => x.QtyKg):N1} كجم). لم يُمس المخزون بعد — اعتمد السند لتنفيذ النقل.",
                doc.Id, doc.DocumentNumber);
        });
    }

    public OpResult UpdateTransfer(int id, string transferDate, int sourceWarehouseId, int destinationWarehouseId,
        List<WarehouseTransferItemDto> items, string notes)
    {
        Require(Module, "Edit");
        var err = ValidateHeaderAndItems(sourceWarehouseId, destinationWarehouseId, items, out var clean);
        if (err != null) return OpResult.Fail(err);

        return RunOp(() =>
        {
            var doc = Db.WarehouseTransfers.Include(t => t.Items).FirstOrDefault(t => t.Id == id)
                      ?? throw new DomainException("سند التحويل غير موجود.");
            if (doc.IsApproved || doc.Status == DocStatuses.Approved)
                throw new DomainException($"السند {doc.DocumentNumber} معتمد ونُفّذت حركاته — لا يُعدَّل. ألغِ الاعتماد (عكس) ثم أنشئ سنداً جديداً.");
            if (doc.Status == DocStatuses.Cancelled)
                throw new DomainException($"السند {doc.DocumentNumber} ملغي — لا يُعدَّل.");

            doc.TransferDate = UiFormat.TryParseDate(transferDate, out var d) ? d : doc.TransferDate;
            doc.SourceWarehouseId = sourceWarehouseId;
            doc.DestinationWarehouseId = destinationWarehouseId;
            doc.Notes = notes;
            doc.ModifiedBy = Session.UserId;
            doc.ModifiedDate = DateTime.Now;
            Db.WarehouseTransferItems.RemoveRange(doc.Items);
            doc.Items.Clear();
            foreach (var i in clean) doc.Items.Add(ToEntity(i));
            Db.SaveChanges();
            return OpResult.Success($"تم تحديث مسودة التحويل ({clean.Count} بند، {clean.Sum(x => x.QtyKg):N1} كجم).", doc.Id, doc.DocumentNumber);
        });
    }

    private static WarehouseTransferItem ToEntity(WarehouseTransferItemDto i) => new()
    {
        ProductId = i.ProductId,
        MaterialId = i.MaterialId,
        LotId = i.LotId,
        CustomerId = i.CustomerId,
        PackagingTypeId = i.PackagingTypeId,
        QtyKg = Math.Round((decimal)i.QtyKg, 3),
        PackageCount = i.PackageCount,
        Notes = i.Notes
    };

    /// <summary>تحقق مشترك: مخزنان مختلفان فعّالان، بنود موجبة، لا تكرار لنفس مفتاح الرصيد، وكل بند ضمن رصيد المصدر المتاح.</summary>
    private string ValidateHeaderAndItems(int src, int dst, List<WarehouseTransferItemDto> items, out List<WarehouseTransferItemDto> clean)
    {
        clean = new List<WarehouseTransferItemDto>();
        if (src <= 0) return "اختر المخزن المصدر.";
        if (dst <= 0) return "اختر المخزن المستلم.";
        if (src == dst) return "المخزن المصدر والمستلم لا يمكن أن يكونا نفس المخزن.";
        var whs = Db.Warehouses.AsNoTracking().Where(w => w.Id == src || w.Id == dst).ToList();
        var s = whs.FirstOrDefault(w => w.Id == src);
        var t = whs.FirstOrDefault(w => w.Id == dst);
        if (s == null) return "المخزن المصدر غير معرّف.";
        if (t == null) return "المخزن المستلم غير معرّف.";
        if (!s.IsActive) return $"المخزن المصدر «{s.WarehouseNameAr}» غير فعّال.";
        if (!t.IsActive) return $"المخزن المستلم «{t.WarehouseNameAr}» غير فعّال.";

        if (items == null || items.Count == 0) return "أدخل بنداً واحداً على الأقل.";
        var keys = new HashSet<string>();
        int n = 0;
        foreach (var i in items)
        {
            n++;
            if (i == null) continue;
            if (i.ProductId == null && i.MaterialId == null) return $"البند {n}: اختر الصنف أو المادة.";
            if (i.ProductId != null && i.MaterialId != null) return $"البند {n}: لا يجوز أن يكون صنفاً ومادة معاً.";
            if (double.IsNaN(i.QtyKg) || i.QtyKg <= 0) return $"البند {n}: الكمية يجب أن تكون أكبر من صفر.";
            if (i.PackageCount < 0) return $"البند {n}: عدد العبوات لا يكون سالباً.";
            var key = $"{i.ProductId}|{i.MaterialId}|{i.LotId}|{i.CustomerId}|{i.PackagingTypeId}";
            if (!keys.Add(key)) return $"البند {n}: مكرر — نفس الصنف/الدفعة/العميل/العبوة موجود في بند آخر؛ ادمج الكمية في بند واحد.";

            // الرصيد المتاح في المصدر لمفتاح الرصيد نفسه (نفس منطق PostStockMovement عند الاعتماد)
            var bal = Db.StockBalances.AsNoTracking().FirstOrDefault(b =>
                b.WarehouseId == src && b.ProductId == i.ProductId && b.MaterialId == i.MaterialId &&
                b.LotId == i.LotId && b.CustomerId == i.CustomerId && b.PackagingTypeId == i.PackagingTypeId);
            double avail = bal?.QtyKg ?? 0;
            int availPkg = bal?.PackageCount ?? 0;
            if (i.QtyKg > avail + 0.0005)
                return $"البند {n}: الكمية {i.QtyKg:N1} كجم أكبر من المتاح في «{s.WarehouseNameAr}» ({avail:N1} كجم) لنفس الدفعة/العميل/العبوة.";
            if (i.PackageCount > availPkg)
                return $"البند {n}: عدد العبوات {i.PackageCount:N0} أكبر من المتاح ({availPkg:N0}).";
            clean.Add(i);
        }
        if (clean.Count == 0) return "أدخل بنداً واحداً على الأقل.";
        return null;
    }

    // ═══════════════════ اعتماد / إلغاء / حذف ═══════════════════

    public OpResult ApproveTransfer(int id)
    {
        Require(Module, "Approve");
        return RunOp(() =>
        {
            var doc = Db.WarehouseTransfers.Include(t => t.Items).FirstOrDefault(t => t.Id == id)
                      ?? throw new DomainException("سند التحويل غير موجود.");
            if (doc.IsApproved || doc.Status == DocStatuses.Approved)
                throw new DomainException($"السند {doc.DocumentNumber} معتمد مسبقاً — لا يُعاد تنفيذ التحويل.");
            if (doc.Status == DocStatuses.Cancelled)
                throw new DomainException($"السند {doc.DocumentNumber} ملغي — لا يُعتمد.");
            if (doc.Items.Count == 0) throw new DomainException("السند بلا بنود.");
            if (doc.SourceWarehouseId == doc.DestinationWarehouseId)
                throw new DomainException("المخزن المصدر والمستلم متطابقان.");
            // حماية إضافية ضد تنفيذ مزدوج (جهازان): أي حركة سابقة بنفس المرجع تعني أنه نُفّذ
            if (Db.InventoryTransactions.Any(x => x.ReferenceDocType == ReferenceDocType.WarehouseTransfer && x.ReferenceDocNumber == doc.DocumentNumber))
                throw new DomainException($"توجد حركات مخزون منفذة مسبقاً للسند {doc.DocumentNumber} — لا يُكرر التنفيذ.");

            var whSrc = Db.Warehouses.AsNoTracking().FirstOrDefault(w => w.Id == doc.SourceWarehouseId)?.WarehouseNameAr ?? $"#{doc.SourceWarehouseId}";
            var whDst = Db.Warehouses.AsNoTracking().FirstOrDefault(w => w.Id == doc.DestinationWarehouseId)?.WarehouseNameAr ?? $"#{doc.DestinationWarehouseId}";
            string note = $"تحويل مخزني {doc.DocumentNumber}: {whSrc} ← {whDst}";

            foreach (var i in doc.Items)
            {
                if (i.QtyKg <= 0) throw new DomainException("بند بكمية صفر أو سالبة — لا يُنفَّذ.");
                double qty = (double)i.QtyKg;
                // صادر من المصدر — PostStockMovement يرفض الرصيد السالب ويرفض التكرار
                PostStockMovement(doc.SourceWarehouseId, MovementType.Outbound, qty, i.PackageCount,
                    ReferenceDocType.WarehouseTransfer, doc.DocumentNumber,
                    productId: i.ProductId, materialId: i.MaterialId, lotId: i.LotId,
                    customerId: i.CustomerId, packagingTypeId: i.PackagingTypeId, notes: note);
                // وارد للمستلم بنفس المفتاح (الدفعة/العميل/العبوة تبقى مرتبطة بالمصدر)
                PostStockMovement(doc.DestinationWarehouseId, MovementType.Inbound, qty, i.PackageCount,
                    ReferenceDocType.WarehouseTransfer, doc.DocumentNumber,
                    productId: i.ProductId, materialId: i.MaterialId, lotId: i.LotId,
                    customerId: i.CustomerId, packagingTypeId: i.PackagingTypeId, notes: note);
            }

            doc.Status = DocStatuses.Approved;
            doc.IsApproved = true;
            doc.ApprovedBy = Session.UserId;
            doc.ApprovedDate = DateTime.Now;
            doc.IsPosted = true;
            doc.PostedDate = DateTime.Now;
            doc.StatusReason = null;
            doc.ModifiedBy = Session.UserId;
            doc.ModifiedDate = DateTime.Now;
            Db.SaveChanges();
            return OpResult.Success(
                $"تم اعتماد التحويل وتنفيذ النقل: {doc.Items.Count} بند، {(double)doc.Items.Sum(x => x.QtyKg):N1} كجم من «{whSrc}» إلى «{whDst}».",
                doc.Id, doc.DocumentNumber);
        });
    }

    public OpResult CancelTransfer(int id, string reason)
    {
        Require(Module, "Cancel");
        return RunOp(() =>
        {
            var doc = Db.WarehouseTransfers.Include(t => t.Items).FirstOrDefault(t => t.Id == id)
                      ?? throw new DomainException("سند التحويل غير موجود.");
            if (doc.Status == DocStatuses.Cancelled)
                throw new DomainException($"السند {doc.DocumentNumber} ملغي مسبقاً.");

            string revRef = doc.DocumentNumber + "#REV";
            if (doc.IsApproved || doc.Status == DocStatuses.Approved)
            {
                if (Db.InventoryTransactions.Any(x => x.ReferenceDocType == ReferenceDocType.WarehouseTransfer && x.ReferenceDocNumber == revRef))
                    throw new DomainException($"حركات العكس للسند {doc.DocumentNumber} منفذة مسبقاً.");
                string note = $"عكس تحويل مخزني {doc.DocumentNumber}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}");
                foreach (var i in doc.Items)
                {
                    double qty = (double)i.QtyKg;
                    // سحب من المستلم (يفشل إن صُرفت الكمية هناك — فلا رصيد سالب) ثم إعادة للمصدر
                    PostStockMovement(doc.DestinationWarehouseId, MovementType.Outbound, qty, i.PackageCount,
                        ReferenceDocType.WarehouseTransfer, revRef,
                        productId: i.ProductId, materialId: i.MaterialId, lotId: i.LotId,
                        customerId: i.CustomerId, packagingTypeId: i.PackagingTypeId, notes: note);
                    PostStockMovement(doc.SourceWarehouseId, MovementType.Inbound, qty, i.PackageCount,
                        ReferenceDocType.WarehouseTransfer, revRef,
                        productId: i.ProductId, materialId: i.MaterialId, lotId: i.LotId,
                        customerId: i.CustomerId, packagingTypeId: i.PackagingTypeId, notes: note);
                }
            }

            doc.Status = DocStatuses.Cancelled;
            doc.IsApproved = false;
            doc.IsPosted = false;
            doc.CancelReason = reason;
            doc.CancelledDate = DateTime.Now;
            doc.StatusReason = reason;
            doc.ModifiedBy = Session.UserId;
            doc.ModifiedDate = DateTime.Now;
            Db.SaveChanges();
            return OpResult.Success(
                doc.Items.Count > 0 && Db.InventoryTransactions.Any(x => x.ReferenceDocNumber == revRef)
                    ? $"أُلغي التحويل {doc.DocumentNumber} وعُكست حركاته — عادت الكميات إلى المخزن المصدر."
                    : $"أُلغيت مسودة التحويل {doc.DocumentNumber} (لم يكن لها أثر مخزني).",
                doc.Id, doc.DocumentNumber);
        });
    }

    public OpResult DeleteDraft(int id)
    {
        Require(Module, "Delete");
        return RunOp(() =>
        {
            var doc = Db.WarehouseTransfers.Include(t => t.Items).FirstOrDefault(t => t.Id == id)
                      ?? throw new DomainException("سند التحويل غير موجود.");
            if (doc.IsApproved || doc.Status == DocStatuses.Approved)
                throw new DomainException($"السند {doc.DocumentNumber} معتمد — لا يُحذف. استخدم الإلغاء (عكس).");
            if (Db.InventoryTransactions.Any(x => x.ReferenceDocType == ReferenceDocType.WarehouseTransfer && x.ReferenceDocNumber == doc.DocumentNumber))
                throw new DomainException($"للسند {doc.DocumentNumber} حركات مخزون — لا يُحذف.");
            var no = doc.DocumentNumber;
            Db.WarehouseTransferItems.RemoveRange(doc.Items);
            Db.WarehouseTransfers.Remove(doc);
            Db.SaveChanges();
            return OpResult.Success($"حُذفت مسودة التحويل {no}.");
        });
    }
}
