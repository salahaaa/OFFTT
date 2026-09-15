using System.Windows.Media;
using DatesErp.Core.Interfaces.Services;
using DatesErp.Desktop.Services;
namespace DatesErp.Desktop.Views.Screens;
public partial class PlanningView
{
    private bool _capacityValid, _updatingCapacity;
    internal static PlanItemDto CapacityItem(PlanRowUi row) => new()
    {
        ProductId = row.ProductId, PackagingTypeId = row.PackId, LotId = row.LotId, SelectedRawProductId = row.RawProductId, PlannedCartons = row.Cartons, PlannedQtyKg = row.QtyKg,
        ScheduledDate = row.Date, SuggestedShiftId = row.ShiftId, SuggestedLineId = row.LineId
    };
    private PlanCapacityResult EvaluateDraft(List<PlanItemDto> items)
    {
        using var scope = AppContainer.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlanningService>();
        return service.EvaluateCapacity(items, _currentPlanId > 0 ? _currentPlanId : null,
            StartBox.SelectedDate?.ToString("dd/MM/yyyy"), EndBox.SelectedDate?.ToString("dd/MM/yyyy"), SelectedShiftId(), SelectedLineId());
    }
    private string CheckRowQuantity(PlanRowUi row, int quantity)
    {
        try
        {
            if (quantity == 0) return null;
            var candidate = CapacityItem(row); candidate.PlannedCartons = quantity; candidate.PlannedQtyKg = quantity * row.CartonWeight;
            // §1.50.67 FIX: استثناء الصفوف الفارغة placeholder من فحص الطاقة
            var validOthers = _rows.Where(r => r != row && (r.LotId != null || r.ProductId != 0)).Select(CapacityItem).ToList();
            validOthers.Add(candidate);
            var check = EvaluateDraft(validOthers);
            return check.Rows.LastOrDefault()?.Error ?? check.Error;
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Planning.QuantityGuard"); return "تعذر التحقق من الطاقة؛ لم تُقبل الكمية."; }
    }
    private void UpdateCapacityBar()
    {
        // §شرائح السياق (تاريخ الإنتاج المحدد/الفترة) تُحدَّث هنا لأن هذا المسار يُنادى عند
        // كل تغيير للتواريخ أو الوردية أو البنود أو فتح خطة محفوظة — نقطة تحديث واحدة.
        UpdateDateChips();
        if (_updatingCapacity || CapacitySummary == null) return;
        _updatingCapacity = true;
        try
        {
            // §1.50.67 FIX: زر الحفظ يتشفر بعد تعديل الأصناف دون حفظ — السبب صف فارغ placeholder
            // كان _rows.Any يفحص حتى الصف الفارغ (0 كرتون) فيعطي inputError ويعطل الحفظ دائماً.
            // الآن نفحص فقط البنود الفعلية (LotId != null || ProductId != 0) مثل Save_Click.
            var validRows = _rows.Where(r => r.LotId != null || r.ProductId != 0).ToList();
            var result = EvaluateDraft(validRows.Select(CapacityItem).ToList());
            string inputError = validRows.FirstOrDefault(r => r.QuantityError != null)?.QuantityError;
            if (validRows.Any(r => !int.TryParse(r.CartonsText, out var n) || n <= 0)) inputError ??= "صحّح الكميات: يجب أن تكون أعداداً صحيحة أكبر من صفر.";
            _capacityValid = result.IsValid && inputError == null && validRows.Count > 0;
            // §التخطيط الأفقي: شريط التقدّم يترجم الطاقة إلى مؤشر بصري فوري (أخضر ضمن الطاقة، أحمر عند التجاوز)
            double usedHours = result.Slots.Sum(s => s.UsedHours);
            double totalHours = result.Slots.Sum(s => s.TotalHours);
            if (CapacityProgress != null)
            {
                CapacityProgress.Maximum = totalHours > 0 ? totalHours : 1;
                CapacityProgress.Value = Math.Min(usedHours, CapacityProgress.Maximum);
                CapacityProgress.Foreground = _capacityValid ? Brushes.SeaGreen : Brushes.IndianRed;
                CapacityProgress.ToolTip = totalHours > 0
                    ? $"المجدول {usedHours:N3} من أصل {totalHours:N3} ساعة متاحة"
                    : "لا طاقة معرّفة للوردية المختارة";
            }
            // §v1.50.35: سطر واحد مضغوط — تفاصيل الأيام في التلميح لا تغطي الشاشة في الفترة الأسبوعية.
            int overloaded = result.Slots.Count(s => s.TotalHours > 0 && s.UsedHours > s.TotalHours + 0.001);
            CapacitySummary.Text = result.Summary
                + (result.Slots.Count > 1 ? $"  ·  {result.Slots.Count} يوماً" : "")
                + (overloaded > 0 ? $"  ·  ⚠ تجاوز في {overloaded} يوماً" : "");
            CapacitySummary.ToolTip = result.Slots.Count == 0 ? result.Summary
                : string.Join("\n", result.Slots.Select(s => $"{s.Label}: مستخدم {s.UsedHours:N1} / {s.TotalHours:N1} س"));
            RemainingBadge.Text = inputError ?? result.Error ?? "ضمن الطاقة — الحساب تراكمي لجميع الأصناف";
            RemainingBadge.Foreground = _capacityValid ? Brushes.DarkGreen : Brushes.Firebrick;
            // §1.50.67 FIX: تعيين الطاقة فقط للبنود الفعلية — كان يعين حسب index في _rows فيخطئ مع placeholder
            for (int i = 0; i < validRows.Count && i < result.Rows.Count; i++) validRows[i].Capacity = result.Rows[i];
            // مسح طاقة الصفوف الفارغة
            foreach (var empty in _rows.Where(r => r.LotId == null && r.ProductId == 0)) empty.Capacity = null;
        }
        catch (Exception ex) { ErrorLog.Write(ex, "Planning.CapacityBar"); _capacityValid = false; RemainingBadge.Text = "تعذر التحقق من الطاقة؛ الحفظ موقوف."; }
        finally
        {
            _updatingCapacity = false;
            if (SaveActionBtn != null) SaveActionBtn.IsEnabled = !_locked && _capacityValid;
            if (_toolbar?.SaveBtn != null) _toolbar.SaveBtn.IsEnabled = !_locked && _capacityValid;
        }
    }
}
