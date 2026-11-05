using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace QueueAnalyzer;

public partial class MainWindow : Window
{
    // Единицы времени: подпись и длительность в секундах
    private static readonly (string Name, double Seconds)[] Units =
    {
        ("сек", 1), ("мин", 60), ("ч", 3600), ("сут", 86400),
    };

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private QsKind _kind = QsKind.SingleLoss;
    private RadioButton[] _typeButtons = Array.Empty<RadioButton>();

    public MainWindow()
    {
        InitializeComponent();

        _typeButtons = new[] { Type0, Type1, Type2, Type3, Type4, Type5 };
        for (int i = 0; i < _typeButtons.Length; i++)
        {
            int idx = i;
            _typeButtons[i].IsCheckedChanged += (_, _) =>
            {
                if (_typeButtons[idx].IsChecked == true) SelectType((QsKind)idx);
            };
        }

        foreach (var u in Units)
        {
            LambdaUnit.Items.Add($"заявок / {u.Name}");
            OutputUnit.Items.Add(u.Name);
        }
        LambdaUnit.SelectedIndex = 1;
        OutputUnit.SelectedIndex = 1;
        FillServiceUnits();
        ServiceUnit.SelectedIndex = 1;

        ByMu.IsCheckedChanged += (_, _) => OnServiceModeChanged();

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.F5 || e.Key == Avalonia.Input.Key.Enter) Calculate();
        };

        SelectType(QsKind.SingleLoss);
    }

    // ---------------- Выбор типа ----------------

    private void OnMenuType(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int idx))
            _typeButtons[idx].IsChecked = true;   // вызовет SelectType через обработчик
    }

    private void SelectType(QsKind kind)
    {
        _kind = kind;
        TypeTitle.Text = $"{(int)kind + 1}. {QsInfo.Title(kind)}";
        TypeDescription.Text = QsInfo.Description(kind);
        ChannelsPanel.IsVisible = QsInfo.IsMulti(kind);
        QueuePanel.IsVisible = QsInfo.HasLimitedQueue(kind);
        HideResults();
    }

    // ---------------- Поток обслуживания: μ или t̄об ----------------

    private bool ServiceByIntensity => ByMu.IsChecked == true;

    private void FillServiceUnits()
    {
        int sel = ServiceUnit.SelectedIndex;
        ServiceUnit.Items.Clear();
        foreach (var u in Units)
            ServiceUnit.Items.Add(ServiceByIntensity ? $"заявок / {u.Name}" : u.Name);
        ServiceUnit.SelectedIndex = sel < 0 ? 1 : sel;
    }

    private void OnServiceModeChanged()
    {
        ServiceLabel.Text = ServiceByIntensity ? "Интенсивность μ" : "Время t̄об";
        // Пересчитываем введённое значение, чтобы смысл сохранился: t̄об = 1/μ
        if (TryParse(ServiceBox.Text, out double v) && v > 0)
            ServiceBox.Text = Format(1 / v);
        FillServiceUnits();
        HideResults();
    }

    // ---------------- Расчёт ----------------

    private void OnCalculate(object? sender, RoutedEventArgs e) => Calculate();

    private void Calculate()
    {
        HideResults();

        if (!TryParse(LambdaBox.Text, out double lambdaIn) || lambdaIn <= 0)
        {
            ShowError("Интенсивность входного потока λ должна быть положительным числом.");
            return;
        }
        if (!TryParse(ServiceBox.Text, out double svcIn) || svcIn <= 0)
        {
            ShowError(ServiceByIntensity
                ? "Интенсивность обслуживания μ должна быть положительным числом."
                : "Время обслуживания t̄об должно быть положительным числом.");
            return;
        }

        int n = (int)(ChannelsBox.Value ?? 1);
        int m = (int)(QueueBox.Value ?? 1);
        if (QsInfo.IsMulti(_kind) && n < 1) { ShowError("Число каналов n должно быть ≥ 1."); return; }
        if (QsInfo.HasLimitedQueue(_kind) && m < 1) { ShowError("Число мест в очереди m должно быть ≥ 1."); return; }

        // Всё приводим к выбранной единице времени результата
        double outSec = Units[OutputUnit.SelectedIndex].Seconds;
        double lambda = lambdaIn / Units[LambdaUnit.SelectedIndex].Seconds * outSec;
        double svcSec = Units[ServiceUnit.SelectedIndex].Seconds;
        double mu = ServiceByIntensity
            ? svcIn / svcSec * outSec
            : 1 / (svcIn * svcSec / outSec);

        var result = QueueCalculator.Calculate(_kind, lambda, mu, n, m);
        if (result.Error != null)
        {
            ShowError(result.Error);
            return;
        }
        ShowResults(result, Units[OutputUnit.SelectedIndex].Name);
    }

    private void ShowResults(QsResult result, string unit)
    {
        var g = ResultsGrid;
        g.Children.Clear();
        g.RowDefinitions.Clear();

        AddRow(g, 0, true, "Характеристика", "Обозн.", "Формула", "Значение");
        int row = 1;
        foreach (var r in result.Rows)
        {
            string suffix = r.Kind switch
            {
                ValueKind.Time => $" {unit}",
                ValueKind.Rate => $" 1/{unit}",
                _ => ""
            };
            AddRow(g, row++, false, r.Name, r.Symbol, r.Formula, Format(r.Value) + suffix);
        }
        ResultsCard.IsVisible = true;

        StatesPanel.Children.Clear();
        double sum = 0;
        foreach (var (state, p) in result.States)
        {
            sum += p;
            StatesPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#EEF2FB")),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5),
                Child = new TextBlock
                {
                    Text = $"{state} = {Format(p)}",
                    FontFamily = new FontFamily("Menlo, Consolas, monospace"),
                    FontSize = 13,
                },
            });
        }
        StatesNote.Text = result.StatesTruncated
            ? $"Показаны первые {result.States.Count} состояний (Pₖ — вероятность, что в системе k заявок). Сумма показанных: {Format(sum)}."
            : $"Pₖ — вероятность того, что в системе k заявок. Проверка: Σ Pₖ = {Format(sum)}.";
        StatesCard.IsVisible = true;
    }

    private static void AddRow(Grid g, int row, bool header, string name, string sym, string formula, string value)
    {
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var bg = header ? Color.Parse("#F0F1F4") : (row % 2 == 0 ? Color.Parse("#FAFAFB") : Colors.White);
        var back = new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(header ? 6 : 0) };
        Grid.SetRow(back, row);
        Grid.SetColumnSpan(back, 4);
        g.Children.Add(back);

        string[] cells = { name, sym, formula, value };
        for (int c = 0; c < 4; c++)
        {
            var tb = new TextBlock
            {
                Text = cells[c],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 7),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = header || c == 3 ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = new SolidColorBrush(header ? Color.Parse("#555") : (c == 2 ? Color.Parse("#555") : Color.Parse("#1D1F24"))),
            };
            if (!header && c == 2) tb.FontFamily = new FontFamily("Menlo, Consolas, monospace");
            if (!header && c == 2) tb.FontSize = 12.5;
            if (c == 3) tb.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, c);
            g.Children.Add(tb);
        }
    }

    // ---------------- Служебное ----------------

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        LambdaBox.Text = "";
        ServiceBox.Text = "";
        ChannelsBox.Value = 2;
        QueueBox.Value = 3;
        HideResults();
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorBorder.IsVisible = true;
    }

    private void HideResults()
    {
        ErrorBorder.IsVisible = false;
        ResultsCard.IsVisible = false;
        StatesCard.IsVisible = false;
    }

    private static bool TryParse(string? s, out double v)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static string Format(double v)
    {
        if (v == 0) return "0";
        double a = Math.Abs(v);
        if (a >= 1e-4 && a < 1e7) return v.ToString("0.####", Ru);
        return v.ToString("0.###E+0", Ru);
    }
}
