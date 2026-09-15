using DatesErp.Core.Domain.Entities;
using DatesErp.Core.Exceptions;
using DatesErp.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Application.Services;

/// <summary>
/// §1.50.63 — نظام إدارة الأصناف المساعدة وصرفها للإنتاج
/// يغطي: تهيئة الأصناف المساعدة، ربطها بالصنف التام، حساب المطلوب، صرف، تتبع، تقارير.
/// </summary>
public class AuxiliaryManagementService : ServiceBase
{
    public AuxiliaryManagementService(DatesErp.Infrastructure.Persistence.DatesErpDbContext db, ICurrentSession session, INumberingService numbering)
        : base(db, session, numbering) { }

    // ═══════════════════════════════════════════════════════════════
    // أولًا: تهيئة الأصناف المساعدة
    // ═══════════════════════════════════════════════════════════════

    /// <summary>قائمة الأصناف المساعدة المعرّفة في بطاقة الأصناف (ItemType=Auxiliary/Pack/ByProduct) مع إعداداتها.</summary>
    public List<AuxiliarySetupDto> GetAuxiliarySetups()
    {
        var products = Db.Products.AsNoTracking()
            .Where(p => (p.ItemType == "Auxiliary" || p.ItemType == "Pack" || p.GroupCode == "004") && p.IsActive)
            .OrderBy(p => p.ProductNameAr).ToList();

        var configs = Db.AuxiliaryProductConfigs.AsNoTracking().ToDictionary(c => c.ProductId);

        return products.Select(p =>
        {
            configs.TryGetValue(p.Id, out var cfg);
            return new AuxiliarySetupDto
            {
                ProductId = p.Id,
                ProductCode = p.ProductCode,
                ProductName = p.ProductNameAr,
                GroupCode = cfg?.GroupCode ?? p.GroupCode,
                BaseUnit = cfg?.BaseUnit ?? p.UnitOfMeasure,
                UnitWeightKg = cfg?.UnitWeightKg ?? p.CartonWeightKg,
                DispensingMethod = cfg?.DispensingMethod ?? "ByUnit",
                DispensingMethodAr = ToDispensingAr(cfg?.DispensingMethod ?? "ByUnit"),
                NeedsIssueOnOrder = cfg?.NeedsIssueOnOrder ?? true,
                IsActive = cfg?.IsActive ?? p.IsActive,
                IsConfigured = cfg != null,
                Notes = cfg?.Notes
            };
        }).ToList();
    }

    public OpResult SaveAuxiliarySetup(int productId, string groupCode, string baseUnit, decimal unitWeight, string dispensingMethod, bool needsIssue, bool isActive, string notes = null)
    {
        Require("products", "Edit");
        var product = Db.Products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return OpResult.Fail("الصنف غير موجود في بطاقة الأصناف — عرّفه أولاً في شاشة الأصناف.");
        if (product.ItemType != "Auxiliary" && product.ItemType != "Pack" && product.GroupCode != "004")
        {
            // تحويل تلقائي إلى مساعد إذا كان تاماً؟ لا — نطلب من المستخدم تغيير النوع في البطاقة
            // لكن نسمح بالتهيئة مع تحذير
        }

        return RunOp(() =>
        {
            var cfg = Db.AuxiliaryProductConfigs.FirstOrDefault(c => c.ProductId == productId);
            if (cfg == null)
            {
                cfg = new AuxiliaryProductConfig { ProductId = productId };
                Db.AuxiliaryProductConfigs.Add(cfg);
            }
            cfg.GroupCode = string.IsNullOrWhiteSpace(groupCode) ? product.GroupCode : groupCode.Trim();
            cfg.BaseUnit = string.IsNullOrWhiteSpace(baseUnit) ? product.UnitOfMeasure : baseUnit.Trim();
            cfg.UnitWeightKg = unitWeight;
            cfg.DispensingMethod = string.IsNullOrWhiteSpace(dispensingMethod) ? "ByUnit" : dispensingMethod.Trim();
            cfg.NeedsIssueOnOrder = needsIssue;
            cfg.IsActive = isActive;
            cfg.Notes = notes?.Trim();
            Db.SaveChanges();
            return OpResult.Success($"تم حفظ تهيئة الصنف المساعد {product.ProductNameAr}.", cfg.Id);
        });
    }

    private static string ToDispensingAr(string method) => method switch
    {
        "ByUnit" => "بالوحدة",
        "ByKilo" => "بالكيلو",
        "PerProduction" => "حسب كمية الإنتاج",
        _ => method
    };

    // ═══════════════════════════════════════════════════════════════
    // ثانيًا: ربط الصنف المساعد بالصنف التام (BOM)
    // ═══════════════════════════════════════════════════════════════

