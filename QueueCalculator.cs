using System;
using System.Collections.Generic;

namespace QueueAnalyzer;

/// <summary>Шесть типов СМО (марковские, простейший поток, показательное время обслуживания).</summary>
public enum QsKind
{
    SingleLoss,      // 1 канал, с отказами                (M/M/1/0)
    SingleLimited,   // 1 канал, ограниченная очередь m    (M/M/1/m)
    SingleInfinite,  // 1 канал, неограниченная очередь    (M/M/1/∞)
    MultiLoss,       // n каналов, с отказами (Эрланг)     (M/M/n/0)
    MultiLimited,    // n каналов, ограниченная очередь m  (M/M/n/m)
    MultiInfinite,   // n каналов, неограниченная очередь  (M/M/n/∞)
}

public static class QsInfo
{
    public static bool IsMulti(QsKind k) => k is QsKind.MultiLoss or QsKind.MultiLimited or QsKind.MultiInfinite;
    public static bool HasLimitedQueue(QsKind k) => k is QsKind.SingleLimited or QsKind.MultiLimited;
    public static bool HasInfiniteQueue(QsKind k) => k is QsKind.SingleInfinite or QsKind.MultiInfinite;

    public static string Title(QsKind k) => k switch
    {
        QsKind.SingleLoss => "Одноканальная СМО с отказами",
        QsKind.SingleLimited => "Одноканальная СМО с ограниченной очередью",
        QsKind.SingleInfinite => "Одноканальная СМО с неограниченной очередью",
        QsKind.MultiLoss => "Многоканальная СМО с отказами",
        QsKind.MultiLimited => "Многоканальная СМО с ограниченной очередью",
        QsKind.MultiInfinite => "Многоканальная СМО с неограниченной очередью",
        _ => ""
    };

    public static string Description(QsKind k) => k switch
    {
        QsKind.SingleLoss => "Один канал, мест в очереди нет: заявка, заставшая канал занятым, получает отказ.",
        QsKind.SingleLimited => "Один канал и m мест в очереди: заявка получает отказ, если все m мест заняты.",
        QsKind.SingleInfinite => "Один канал, очередь не ограничена: отказов нет, стационарный режим существует при ρ < 1.",
        QsKind.MultiLoss => "n одинаковых каналов, очереди нет (задача Эрланга): отказ, если заняты все n каналов.",
        QsKind.MultiLimited => "n каналов и m мест в очереди: отказ, если заняты все каналы и все места в очереди.",
        QsKind.MultiInfinite => "n каналов, очередь не ограничена: отказов нет, стационарный режим существует при ρ/n < 1.",
        _ => ""
    };
}

/// <summary>Одна строка результата. Kind задаёт размерность: время, интенсивность или безразмерная величина.</summary>
public enum ValueKind { Plain, Time, Rate, Count }

public sealed record ResultRow(string Name, string Symbol, string Formula, double Value, ValueKind Kind);

public sealed class QsResult
{
    public List<ResultRow> Rows { get; } = new();
    /// <summary>Вероятности состояний: (подпись, P).</summary>
    public List<(string State, double P)> States { get; } = new();
    public bool StatesTruncated { get; set; }
    public string? Error { get; set; }
}


/// <summary>
/// Расчёт характеристик СМО (формулы — как в рабочем варианте ParisNe/Calculator).
/// λ и μ передаются в одной единице времени (например, 1/мин) — все времена в результате в ней же.
/// </summary>
public static class QueueCalculator
{
    private const int MaxShownStates = 40;

    public static QsResult Calculate(QsKind kind, double lambda, double mu, int n, int m)
    {
        var r = new QsResult();
        if (!QsInfo.IsMulti(kind)) n = 1;
        if (!QsInfo.HasLimitedQueue(kind)) m = 0;
        double rho = lambda / mu;

        // Исходные данные (как в шапке рабочего варианта)
        Add(r, "Интенсивность входного потока", "λ", "задано", lambda, ValueKind.Rate);
        Add(r, "Интенсивность обслуживания", "μ", "μ = 1 / tоб", mu, ValueKind.Rate);
        Add(r, "Среднее время обслуживания", "tоб", "tоб = 1 / μ", 1 / mu, ValueKind.Time);
        Add(r, "Приведённая интенсивность", "ρ", "ρ = λ / μ", rho, ValueKind.Plain);
        if (n > 1) Add(r, "Нагрузка на один канал", "ρ/n", "ρ / n", rho / n, ValueKind.Plain);

        switch (kind)
        {
            case QsKind.SingleLoss: SingleReject(r, lambda, mu); break;
            case QsKind.SingleInfinite: SingleUnlimited(r, lambda, mu); break;
            case QsKind.SingleLimited: SingleLimited(r, lambda, mu, m); break;
            case QsKind.MultiLoss: MultiReject(r, lambda, mu, n); break;
            case QsKind.MultiInfinite: MultiUnlimited(r, lambda, mu, n); break;
            case QsKind.MultiLimited: MultiLimited(r, lambda, mu, n, m); break;
        }
        return r;
    }

