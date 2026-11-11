using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProjectPilot.ViewModels;

namespace ProjectPilot.Views;

public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
        Chart.ExtentChanged += (_, _) => UpdateScrollBars();
        Chart.RowMoved += (_, e) => Vm?.MoveRow(e);
        Chart.RowOpened += (_, e) => Vm?.OpenRow(e);
        Chart.PreviewMouseWheel += OnChartWheel;
        Loaded += (_, _) => UpdateScrollBars();
    }

    private TimelineViewModel? Vm => DataContext as TimelineViewModel;

    private void UpdateScrollBars()
    {
        HScroll.ViewportSize = Math.Max(1, Chart.BodyWidth);
        HScroll.LargeChange = Math.Max(40, Chart.BodyWidth * 0.8);
        HScroll.Maximum = Math.Max(0, Chart.ContentWidth - Chart.BodyWidth);

        VScroll.ViewportSize = Math.Max(1, Chart.BodyHeight);
        VScroll.LargeChange = Math.Max(32, Chart.BodyHeight * 0.8);
        VScroll.Maximum = Math.Max(0, Chart.ContentHeight - Chart.BodyHeight);
    }

    private void OnChartWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Vm != null)
        {
            // Ctrl + wheel: zoom in / out
            int i = Vm.ZoomOptions.IndexOf(Vm.Zoom);
            i = e.Delta > 0 ? Math.Max(0, i - 1) : Math.Min(Vm.ZoomOptions.Count - 1, i + 1);
            Vm.Zoom = Vm.ZoomOptions[i];
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            HScroll.Value -= e.Delta;
        else
            VScroll.Value -= e.Delta / 2.0;
        e.Handled = true;
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateScrollBars();
        HScroll.Value = Math.Max(0, Chart.ContentX(DateTime.Today) - Chart.BodyWidth / 3);
    }
}