    public List<ProductAuxiliaryRequirementDto> GetRequirementsForFinished(int finishedProductId)
    {
        var reqs = Db.ProductAuxiliaryRequirements.AsNoTracking()
            .Where(r => r.FinishedProductId == finishedProductId && r.IsActive)
            .OrderBy(r => r.Id).ToList();

        var auxIds = reqs.Select(r => r.AuxiliaryProductId).Distinct().ToList();
        var auxProducts = Db.Products.AsNoTracking().Where(p => auxIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.ProductNameAr);

        return reqs.Select(r => new ProductAuxiliaryRequirementDto
        {
            Id = r.Id,
            FinishedProductId = r.FinishedProductId,
            AuxiliaryProductId = r.AuxiliaryProductId,
            AuxiliaryProductName = auxProducts.GetValueOrDefault(r.AuxiliaryProductId, $"صنف #{r.AuxiliaryProductId}"),
            Unit = r.Unit,
            QtyPerCarton = r.QtyPerCarton,
            CalculationMethod = r.CalculationMethod,
            CalculationMethodAr = ToCalcAr(r.CalculationMethod),
            IsActive = r.IsActive,
            Notes = r.Notes
        }).ToList();
    }

    public List<Product> GetFinishedProducts() => Db.Products.AsNoTracking()
        .Where(p => p.ItemType == "Finished" && p.IsActive).OrderBy(p => p.ProductNameAr).ToList();

    public List<Product> GetAuxiliaryProducts() => Db.Products.AsNoTracking()
        .Where(p => (p.ItemType == "Auxiliary" || p.ItemType == "Pack" || p.GroupCode == "004") && p.IsActive)
        .OrderBy(p => p.ProductNameAr).ToList();

    public OpResult SaveRequirement(int? id, int finishedProductId, int auxiliaryProductId, string unit, decimal qtyPerCarton, string calcMethod, string notes = null)
    {
        Require("products", "Edit");
        if (finishedProductId == auxiliaryProductId) return OpResult.Fail("لا يمكن ربط الصنف بنفسه.");
        var finished = Db.Products.FirstOrDefault(p => p.Id == finishedProductId && p.ItemType == "Finished");
        if (finished == null) return OpResult.Fail("الصنف التام غير موجود أو ليس تاماً.");
        var aux = Db.Products.FirstOrDefault(p => p.Id == auxiliaryProductId);
        if (aux == null) return OpResult.Fail("الصنف المساعد غير موجود.");

        return RunOp(() =>
        {
            var req = id == null ? new ProductAuxiliaryRequirement() : Db.ProductAuxiliaryRequirements.FirstOrDefault(r => r.Id == id);
            if (req == null) throw new DomainException("الاحتياج غير موجود.");
            if (Db.ProductAuxiliaryRequirements.Any(r => r.FinishedProductId == finishedProductId && r.AuxiliaryProductId == auxiliaryProductId && r.Id != req.Id && r.IsActive))
                throw new DomainException("هذا الصنف المساعد مرتبط مسبقاً بهذا الصنف التام — عدّل الكمية بدل التكرار.");

            req.FinishedProductId = finishedProductId;
            req.AuxiliaryProductId = auxiliaryProductId;
            req.Unit = string.IsNullOrWhiteSpace(unit) ? aux.UnitOfMeasure : unit.Trim();
            req.QtyPerCarton = qtyPerCarton;
            req.CalculationMethod = string.IsNullOrWhiteSpace(calcMethod) ? "PerCarton" : calcMethod.Trim();
            req.IsActive = true;
            req.Notes = notes?.Trim();
            if (id == null) Db.ProductAuxiliaryRequirements.Add(req);
            Db.SaveChanges();
            return OpResult.Success($"تم حفظ احتياج {aux.ProductNameAr} للصنف {finished.ProductNameAr} — {qtyPerCarton} {req.Unit} لكل كرتون.", req.Id);
        });
    }

    public OpResult DeleteRequirement(int id)
    {
        Require("products", "Delete");
        return RunOp(() =>
        {
            var req = Db.ProductAuxiliaryRequirements.FirstOrDefault(r => r.Id == id);
            if (req == null) throw new DomainException("الاحتياج غير موجود.");
            req.IsActive = false;
            Db.SaveChanges();
            return OpResult.Success("تم إيقاف الاحتياج — لن يُحتسب في الأوامر الجديدة، والقديمة محفوظة.");
        });
    }

    private static string ToCalcAr(string m) => m switch
    {
        "PerCarton" => "لكل كرتون",
        "PerKg" => "لكل كجم",
        "PerProduction" => "حسب كمية الإنتاج",
        _ => m
    };

    // ═══════════════════════════════════════════════════════════════
    // ثالثًا ورابعًا: حساب المطلوب وعرضه عند أمر الإنتاج
    // ═══════════════════════════════════════════════════════════════

