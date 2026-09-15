using System.Windows;
using System.Windows.Controls;
using DatesErp.Application.Services;
using DatesErp.Desktop.Services;
using DatesErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DatesErp.Desktop.Views.Screens;

public partial class ProductBOMView : UserControl
{
    public ProductBOMView()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    public void AttachChrome(Views.ErpChrome chrome)
    {
        chrome.SetModule("مكونات الإنتاج / احتياجات الصنف التام");
        chrome.SetScreenCode("MRPAUX1002");
        chrome.SetToolbar(new Views.ErpToolbar()
            .WithSave((_, _) => Add_Click(null, null), "إضافة احتياج")
            .WithRefresh((_, _) => Load())
            .WithExit((_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard")));
        chrome.SetBody(this);
        chrome.CloseRequested += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenScreen("dashboard");
    }

    private void Load()
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var db = scope.ServiceProvider.GetRequiredService<DatesErpDbContext>();
            var svc = scope.ServiceProvider.GetRequiredService<AuxiliaryManagementService>();

            FinishedBox.ItemsSource = svc.GetFinishedProducts();
            AuxBox.ItemsSource = svc.GetAuxiliaryProducts();
            UnitBox.ItemsSource = db.UnitsOfMeasure.AsNoTracking().Where(u => u.IsActive).Select(u => u.UnitNameAr).ToList();

            if (FinishedBox.SelectedValue is int fid)
                RefreshBOM(fid);
            else
                BOMGrid.ItemsSource = null;

            CalcHint.Text = "اختر صنفاً تاماً لعرض احتياجاته — مثال: سكري 8 كجم يحتاج كرتون 8 كجم 1 وحدة/كرتون، ملصق 1، كيس 1، مادة X 0.020 كجم.";
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "BOM.Load"); }
    }

    private void Finished_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FinishedBox.SelectedValue is int fid)
            RefreshBOM(fid);
    }

    private void RefreshBOM(int finishedId)
    {
        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<AuxiliaryManagementService>();
            var list = svc.GetRequirementsForFinished(finishedId);
            // نحتاج اسم الصنف التام للعرض
            var finishedName = (FinishedBox.SelectedItem as DatesErp.Core.Domain.Entities.Product)?.ProductNameAr ?? "";
            var display = list.Select(r => new
            {
                r.Id,
                FinishedProductId = r.FinishedProductId,
                FinishedProductName = finishedName,
                r.AuxiliaryProductId,
                r.AuxiliaryProductName,
                r.Unit,
                r.QtyPerCarton,
                r.CalculationMethod,
                r.CalculationMethodAr
            }).ToList();
            BOMGrid.ItemsSource = display;
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "BOM.Refresh"); }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (FinishedBox.SelectedValue is not int fid)
        {
            AppContainer.Get<DialogService>().Error("اختر الصنف التام أولاً.");
            return;
        }
        if (AuxBox.SelectedValue is not int aid)
        {
            AppContainer.Get<DialogService>().Error("اختر الصنف المساعد.");
            return;
        }
        if (!double.TryParse(QtyBox.Text, out var qty) || qty <= 0)
        {
            AppContainer.Get<DialogService>().Error("أدخل كمية صحيحة لكل كرتون.");
            return;
        }
        string calc = (CalcBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "PerCarton";

        try
        {
            using var scope = AppContainer.NewScope();
            var svc = scope.ServiceProvider.GetRequiredService<AuxiliaryManagementService>();
            var r = svc.SaveRequirement(null, fid, aid, UnitBox.Text, qty, calc);
            if (!r.Ok) AppContainer.Get<DialogService>().Error(r.Message);
            else
            {
                AppContainer.Get<DialogService>().Info(r.Message);
                RefreshBOM(fid);
                QtyBox.Text = "1";
            }
        }
        catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "BOM.Add"); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag != null)
        {
            dynamic row = b.Tag;
            int id = (int)row.Id;
            if (!AppContainer.Get<DialogService>().Confirm("حذف هذا الاحتياج؟ لن يُحتسب في الأوامر الجديدة.")) return;
            try
            {
                using var scope = AppContainer.NewScope();
                var svc = scope.ServiceProvider.GetRequiredService<AuxiliaryManagementService>();
                var r = svc.DeleteRequirement(id);
                if (!r.Ok) AppContainer.Get<DialogService>().Error(r.Message);
                else
                {
                    AppContainer.Get<DialogService>().Info(r.Message);
                    if (FinishedBox.SelectedValue is int fid) RefreshBOM(fid);
                }
            }
            catch (Exception ex) { AppContainer.Get<DialogService>().HandleException(ex, "BOM.Delete"); }
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
}
