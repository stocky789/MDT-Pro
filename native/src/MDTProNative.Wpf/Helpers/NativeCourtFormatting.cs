using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MDTProNative.Wpf.Helpers;

/// <summary>Aligns native court charge/sentence text with browser <c>court.js</c> / <c>root.js</c> helpers.</summary>
static class NativeCourtFormatting
{
    public static string FormatCurrency(int amount) => amount.ToString("C0", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>Compact Y/M/D breakdown matching browser <c>convertDaysToYMD</c> (30-day months).</summary>
    public static string FormatDaysToYmd(int days)
    {
        if (days < 0) days = 0;
        var years = days / 365;
        var daysAfterYears = days % 365;
        var months = daysAfterYears / 30;
        var remainingDays = daysAfterYears % 30;
        var parts = new List<string>(3);
        if (years > 0) parts.Add($"{years}y");
        if (months > 0) parts.Add($"{months}mo");
        if (remainingDays > 0) parts.Add($"{remainingDays}d");
        return parts.Count > 0 ? string.Join(", ", parts) : "0d";
    }

    public static string FormatTotalTime(int totalDays, int lifeSentences)
    {
        var timeString = FormatDaysToYmd(totalDays);
        if (lifeSentences < 1) return timeString;
        if (lifeSentences == 1) return $"Life + {timeString}";
        return $"{lifeSentences}× Life + {timeString}";
    }

    public static string ChargeDetailLine(JObject charge, int caseStatus, out bool countedTowardTotals)
    {
        countedTowardTotals = false;
        var fine = charge.Value<int?>("Fine") ?? 0;
        var timeTok = charge["Time"];
        int? timeDays = timeTok == null || timeTok.Type == JTokenType.Null ? null : charge.Value<int?>("Time");
        var minDays = charge.Value<int?>("MinDays") ?? 0;
        var maxDays = charge["MaxDays"]?.Type == JTokenType.Null ? null : charge.Value<int?>("MaxDays");
        var outcome = charge.Value<int?>("Outcome");
        var isResolved = caseStatus != 0;
        var convicted = isResolved && (outcome == 1 || (outcome is null or 0 && caseStatus == 1));
        if (!isResolved || convicted)
            countedTowardTotals = true;

        string finePart = $"Fine: {FormatCurrency(fine)}";
        string? incarcerationPart = null;

        if (!isResolved)
        {
            var useStatutoryRange = minDays > 0 || (maxDays is > 0);
            if (useStatutoryRange)
            {
                var maxV = maxDays ?? minDays;
                incarcerationPart = minDays == maxV
                    ? $"Incarceration: {FormatDaysToYmd(minDays)}"
                    : $"Incarceration: {FormatDaysToYmd(minDays)} – {(maxDays == null ? "Life" : FormatDaysToYmd(maxV))}";
            }
            else if (timeDays is > 0 || timeDays == null)
                incarcerationPart = timeDays == null
                    ? "Incarceration: Life"
                    : $"Incarceration: {FormatDaysToYmd(timeDays.Value)}";
        }
        else if (convicted)
        {
            var imposed = charge.Value<int?>("SentenceDaysServed");
            if (imposed == null)
                imposed = timeDays;
            incarcerationPart = imposed == null
                ? "Incarceration: Life"
                : $"Incarceration: {FormatDaysToYmd(imposed.Value)}";
        }
        else
        {
            incarcerationPart = null;
        }

        var line = incarcerationPart != null ? $"{finePart}  ·  {incarcerationPart}" : finePart;

        if (isResolved)
        {
            var displayOutcome = outcome is null or 0 ? caseStatus : outcome.Value;
            var outcomeLabel = displayOutcome switch
            {
                1 => "Convicted",
                2 => "Acquitted",
                3 => "Dismissed",
                _ => caseStatus == 0 ? "Pending" : NativeMdtFormat.CourtStatus(caseStatus)
            };
            line = $"{line}  ·  {outcomeLabel}";
        }

        return line;
    }

    public static void AccumulateChargeTotals(JObject charge, int caseStatus, ref int totalFine, ref int totalTime, ref int lifeSentences)
    {
        var isResolved = caseStatus != 0;
        var outcome = charge.Value<int?>("Outcome");
        var convicted = isResolved && (outcome == 1 || (outcome is null or 0 && caseStatus == 1));
        if (isResolved && !convicted) return;

        totalFine += charge.Value<int?>("Fine") ?? 0;

        var timeIsNull = charge["Time"] == null || charge["Time"]?.Type == JTokenType.Null;
        int daysForTime;
        if (isResolved && convicted && charge.Value<int?>("SentenceDaysServed") is { } served)
            daysForTime = served;
        else
            daysForTime = charge.Value<int?>("Time") ?? 0;

        totalTime += daysForTime;
        if (timeIsNull)
            lifeSentences++;
    }
}