    /// <summary>حساب احتياجات الأصناف المساعدة لأمر إنتاج حسب كميته (كراتين).</summary>
    public List<AuxiliaryNeedDto> CalculateNeedsForOrder(int orderId)
    {
        var order = Db.ProductionOrders.AsNoTracking().Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
        if (order == null) throw new DomainException("أمر الإنتاج غير موجود.");

        // تجميع كراتين لكل صنف تام
        var cartonsByProduct = order.Items.GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.PlannedCartons));

        var needs = new Dictionary<int, AuxiliaryNeedDto>();

        foreach (var kv in cartonsByProduct)
        {
            int finishedId = kv.Key;
            int cartons = kv.Value;
            var reqs = Db.ProductAuxiliaryRequirements.AsNoTracking()
                .Where(r => r.FinishedProductId == finishedId && r.IsActive).ToList();

            foreach (var r in reqs)
            {
                // §1.50.64 — ربط كراتين العميل: استبدال العام بماركة العميل
                int effectiveAuxId = r.AuxiliaryProductId;
                if (order.CustomerId != null)
                {
                    var packagingTypeId = order.Items.Where(i => i.ProductId == finishedId).Select(i => i.PackagingTypeId).FirstOrDefault();
                    effectiveAuxId = ResolveAuxProductForCustomerInternal(r.AuxiliaryProductId, order.CustomerId, finishedId, packagingTypeId);
                }

                decimal required = 0;
                switch (r.CalculationMethod)
                {
                    case "PerCarton":
                    default:
                        required = cartons * r.QtyPerCarton;
                        break;
                    case "PerKg":
                        var totalKg = order.Items.Where(i => i.ProductId == finishedId).Sum(i => i.PlannedQtyKg);
                        required = (decimal)totalKg * r.QtyPerCarton;
                        break;
                    case "PerProduction":
                        required = cartons * r.QtyPerCarton;
                        break;
                }

                if (!needs.TryGetValue(effectiveAuxId, out var existing))
                {
                    var auxProd = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == effectiveAuxId);
                    var originalProd = effectiveAuxId != r.AuxiliaryProductId ? Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == r.AuxiliaryProductId) : null;
                    string details = originalProd != null
                        ? $"{cartons:N0} كرتون × {r.QtyPerCarton} {r.Unit} = {required:N3} (عام: {originalProd.ProductNameAr} → ماركة عميل: {auxProd?.ProductNameAr})"
                        : $"{cartons:N0} كرتون × {r.QtyPerCarton} {r.Unit} = {required:N3}";
                    needs[effectiveAuxId] = new AuxiliaryNeedDto
                    {
                        AuxiliaryProductId = effectiveAuxId,
                        AuxiliaryProductName = auxProd?.ProductNameAr ?? $"صنف #{effectiveAuxId}",
                        Unit = r.Unit ?? auxProd?.UnitOfMeasure ?? "وحدة",
                        RequiredQty = Math.Round(required, 3),
                        CalculationDetails = details,
                        GenericAuxiliaryProductId = effectiveAuxId != r.AuxiliaryProductId ? r.AuxiliaryProductId : null
                    };
                }
                else
                {
                    existing.RequiredQty = Math.Round(existing.RequiredQty + required, 3);
                    existing.CalculationDetails += $" + {required:N3}";
                }
            }
        }

        // جلب المصروف والمتبقي والمتاح
        var materials = Db.ProductionOrderMaterials.AsNoTracking().Where(m => m.OrderId == orderId).ToList();
        var auxConfigs = Db.AuxiliaryProductConfigs.AsNoTracking().ToDictionary(c => c.ProductId);
        var whAux = Db.Warehouses.AsNoTracking().FirstOrDefault(w => w.WarehouseCode == "WAUX")?.Id ?? 0;

        foreach (var need in needs.Values)
        {
            var mat = materials.FirstOrDefault(m => m.AuxiliaryProductId == need.AuxiliaryProductId);
            if (mat != null)
            {
                need.IssuedQty = (decimal)mat.ActualIssuedQty;
                need.RemainingQty = Math.Max(0, need.RequiredQty - (decimal)mat.ActualIssuedQty);
            }
            else
            {
                need.IssuedQty = 0;
                need.RemainingQty = need.RequiredQty;
            }

            // المتاح في المخزن
            decimal available = 0;
            if (whAux != 0)
            {
                // §1.50.66 — مفتاح الرصيد يشمل PackagingTypeId + CustomerId (للمساعد نستخدم nulls لكن نذكرها صراحة لمنع خلط)
                available = Db.StockBalances.AsNoTracking()
                    .Where(b => b.WarehouseId == whAux && b.ProductId == need.AuxiliaryProductId && b.LotId == null && b.CustomerId == null)
                    .Sum(b => b.QtyKg + b.PackageCount);
                var bal = Db.StockBalances.AsNoTracking().FirstOrDefault(b => b.WarehouseId == whAux && b.ProductId == need.AuxiliaryProductId && b.LotId == null && b.CustomerId == null && b.PackagingTypeId == null);
                if (bal != null)
                {
                    available = need.Unit.Contains("كجم") || need.Unit == "Kg" ? bal.QtyKg : (bal.PackageCount > 0 ? bal.PackageCount : bal.QtyKg);
                }
            }
            need.AvailableQty = available;
            need.StockStatus = available >= need.RemainingQty ? "متوفر" : available > 0 ? "جزئي" : "غير متوفر";
            need.StockStatusAr = need.StockStatus;
            need.NeedsIssue = auxConfigs.TryGetValue(need.AuxiliaryProductId, out var cfg) ? cfg.NeedsIssueOnOrder : true;
        }

        // إضافة مواد من النظام القديم (ConsumptionFormula) إن وجدت ولم تُغطّ بالنظام الجديد
        var oldMaterials = Db.ProductionOrderMaterials.AsNoTracking().Where(m => m.OrderId == orderId && m.AuxiliaryProductId == null).ToList();
        foreach (var om in oldMaterials)
        {
            if (needs.ContainsKey(om.MaterialId)) continue; // لا نكرر
            var auxMat = Db.AuxiliaryMaterials.AsNoTracking().FirstOrDefault(a => a.Id == om.MaterialId);
            needs[om.MaterialId + 1000000] = new AuxiliaryNeedDto // مفتاح وهمي للقديم
            {
                AuxiliaryProductId = null,
                MaterialId = om.MaterialId,
                AuxiliaryProductName = auxMat?.MaterialNameAr ?? $"مادة #{om.MaterialId}",
                Unit = om.UnitOfMeasure ?? auxMat?.UnitOfMeasure ?? "وحدة",
                RequiredQty = (decimal)om.CalculatedQty,
                IssuedQty = (decimal)om.ActualIssuedQty,
                RemainingQty = (decimal)(om.CalculatedQty - om.ActualIssuedQty),
                AvailableQty = 0,
                StockStatus = "قديم",
                StockStatusAr = "قديم",
                IsLegacy = true
            };
        }

        return needs.Values.OrderBy(n => n.AuxiliaryProductName).ToList();
    }

    /// <summary>§1.50.64 — حل صنف مساعد العميل داخلياً (نفس منطق ProductionOrderService).</summary>
    private int ResolveAuxProductForCustomerInternal(int genericAuxProductId, int? customerId, int? productId, int? packagingTypeId)
    {
        if (customerId == null) return genericAuxProductId;
        var specs = Db.AuxCustomerSpecs.AsNoTracking()
            .Where(x => x.IsActive && x.CustomerId == customerId)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.ProductId != null)
            .ThenByDescending(x => x.PackagingTypeId != null)
            .ToList();
        foreach (var spec in specs)
        {
            if (spec.ProductId != null && productId != null && spec.ProductId != productId) continue;
            if (spec.PackagingTypeId != null && packagingTypeId != null && spec.PackagingTypeId != packagingTypeId) continue;
            if (spec.GenericAuxiliaryProductId != null && spec.GenericAuxiliaryProductId != genericAuxProductId) continue;
            if (spec.AuxiliaryProductId != null && spec.AuxiliaryProductId != 0)
                return spec.AuxiliaryProductId.Value;
            if (spec.MaterialId != 0)
            {
                var mat = Db.AuxiliaryMaterials.AsNoTracking().FirstOrDefault(m => m.Id == spec.MaterialId);
                if (mat != null)
                {
                    var brandedProduct = Db.Products.AsNoTracking()
                        .FirstOrDefault(p => (p.ItemType == "Auxiliary" || p.ItemType == "Pack" || p.GroupCode == "004")
                                            && (p.ProductNameAr.Contains(spec.BrandName ?? "") || p.ProductNameAr == mat.MaterialNameAr));
                    if (brandedProduct != null) return brandedProduct.Id;
                }
            }
        }
        return genericAuxProductId;
    }

    // ═══════════════════════════════════════════════════════════════
    // خامسًا وسادسًا: التحقق من المخزون والصرف الجزئي
    // ═══════════════════════════════════════════════════════════════

    public OpResult IssueAuxiliary(int orderId, int auxiliaryProductId, decimal qty, string notes = null)
    {
        Require("materials", "Post");
        if (qty <= 0) return OpResult.Fail("كمية الصرف يجب أن تكون أكبر من صفر.");

        var order = Db.ProductionOrders.Include(o => o.Materials).FirstOrDefault(o => o.Id == orderId);
        if (order == null) return OpResult.Fail("أمر الإنتاج غير موجود.");
        if (!order.IsApproved) return OpResult.Fail("لا يمكن صرف مواد لأمر غير معتمد.");

        var needs = CalculateNeedsForOrder(orderId);
        var need = needs.FirstOrDefault(n => n.AuxiliaryProductId == auxiliaryProductId);
        if (need == null) return OpResult.Fail("هذا الصنف المساعد غير مطلوب لهذا الأمر.");

        if (qty > need.RemainingQty + 0.001)
        {
            // السماح بالصرف الجزئي — إذا تجاوز المتبقي نرفض إلا بصلاحية
            if (!Session.Can("materials", "OverIssue"))
                return OpResult.Fail($"الكمية المطلوب صرفها {qty:N3} تتجاوز المتبقي {need.RemainingQty:N3} — لا يسمح بالصرف الزائد إلا بصلاحية.");
        }

        return RunOp(() =>
        {
            var whAux = Db.Warehouses.FirstOrDefault(w => w.WarehouseCode == "WAUX");
            if (whAux == null) throw new DomainException("مخزن الأصناف المساعدة WAUX غير موجود — أنشئه من شاشة المخازن.");

            // التحقق من المخزون
            // §1.50.66 — مفتاح كامل: Warehouse + Product + PackagingType (null للمساعد) + Customer null + Lot null
            var balance = Db.StockBalances.FirstOrDefault(b => b.WarehouseId == whAux.Id && b.ProductId == auxiliaryProductId && b.LotId == null && b.CustomerId == null && b.PackagingTypeId == null);
            double availableD = balance != null ? (need.Unit.Contains("كجم") ? balance.QtyKg : (balance.PackageCount > 0 ? balance.PackageCount : balance.QtyKg)) : 0;
            decimal available = (decimal)availableD;

            if (available < qty - 0.001)
            {
                // رسالة تفصيلية كما طلب المستخدم
                throw new DomainException($"غير كافٍ للصرف\nالمطلوب: {qty:N3} {need.Unit}\nالمتاح: {available:N3}\nالعجز: {(qty - available):N3}");
            }

            decimal before = available;

            // إنشاء حركة مخزنية
            var txn = new InventoryTransaction
            {
                TxnDate = Db.BusinessNow,
                WarehouseId = whAux.Id,
                ProductId = auxiliaryProductId,
                QtyKg = need.Unit.Contains("كجم") ? -qty : 0,
                PackageCount = need.Unit.Contains("كجم") ? 0 : -(int)qty,
                ReferenceDocType = ReferenceDocType.MaterialIssue,
                ReferenceDocNumber = $"{order.DocumentNumber}#AUX-{auxiliaryProductId}-{DateTime.Now:yyyyMMddHHmmss}",
                Notes = notes ?? $"صرف {qty:N3} {need.Unit} من {need.AuxiliaryProductName} لأمر {order.DocumentNumber}",
                CreatedBy = Session.UserId
            };
            Db.InventoryTransactions.Add(txn);

            // تحديث الرصيد — §1.50.66 مفتاح كامل
            if (balance == null)
            {
                balance = new StockBalance { WarehouseId = whAux.Id, ProductId = auxiliaryProductId, QtyKg = 0, PackageCount = 0, LotId = null, CustomerId = null, PackagingTypeId = null };
                Db.StockBalances.Add(balance);
            }
            if (need.Unit.Contains("كجم"))
                balance.QtyKg -= qty;
            else
                balance.PackageCount -= (int)qty;

            decimal after = (decimal)(need.Unit.Contains("كجم") ? balance.QtyKg : balance.PackageCount);

            // تحديث ProductionOrderMaterial
            var mat = Db.ProductionOrderMaterials.FirstOrDefault(m => m.OrderId == orderId && m.AuxiliaryProductId == auxiliaryProductId);
            if (mat == null)
            {
                mat = new ProductionOrderMaterial
                {
                    OrderId = orderId,
                    AuxiliaryProductId = auxiliaryProductId,
                    MaterialId = 0,
                    CalculatedQty = need.RequiredQty,
                    ActualIssuedQty = qty,
                    UnitOfMeasure = need.Unit,
                    IsAutoCalculated = true,
                    Status = DocStatuses.Issued
                };
                Db.ProductionOrderMaterials.Add(mat);
            }
            else
            {
                mat.ActualIssuedQty += qty;
                mat.Status = DocStatuses.Issued;
            }

            // تسجيل حركة تتبع مفصلة
            var issueTxn = new AuxiliaryIssueTransaction
            {
                OrderId = orderId,
                OrderNumber = order.DocumentNumber,
                AuxiliaryProductId = auxiliaryProductId,
                RequiredQty = need.RequiredQty,
                IssuedQty = qty,
                Unit = need.Unit,
                IssueDate = Db.BusinessNow,
                UserId = Session.UserId,
                UserName = Session.UserName,
                DocumentNumber = txn.ReferenceDocNumber,
                WarehouseId = whAux.Id,
                BalanceBefore = before,
                BalanceAfter = after,
                Notes = notes
            };
            Db.AuxiliaryIssueTransactions.Add(issueTxn);

            Db.SaveChanges();
            return OpResult.Success($"تم صرف {qty:N3} {need.Unit} من {need.AuxiliaryProductName} لأمر {order.DocumentNumber}.\nالمتبقي: {(need.RequiredQty - mat.ActualIssuedQty):N3}", mat.Id, txn.ReferenceDocNumber);
        });
    }

    public OpResult IssueAllRemaining(int orderId)
    {
        Require("materials", "Post");
        var needs = CalculateNeedsForOrder(orderId);
        var remaining = needs.Where(n => n.RemainingQty > 0.001 && n.AuxiliaryProductId != null).ToList();
        if (remaining.Count == 0) return OpResult.Fail("لا يوجد متبقي للصرف — جميع الاحتياجات مصروفة.");

        int success = 0;
        var errors = new List<string>();
        foreach (var need in remaining)
        {
            var r = IssueAuxiliary(orderId, need.AuxiliaryProductId.Value, need.RemainingQty, "صرف جماعي للمتبقي");
            if (r.Ok) success++;
            else errors.Add($"{need.AuxiliaryProductName}: {r.Message}");
        }

        if (errors.Count > 0)
            return OpResult.Fail($"تم صرف {success} من {remaining.Count} أصناف.\nالأخطاء:\n{string.Join("\n", errors)}");

        return OpResult.Success($"تم صرف جميع المتبقي — {success} صنف مساعد.");
    }

    // ═══════════════════════════════════════════════════════════════
    // سابعًا: الفصل بين الخام والمساعد — مضمون في التصميم
    // الخام من Lot/Shipment، المساعد من WAUX بلا Lot
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    // ثامنًا: التتبع
    // ═══════════════════════════════════════════════════════════════

    public List<AuxiliaryIssueTransaction> GetIssueHistory(int orderId)
        => Db.AuxiliaryIssueTransactions.AsNoTracking().Where(t => t.OrderId == orderId).OrderByDescending(t => t.IssueDate).ToList();

    public List<AuxiliaryIssueTransaction> GetConsumptionByOrder(int orderId)
        => GetIssueHistory(orderId);

    // ═══════════════════════════════════════════════════════════════
    // تاسعًا وعاشرًا وحادي عشر: إعادة حساب عند التعديل والإرجاع
    // ═══════════════════════════════════════════════════════════════

    public OpResult RecalculateOnOrderChange(int orderId)
    {
        Require("production", "Edit");
        return RunOp(() =>
        {
            var order = Db.ProductionOrders.Include(o => o.Items).Include(o => o.Materials).FirstOrDefault(o => o.Id == orderId);
            if (order == null) throw new DomainException("الأمر غير موجود.");

            var cartonsByProduct = order.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.PlannedCartons));
            var newNeeds = new Dictionary<int, decimal>();

            foreach (var kv in cartonsByProduct)
            {
                var reqs = Db.ProductAuxiliaryRequirements.AsNoTracking().Where(r => r.FinishedProductId == kv.Key && r.IsActive).ToList();
                foreach (var r in reqs)
                {
                    decimal reqQty = (decimal)kv.Value * r.QtyPerCarton;
                    if (newNeeds.ContainsKey(r.AuxiliaryProductId))
                        newNeeds[r.AuxiliaryProductId] += reqQty;
                    else
                        newNeeds[r.AuxiliaryProductId] = reqQty;
                }
            }

            // لا نعدل حركات الصرف السابقة — فقط نحدث المطلوب
            foreach (var kv in newNeeds)
            {
                var mat = Db.ProductionOrderMaterials.FirstOrDefault(m => m.OrderId == orderId && m.AuxiliaryProductId == kv.Key);
                if (mat != null)
                {
                    mat.CalculatedQty = Math.Round(kv.Value, 3);
                    // المتبقي = الجديد - المصروف السابق
                }
                else
                {
                    var prod = Db.Products.AsNoTracking().FirstOrDefault(p => p.Id == kv.Key);
                    Db.ProductionOrderMaterials.Add(new ProductionOrderMaterial
                    {
                        OrderId = orderId,
                        AuxiliaryProductId = kv.Key,
                        MaterialId = 0,
                        CalculatedQty = Math.Round(kv.Value, 3),
                        ActualIssuedQty = 0,
                        UnitOfMeasure = prod?.UnitOfMeasure ?? "وحدة",
                        IsAutoCalculated = true,
                        Status = DocStatuses.Draft
                    });
                }
            }

            Db.SaveChanges();
            return OpResult.Success("تم إعادة حساب احتياجات الأصناف المساعدة حسب الكمية الجديدة — الصرف السابق محفوظ، والمتبقي هو (الجديد - المصروف السابق).");
        });
    }

    public OpResult ReturnAuxiliary(int orderId, int auxiliaryProductId, decimal qty, string reason)
    {
        Require("materials", "Post");
        if (qty <= 0) return OpResult.Fail("كمية الإرجاع يجب أن تكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(reason)) return OpResult.Fail("سبب الإرجاع إجباري للتدقيق.");

        return RunOp(() =>
        {
            var mat = Db.ProductionOrderMaterials.FirstOrDefault(m => m.OrderId == orderId && m.AuxiliaryProductId == auxiliaryProductId);
            if (mat == null) throw new DomainException("هذا الصنف غير مصروف لهذا الأمر.");
            decimal unused = (decimal)(mat.ActualIssuedQty - mat.ConsumedQty - mat.WastedQty - mat.ReturnedQty);
            if (qty > unused + 0.001) throw new DomainException($"كمية الإرجاع {qty:N3} تتجاوز الفائض غير المستخدم {unused:N3}.");

            var whAux = Db.Warehouses.FirstOrDefault(w => w.WarehouseCode == "WAUX");
            if (whAux == null) throw new DomainException("مخزن WAUX غير موجود.");

            var balance = Db.StockBalances.FirstOrDefault(b => b.WarehouseId == whAux.Id && b.ProductId == auxiliaryProductId && b.LotId == null && b.CustomerId == null && b.PackagingTypeId == null);
            decimal before = (decimal)(balance != null ? (balance.QtyKg + balance.PackageCount) : 0);

            var txn = new InventoryTransaction
            {
                TxnDate = Db.BusinessNow,
                WarehouseId = whAux.Id,
                ProductId = auxiliaryProductId,
                QtyKg = mat.UnitOfMeasure.Contains("كجم") ? qty : 0,
                PackageCount = mat.UnitOfMeasure.Contains("كجم") ? 0 : (int)qty,
                ReferenceDocType = ReferenceDocType.Return,
                ReferenceDocNumber = $"{Db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.DocumentNumber).FirstOrDefault()}#RET-{auxiliaryProductId}-{DateTime.Now:yyyyMMddHHmmss}",
                Notes = $"إرجاع {qty:N3} {mat.UnitOfMeasure} — السبب: {reason}",
                CreatedBy = Session.UserId
            };
            Db.InventoryTransactions.Add(txn);

            if (balance == null)
            {
                balance = new StockBalance { WarehouseId = whAux.Id, ProductId = auxiliaryProductId, QtyKg = 0, PackageCount = 0, LotId = null, CustomerId = null, PackagingTypeId = null };
                Db.StockBalances.Add(balance);
            }
            if (mat.UnitOfMeasure.Contains("كجم"))
                balance.QtyKg += qty;
            else
                balance.PackageCount += (int)qty;

            decimal after = (decimal)(balance.QtyKg + balance.PackageCount);

            mat.ReturnedQty += qty;

            var retTxn = new AuxiliaryReturnTransaction
            {
                OrderId = orderId,
                OrderNumber = Db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.DocumentNumber).FirstOrDefault(),
                AuxiliaryProductId = auxiliaryProductId,
                ReturnedQty = qty,
                Unit = mat.UnitOfMeasure,
                ReturnDate = Db.BusinessNow,
                UserId = Session.UserId,
                UserName = Session.UserName,
                DocumentNumber = txn.ReferenceDocNumber,
                WarehouseId = whAux.Id,
                BalanceBefore = before,
                BalanceAfter = after,
                Reason = reason
            };
            Db.AuxiliaryReturnTransactions.Add(retTxn);

            Db.SaveChanges();
            return OpResult.Success($"تم إرجاع {qty:N3} {mat.UnitOfMeasure} إلى المخزن.", retTxn.Id, txn.ReferenceDocNumber);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // ثاني عشر: التقارير
    // ═══════════════════════════════════════════════════════════════

    public List<AuxiliaryConsumptionReportRow> GetConsumptionByOrderReport(int orderId)
    {
        var order = Db.ProductionOrders.AsNoTracking().FirstOrDefault(o => o.Id == orderId);
        if (order == null) return new List<AuxiliaryConsumptionReportRow>();

        var needs = CalculateNeedsForOrder(orderId);
        return needs.Select(n => new AuxiliaryConsumptionReportRow
        {
            OrderId = orderId,
            OrderNumber = order.DocumentNumber,
            AuxiliaryProductName = n.AuxiliaryProductName,
            Unit = n.Unit,
            RequiredQty = n.RequiredQty,
            IssuedQty = n.IssuedQty,
            RemainingQty = n.RemainingQty,
            CalculationDetails = n.CalculationDetails
        }).ToList();
    }

    public List<AuxiliaryPeriodConsumptionRow> GetPeriodConsumption(DateTime from, DateTime to, int? auxiliaryProductId = null)
    {
        var q = Db.AuxiliaryIssueTransactions.AsNoTracking().Where(t => t.IssueDate >= from && t.IssueDate <= to);
        if (auxiliaryProductId != null)
            q = q.Where(t => t.AuxiliaryProductId == auxiliaryProductId);

        return q.GroupBy(t => t.AuxiliaryProductId)
            .Select(g => new AuxiliaryPeriodConsumptionRow
            {
                AuxiliaryProductId = g.Key,
                AuxiliaryProductName = Db.Products.Where(p => p.Id == g.Key).Select(p => p.ProductNameAr).FirstOrDefault() ?? $"صنف #{g.Key}",
                TotalIssued = g.Sum(x => x.IssuedQty),
                Unit = g.Select(x => x.Unit).FirstOrDefault(),
                TransactionsCount = g.Count()
            }).ToList();
    }
}

    // ═══════════════════════════════════════════════════════════════
    // §1.50.64 — كراتين العملاء (ماركات)
    // ═══════════════════════════════════════════════════════════════

    public List<AuxCustomerSpec> GetCustomerCartonMappings()
        => Db.AuxCustomerSpecs.AsNoTracking().OrderByDescending(s => s.Priority).ToList();

    public OpResult SaveCustomerCartonMapping(int customerId, int? finishedProductId, int? packagingTypeId, int? genericAuxProductId, int customerCartonProductId, string brandName, int priority)
    {
        Require("products", "Edit");
        if (customerId <= 0) return OpResult.Fail("اختر العميل.");
        if (customerCartonProductId <= 0) return OpResult.Fail("اختر كرتون العميل.");
        return RunOp(() =>
        {
            var exists = Db.AuxCustomerSpecs.Any(x => x.CustomerId == customerId && x.ProductId == finishedProductId
                && x.PackagingTypeId == packagingTypeId && x.GenericAuxiliaryProductId == genericAuxProductId
                && x.AuxiliaryProductId == customerCartonProductId && x.IsActive);
            if (exists) throw new DomainException("هذا الربط موجود مسبقاً — نفس العميل والصنف والعبوة والكرتون.");
            var spec = new AuxCustomerSpec
            {
                CustomerId = customerId,
                ProductId = finishedProductId,
                PackagingTypeId = packagingTypeId,
                MaterialId = 0,
                GenericAuxiliaryProductId = genericAuxProductId,
                AuxiliaryProductId = customerCartonProductId,
                BrandName = string.IsNullOrWhiteSpace(brandName) ? null : brandName.Trim(),
                Priority = priority,
                IsActive = true
            };
            Db.AuxCustomerSpecs.Add(spec);
            Db.SaveChanges();
            return OpResult.Success("تم ربط كرتون العميل — عند إنتاج للعميل سيخصم تلقائياً من ماركته الخاصة.", spec.Id);
        });
    }

    public OpResult DeleteCustomerCartonMapping(int id)
    {
        Require("products", "Delete");
        return RunOp(() =>
        {
            var spec = Db.AuxCustomerSpecs.FirstOrDefault(s => s.Id == id);
            if (spec == null) throw new DomainException("الربط غير موجود.");
            spec.IsActive = false;
            Db.SaveChanges();
            return OpResult.Success("تم إيقاف الربط — لن يُستخدم في الأوامر الجديدة.");
        });
    }

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class AuxiliarySetupDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public string GroupCode { get; set; }
    public string BaseUnit { get; set; }
    public decimal UnitWeightKg { get; set; }
    public string DispensingMethod { get; set; }
    public string DispensingMethodAr { get; set; }
    public bool NeedsIssueOnOrder { get; set; }
    public bool IsActive { get; set; }
    public bool IsConfigured { get; set; }
    public string Notes { get; set; }
}

public class ProductAuxiliaryRequirementDto
{
    public int Id { get; set; }
    public int FinishedProductId { get; set; }
    public int AuxiliaryProductId { get; set; }
    public string AuxiliaryProductName { get; set; }
    public string Unit { get; set; }
    public decimal QtyPerCarton { get; set; }
    public string CalculationMethod { get; set; }
    public string CalculationMethodAr { get; set; }
    public bool IsActive { get; set; }
    public string Notes { get; set; }
}

public class AuxiliaryNeedDto
{
    public int? AuxiliaryProductId { get; set; }
    public int? MaterialId { get; set; }
    public string AuxiliaryProductName { get; set; }
    public string Unit { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal IssuedQty { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string StockStatus { get; set; }
    public string StockStatusAr { get; set; }
    public bool NeedsIssue { get; set; } = true;
    public string CalculationDetails { get; set; }
    public bool IsLegacy { get; set; }
    public int? GenericAuxiliaryProductId { get; set; }
}

public class AuxiliaryConsumptionReportRow
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public string AuxiliaryProductName { get; set; }
    public string Unit { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal IssuedQty { get; set; }
    public decimal RemainingQty { get; set; }
    public string CalculationDetails { get; set; }
}

public class AuxiliaryPeriodConsumptionRow
{
    public int? AuxiliaryProductId { get; set; }
    public string AuxiliaryProductName { get; set; }
    public decimal TotalIssued { get; set; }
    public string Unit { get; set; }
    public int TransactionsCount { get; set; }
}