    // ==================== 1. M/M/1/0 ====================
    private static void SingleReject(QsResult r, double lambda, double mu)
    {
        double rho = lambda / mu;
        double p0 = 1.0 / (1.0 + rho);
        double pRej = rho / (1.0 + rho);
        double pSer = 1.0 - pRej;
        double L = rho / (1.0 + rho);
        double W = L / lambda;
        double Wob = 1.0 / mu;

        Add(r, "Вероятность простоя канала", "P0", "P0 = 1 / (1 + ρ)", p0, ValueKind.Plain);
        Add(r, "Вероятность отказа", "Pотк", "Pотк = ρ / (1 + ρ)", pRej, ValueKind.Plain);
        Add(r, "Вероятность обслуживания", "Pобс", "Pобс = 1 − Pотк", pSer, ValueKind.Plain);
        Add(r, "Абсолютная пропускная способность", "A", "A = λ · Pобс", lambda * pSer, ValueKind.Rate);
        Add(r, "Среднее число заявок в системе", "L", "L = ρ / (1 + ρ)", L, ValueKind.Count);
        Add(r, "Среднее время пребывания в системе", "T", "T = L / λ", W, ValueKind.Time);
        Add(r, "Среднее время обслуживания", "Tоб", "Tоб = 1 / μ", Wob, ValueKind.Time);
        Add(r, "Среднее время ожидания", "Tожид", "Tожид = 0 (отказы)", 0, ValueKind.Time);

        States(r, p0, rho, 1, 0);
    }

    // ==================== 2. M/M/1/∞ ====================
    private static void SingleUnlimited(QsResult r, double lambda, double mu)
    {
        double rho = lambda / mu;
        if (rho >= 1)
        {
            r.Error = $"НЕСТАБИЛЬНА: ρ = {rho:0.####} ≥ 1, очередь растёт бесконечно.";
            return;
        }
        double p0 = 1.0 - rho;
        double Lq = rho * rho / (1.0 - rho);
        double L = rho / (1.0 - rho);
        double Wq = Lq / lambda;
        double W = 1.0 / (mu - lambda);

        Add(r, "Вероятность простоя канала", "P0", "P0 = 1 − ρ", p0, ValueKind.Plain);
        Add(r, "Вероятность ожидания", "Pожид", "Pожид = ρ", rho, ValueKind.Plain);
        Add(r, "Среднее число заявок в очереди", "Lоч", "Lоч = ρ² / (1 − ρ)", Lq, ValueKind.Count);
        Add(r, "Среднее число заявок в системе", "L", "L = ρ / (1 − ρ)", L, ValueKind.Count);
        Add(r, "Среднее время ожидания в очереди", "Tоч", "Tоч = Lоч / λ", Wq, ValueKind.Time);
        Add(r, "Среднее время пребывания в системе", "T", "T = 1 / (μ − λ)", W, ValueKind.Time);

        States(r, p0, rho, 1, -1);
    }

    // ==================== 3. M/M/1/m ====================
    private static void SingleLimited(QsResult r, double lambda, double mu, int m)
    {
        double rho = lambda / mu;
        bool one = Math.Abs(rho - 1) < 1e-9;
        double p0 = one ? 1.0 / (m + 2) : (1 - rho) / (1 - Math.Pow(rho, m + 2));
        double pRej = Math.Pow(rho, m + 1) * p0;
        double pSer = 1 - pRej;
        double lEff = lambda * pSer;
        double Lq = one
            ? m * (m + 1) / (2.0 * (m + 2))
            : rho * rho * (1 - (m + 1) * Math.Pow(rho, m) + m * Math.Pow(rho, m + 1))
              / ((1 - rho) * (1 - Math.Pow(rho, m + 2)));
        double L = Lq + (1 - p0);              // в рабочем варианте было Lq + Pобс — ошибка
        double Wq = lEff > 0 ? Lq / lEff : 0;
        double W = Wq + 1.0 / mu;

        Add(r, "Вероятность простоя канала", "P0",
            one ? "P0 = 1 / (m + 2)   (ρ = 1)" : "P0 = (1 − ρ) / (1 − ρ^(m+2))", p0, ValueKind.Plain);
        Add(r, "Вероятность отказа", "Pотк", "Pотк = ρ^(m+1) · P0", pRej, ValueKind.Plain);
        Add(r, "Вероятность обслуживания", "Pобс", "Pобс = 1 − Pотк", pSer, ValueKind.Plain);
        Add(r, "Абсолютная пропускная способность", "A", "A = λ · Pобс", lEff, ValueKind.Rate);
        Add(r, "Среднее число заявок в очереди", "Lоч",
            one ? "Lоч = m(m+1) / (2(m+2))   (ρ = 1)"
                : "Lоч = ρ²·(1 − (m+1)ρᵐ + m·ρ^(m+1)) / ((1 − ρ)(1 − ρ^(m+2)))", Lq, ValueKind.Count);
        Add(r, "Среднее число заявок в системе", "L", "L = Lоч + (1 − P0)", L, ValueKind.Count);
        Add(r, "Среднее время ожидания в очереди", "Tоч", "Tоч = Lоч / A", Wq, ValueKind.Time);
        Add(r, "Среднее время пребывания в системе", "T", "T = Tоч + 1 / μ", W, ValueKind.Time);

        States(r, p0, rho, 1, m);
    }

