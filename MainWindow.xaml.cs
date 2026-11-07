using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QueueAnalyzer;

/// <summary>Строка таблицы характеристик.</summary>
public sealed record ResultView(string Name, string Symbol, string Formula, string ValueText);

/// <summary>Строка диаграммы вероятностей состояний.</summary>
public sealed record StateView(string Label, string ValueText, GridLength Filled, GridLength Rest);

public partial class MainWindow : Window
{
    // Единицы времени: подпись и длительность в секундах
    private static readonly (string Name, double Seconds)[] Units =
    {
        ("сек", 1), ("мин", 60), ("ч", 3600), ("сут", 86400),
    };

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private QsKind _kind = QsKind.SingleLoss;
    private readonly RadioButton[] _typeButtons;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        _typeButtons = new[] { Type0, Type1, Type2, Type3, Type4, Type5 };
        for (int i = 0; i < _typeButtons.Length; i++)
        {
            var kind = (QsKind)i;
            _typeButtons[i].Checked += (_, _) => SelectType(kind);
        }

        foreach (var u in Units)
        {
            LambdaUnit.Items.Add($"заявок / {u.Name}");
            OutputUnit.Items.Add(u.Name);
        }
        LambdaUnit.SelectedIndex = 1;
        OutputUnit.SelectedIndex = 1;
        FillServiceUnits(1);

        ByMu.Checked += (_, _) => OnServiceModeChanged();
        ByTime.Checked += (_, _) => OnServiceModeChanged();

        _ready = true;
        SelectType(QsKind.SingleLoss);
    }

    // ---------------- 1. Выбор типа СМО ----------------

    private void OnMenuType(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int idx))
            _typeButtons[idx].IsChecked = true;   // обработчик Checked вызовет SelectType
    }

    private void SelectType(QsKind kind)
    {
        if (!_ready) return;
        _kind = kind;
        TypeTitle.Text = $"{(int)kind + 1}. {QsInfo.Title(kind)}";
        TypeDescription.Text = QsInfo.Description(kind);

        bool multi = QsInfo.IsMulti(kind);
        bool limited = QsInfo.HasLimitedQueue(kind);
        ChannelsPanel.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        QueuePanel.Visibility = limited ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = multi || limited ? Visibility.Visible : Visibility.Collapsed;
        HideResults();
    }

    // ---------------- 2. Поток обслуживания: μ или tоб ----------------

    private bool ServiceByIntensity => ByMu.IsChecked == true;

    private void FillServiceUnits(int selected)
    {
        ServiceUnit.Items.Clear();
        foreach (var u in Units)
            ServiceUnit.Items.Add(ServiceByIntensity ? $"заявок / {u.Name}" : u.Name);
        ServiceUnit.SelectedIndex = selected < 0 ? 1 : selected;
    }

    private void OnServiceModeChanged()
    {
        if (!_ready) return;
        ServiceLabel.Text = ServiceByIntensity ? "Интенсивность μ" : "Среднее время обслуживания tоб";
        // Пересчитываем введённое значение, чтобы смысл сохранился: tоб = 1/μ
        if (TryParse(ServiceBox.Text, out double v) && v > 0)
            ServiceBox.Text = Format(1 / v);
        FillServiceUnits(ServiceUnit.SelectedIndex);
        HideResults();
    }

    // ---------------- 3. Расчёт ----------------

    private void OnCalculate(object sender, RoutedEventArgs e) => Calculate();

    private void OnCalculateCommand(object sender, ExecutedRoutedEventArgs e) => Calculate();

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
                : "Время обслуживания tоб должно быть положительным числом.");
            return;
        }

        int n = 1, m = 0;
        if (QsInfo.IsMulti(_kind) && (!int.TryParse(ChannelsBox.Text.Trim(), out n) || n < 1 || n > 200))
        {
            ShowError("Число каналов n должно быть целым числом от 1 до 200.");
            return;
        }
        if (QsInfo.HasLimitedQueue(_kind) && (!int.TryParse(QueueBox.Text.Trim(), out m) || m < 1 || m > 1000))
        {
            ShowError("Число мест в очереди m должно быть целым числом от 1 до 1000.");
            return;
        }

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
        ResultsGrid.ItemsSource = result.Rows.Select(r =>
        {
            string suffix = r.Kind switch
            {
                ValueKind.Time => $" {unit}",
                ValueKind.Rate => $" 1/{unit}",
                _ => ""
            };
            return new ResultView(r.Name, r.Symbol, r.Formula, Format(r.Value) + suffix);
        }).ToList();

        double max = result.States.Count > 0 ? result.States.Max(s => s.P) : 1;
        if (max <= 0) max = 1;
        StatesList.ItemsSource = result.States.Select(s =>
        {
            double share = s.P / max;
            return new StateView(s.State, Format(s.P),
                new GridLength(share, GridUnitType.Star),
                new GridLength(1 - share, GridUnitType.Star));
        }).ToList();

        double sum = result.States.Sum(s => s.P);
        StatesNote.Text = result.StatesTruncated
            ? $"Pₖ — вероятность того, что в системе k заявок. Показаны первые {result.States.Count} состояний, их сумма: {Format(sum)}."
            : $"Pₖ — вероятность того, что в системе k заявок. Проверка: Σ Pₖ = {Format(sum)}.";

        Placeholder.Visibility = Visibility.Collapsed;
        ResultTabs.Visibility = Visibility.Visible;
    }

    // ---------------- Служебное ----------------

    private void OnReset(object sender, RoutedEventArgs e)
    {
        LambdaBox.Text = "";
        ServiceBox.Text = "";
        ChannelsBox.Text = "2";
        QueueBox.Text = "3";
        HideResults();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideResults()
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
        ResultTabs.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
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
