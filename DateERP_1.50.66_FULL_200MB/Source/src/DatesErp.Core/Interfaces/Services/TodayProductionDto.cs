namespace DatesErp.Core.Interfaces.Services;

/// <summary>The approved day sheet is a read model, not an order-entry form.</summary>
public sealed class TodayProductionDto
{
    public DateTime Day { get; init; }
    public string Message { get; init; }
    public IReadOnlyList<TodayProductionRowDto> Rows { get; init; } = Array.Empty<TodayProductionRowDto>();
    public bool CanIssue { get; init; }
}

public sealed class TodayProductionRowDto : System.ComponentModel.INotifyPropertyChanged
{
    public int PlanId { get; init; }
    public int PlanItemId { get; init; }
    public string PlanNumber { get; init; }
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; }
    public int PlannedCartons { get; init; }
    public double PlannedKg { get; init; }
    public string Unit { get; init; } = "كرتون";
    public int? ShiftId { get; init; }
    public string ShiftName { get; init; }
    public int? LineId { get; init; }
    public string LineName { get; init; }
    public int? OrderId { get; init; }
    public string OrderNumber { get; init; }
    public string Status { get; init; }
    public bool IsPending { get; init; }
    /// <summary>§v1.50.24: يوم هذا الأمر أُقفل (سُجل فعليه) — يغادر قائمة «أمر إنتاج اليوم».</summary>
    public bool DayClosed { get; init; }
    // §1.50.61 — إصدار جماعي: اختيار متعدد + تعديل مباشر
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); } }
    private int _editableCartons;
    public int EditableCartons { get => _editableCartons; set { _editableCartons = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(EditableCartons))); } }
    public bool IsInvalid => IsPending && EditableCartons <= 0;
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}

/// <summary>§v1.50.34 — مجموعة يوم معتمدة بلا أمر بعد (زر «إضافة أمر من الخطة»).</summary>
public class TodayPendingGroupDto
{
    public int PlanId { get; init; }
    public string PlanNumber { get; init; }
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; }
    public int? ShiftId { get; init; }
    public string ShiftName { get; init; }
    public int? LineId { get; init; }
    public string LineName { get; init; }
    public int ItemsCount { get; init; }
    public int Cartons { get; init; }
}

/// <summary>§v1.50.34 — صف نتيجة بحث أوامر الإنتاج.</summary>
public class OrderSearchRowDto
{
    public int OrderId { get; init; }
    public string DocumentNumber { get; init; }
    public string ProductionDate { get; init; }
    public string CustomerName { get; init; }
    public string StatusAr { get; init; }
}