    // ==================== 4. M/M/n/0 ====================
    private static void MultiReject(QsResult r, double lambda, double mu, int n)
    {
        double rho = lambda / mu;
        double sum = 0;
        for (int k = 0; k <= n; k++) sum += Math.Pow(rho, k) / Fact(k);
        double p0 = 1.0 / sum;
        double pRej = Math.Pow(rho, n) / Fact(n) * p0;
        double pSer = 1 - pRej;

        Add(r, "Вероятность простоя всех каналов", "P0", "P0 = [ Σₖ₌₀ⁿ ρᵏ/k! ]⁻¹", p0, ValueKind.Plain);
        Add(r, "Вероятность отказа", "Pотк", "Pотк = ρⁿ/n! · P0", pRej, ValueKind.Plain);
        Add(r, "Вероятность обслуживания", "Pобс", "Pобс = 1 − Pотк", pSer, ValueKind.Plain);
        Add(r, "Абсолютная пропускная способность", "A", "A = λ · Pобс", lambda * pSer, ValueKind.Rate);
        Add(r, "Среднее число заявок в системе (занятых каналов)", "L", "L = ρ · Pобс", rho * pSer, ValueKind.Count);
        Add(r, "Среднее время пребывания в системе", "T", "T = 1 / μ", 1.0 / mu, ValueKind.Time);

        States(r, p0, rho, n, 0);
    }

    // ==================== 5. M/M/n/∞ ====================
    private static void MultiUnlimited(QsResult r, double lambda, double mu, int n)
    {
        double rho = lambda / mu, rn = rho / n;
        if (rn >= 1)
        {
            r.Error = $"НЕСТАБИЛЬНА: ρ/n = {rn:0.####} ≥ 1, очередь растёт бесконечно.";
            return;
        }
        double sum = 0;
        for (int k = 0; k < n; k++) sum += Math.Pow(rho, k) / Fact(k);
        sum += Math.Pow(rho, n) / Fact(n) * (1.0 / (1.0 - rn));
        double p0 = 1.0 / sum;
        double pWait = Math.Pow(rho, n) / Fact(n) * (1.0 / (1.0 - rn)) * p0;
        double Lq = pWait * rn / (1.0 - rn);
        double Wq = Lq / lambda;
        double W = Wq + 1.0 / mu;

        Add(r, "Вероятность простоя всех каналов", "P0",
            "P0 = [ Σₖ₌₀ⁿ⁻¹ ρᵏ/k! + ρⁿ/n! · 1/(1 − ρ/n) ]⁻¹", p0, ValueKind.Plain);
        Add(r, "Вероятность ожидания", "Pожид", "Pожид = ρⁿ/n! · 1/(1 − ρ/n) · P0", pWait, ValueKind.Plain);
        Add(r, "Среднее число заявок в очереди", "Lоч", "Lоч = Pожид · (ρ/n) / (1 − ρ/n)", Lq, ValueKind.Count);
        Add(r, "Среднее число заявок в системе", "L", "L = Lоч + ρ", Lq + rho, ValueKind.Count);
        Add(r, "Среднее время ожидания в очереди", "Tоч", "Tоч = Lоч / λ", Wq, ValueKind.Time);
        Add(r, "Среднее время пребывания в системе", "T", "T = Tоч + 1 / μ", W, ValueKind.Time);

        States(r, p0, rho, n, -1);
    }

    // ==================== 6. M/M/n/m ====================
    private static void MultiLimited(QsResult r, double lambda, double mu, int n, int m)
    {
        double rho = lambda / mu, q = rho / n;
        double sum = 0;
        for (int k = 0; k <= n; k++) sum += Math.Pow(rho, k) / Fact(k);
        double sumQ = 0;
        for (int k = 1; k <= m; k++) sumQ += Math.Pow(q, k);
        sum += Math.Pow(rho, n) / Fact(n) * sumQ;
        double p0 = 1.0 / sum;
        double pRej = Math.Pow(rho, n + m) / (Fact(n) * Math.Pow(n, m)) * p0;
        double pSer = 1 - pRej;
        double lEff = lambda * pSer;
        double pWait = 0;
        for (int k = 1; k <= m; k++)
            pWait += Math.Pow(rho, n) / Fact(n) * Math.Pow(q, k) * p0;
        bool one = Math.Abs(q - 1) < 1e-9;
        double Lq = one
            ? Math.Pow(rho, n) / Fact(n) * p0 * m * (m + 1) / 2.0
            : Math.Pow(rho, n) / Fact(n) * p0 * q * (1 - (m + 1) * Math.Pow(q, m) + m * Math.Pow(q, m + 1)) / Math.Pow(1 - q, 2);
        double Wq = lEff > 0 ? Lq / lEff : 0;
        double W = Wq + 1.0 / mu;

        Add(r, "Вероятность простоя всех каналов", "P0",
            "P0 = [ Σₖ₌₀ⁿ ρᵏ/k! + ρⁿ/n! · Σₛ₌₁ᵐ (ρ/n)ˢ ]⁻¹", p0, ValueKind.Plain);
        Add(r, "Вероятность отказа", "Pотк", "Pотк = ρ^(n+m) / (n!·nᵐ) · P0", pRej, ValueKind.Plain);
        Add(r, "Вероятность обслуживания", "Pобс", "Pобс = 1 − Pотк", pSer, ValueKind.Plain);
        Add(r, "Вероятность ожидания", "Pожид", "Pожид = Σₛ₌₁ᵐ ρⁿ/n! · (ρ/n)ˢ · P0", pWait, ValueKind.Plain);
        Add(r, "Абсолютная пропускная способность", "A", "A = λ · Pобс", lEff, ValueKind.Rate);
        Add(r, "Среднее число заявок в очереди", "Lоч",
            one ? "Lоч = ρⁿ/n! · P0 · m(m+1)/2   (ρ/n = 1)"
                : "Lоч = ρⁿ/n! · P0 · q·(1 − (m+1)qᵐ + m·q^(m+1)) / (1 − q)²,  q = ρ/n", Lq, ValueKind.Count);
        Add(r, "Среднее число заявок в системе", "L", "L = Lоч + ρ · Pобс", Lq + rho * pSer, ValueKind.Count);
        Add(r, "Среднее время ожидания в очереди", "Tоч", "Tоч = Lоч / A", Wq, ValueKind.Time);
        Add(r, "Среднее время пребывания в системе", "T", "T = Tоч + 1 / μ", W, ValueKind.Time);

        States(r, p0, rho, n, m);
    }

    // ==================== вспомогательные ====================

    /// <summary>
    /// Вероятности состояний Pk: ρᵏ/k!·P0 для k ≤ n, далее ·(ρ/n) на каждое место в очереди.
    /// m ≥ 0 — конечная очередь из m мест; m = −1 — неограниченная (показываем, пока Pk заметны).
    /// </summary>
    private static void States(QsResult r, double p0, double rho, int n, int m)
    {
        double p = p0;
        r.States.Add(("P0", p));
        for (int k = 1; k <= n && r.States.Count < MaxShownStates; k++)
        {
            p *= rho / k;
            r.States.Add(($"P{k}", p));
        }
        double step = rho / n;
        if (m >= 0)
        {
            for (int s = 1; s <= m && r.States.Count < MaxShownStates; s++)
            {
                p *= step;
                r.States.Add(($"P{n + s}", p));
            }
            r.StatesTruncated = n + m + 1 > MaxShownStates;
        }
        else
        {
            int s = 0;
            while (r.States.Count < MaxShownStates && (p > 1e-4 || s < 3))
            {
                p *= step;
                s++;
                r.States.Add(($"P{n + s}", p));
            }
            r.StatesTruncated = true;
        }
    }

    private static double Fact(int n)
    {
        double f = 1;
        for (int i = 2; i <= n; i++) f *= i;
        return f;
    }

    private static void Add(QsResult r, string name, string sym, string formula, double v, ValueKind kind) =>
        r.Rows.Add(new ResultRow(name, sym, formula, v, kind));
}
