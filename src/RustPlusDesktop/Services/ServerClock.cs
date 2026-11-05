using System;

namespace RustPlusDesk.Services;

internal sealed class ServerClock
{
    public double DaySpeed { get; private set; } = 12.0 / 50.0;
    public double NightSpeed { get; private set; } = 12.0 / 10.0;
    public double Sunrise { get; private set; } = 8;
    public double Sunset { get; private set; } = 20;
    public bool IsPaused { get; private set; }
    /// <summary>Share of the reported cycle spent between sunrise and sunset (70 of 80 minutes).</summary>
    public const double DayShare = 70.0 / 80.0;
    /// <summary>Real minutes of the last fully observed day or night, when one has been seen.</summary>
    public double? MeasuredDayMinutes { get; private set; }
    public double? MeasuredNightMinutes { get; private set; }
    private DateTime? _sampleTime;
    private double _sampleHours;
    private double? _cycleMinutes;
    private DateTime? _lastSeen;
    private double _lastSeenHours;
    private DateTime? _phaseStart;

    public bool IsDay(double hours) => hours >= Sunrise && hours < Sunset;

    public void Observe(double hours, DateTime now, double? sunrise = null, double? sunset = null,
        double? dayLengthMinutes = null, double? timeScale = null)
    {
        if (!double.IsFinite(hours)) return;
        if (sunrise.HasValue && sunset.HasValue && double.IsFinite(sunrise.Value)
            && double.IsFinite(sunset.Value) && sunrise >= 0 && sunrise < sunset && sunset < 24)
        {
            if (Sunrise != sunrise.Value || Sunset != sunset.Value) _sampleTime = null;
            Sunrise = sunrise.Value;
            Sunset = sunset.Value;
        }
        hours = ((hours % 24) + 24) % 24;
        bool wasPaused = IsPaused;
        IsPaused = false;
        if (dayLengthMinutes.HasValue && double.IsFinite(dayLengthMinutes.Value) && dayLengthMinutes > 0
            && timeScale.HasValue && double.IsFinite(timeScale.Value) && timeScale >= 0)
        {
            IsPaused = timeScale == 0;
            _sampleTime = null;
            if (IsPaused)
            {
                _lastSeen = null;
                return;
            }
            // The API reports only the cycle length. Rust's time curve spends 70 of every 80
            // minutes in daylight and 10 at night, so each phase gets its share at an even pace.
            double cycleMinutes = dayLengthMinutes.Value / timeScale.Value;
            if (_cycleMinutes != cycleMinutes)
            {
                _cycleMinutes = cycleMinutes;
                MeasuredDayMinutes = MeasuredNightMinutes = null;
                _phaseStart = null;
            }
            TrackPhaseLength(hours, now);
            double dayMinutes = PhaseMinutes(cycleMinutes * DayShare, MeasuredDayMinutes);
            double nightMinutes = PhaseMinutes(cycleMinutes * (1 - DayShare), MeasuredNightMinutes);
            double dayHours = Sunset - Sunrise;
            DaySpeed = dayHours / dayMinutes;
            NightSpeed = (24 - dayHours) / nightMinutes;
            return;
        }
        if (wasPaused) _sampleTime = null;
        if (DaySpeed <= 0) DaySpeed = 12.0 / 50.0;
        if (NightSpeed <= 0) NightSpeed = 12.0 / 10.0;
        // ponytail: phase-average curve estimate; use the exact curve if the protocol ever exposes it.
        if (_sampleTime.HasValue)
        {
            double minutes = (now - _sampleTime.Value).TotalMinutes;
            bool samePhase = IsDay(hours) == IsDay(_sampleHours);
            // Accumulate a stable sample; short, minute-rounded deltas skew learning.
            if (minutes >= 0 && minutes < 0.5 && samePhase) return;
            double delta = hours - _sampleHours;
            if (delta < -12) delta += 24;
            if (minutes >= 0.5 && minutes < 5 && samePhase && delta >= 0 && delta < 0.1) return;
            if (minutes >= 0.5 && minutes < 5 && samePhase && delta >= 0.1 && delta < 2)
            {
                if (IsDay(hours)) DaySpeed = delta / minutes;
                else NightSpeed = delta / minutes;
            }
        }
        _sampleTime = now;
        _sampleHours = hours;
    }

    /// <summary>
    /// Modded servers can shorten the night or lengthen the day without the API saying so.
    /// A phase measured more than 20% away from its 70/10 share replaces that share.
    /// </summary>
    private static double PhaseMinutes(double share, double? measured) =>
        measured is double m && m > 0 && Math.Abs(m - share) > 0.2 * share ? m : share;

    /// <summary>Times each phase from one observed boundary crossing to the next.</summary>
    private void TrackPhaseLength(double hours, DateTime now)
    {
        if (_lastSeen is DateTime prev)
        {
            double minutes = (now - prev).TotalMinutes;
            double delta = hours - _lastSeenHours;
            if (delta < -12) delta += 24;
            // A gap, or a jump such as a skipped night, breaks the measurement.
            if (minutes <= 0 || minutes > 2 || delta < 0 || delta > 6) _phaseStart = null;
            else if (IsDay(hours) != IsDay(_lastSeenHours))
            {
                double toBoundary = (IsDay(hours) ? Sunrise : Sunset) - _lastSeenHours;
                if (toBoundary < 0) toBoundary += 24;
                DateTime crossed = prev.AddMinutes(delta > 0 ? minutes * Math.Min(1, toBoundary / delta) : 0);
                if (_phaseStart is DateTime start)
                {
                    double length = (crossed - start).TotalMinutes;
                    if (IsDay(hours)) MeasuredNightMinutes = length;
                    else MeasuredDayMinutes = length;
                }
                _phaseStart = crossed;
            }
        }
        _lastSeen = now;
        _lastSeenHours = hours;
    }

    public double Advance(double hours, double minutes)
    {
        if (IsPaused) return hours;
        // Split at the boundary so extrapolation uses the new phase's speed.
        double speed = IsDay(hours) ? DaySpeed : NightSpeed;
        double boundary = IsDay(hours) ? Sunset : hours >= Sunset ? 24 + Sunrise : Sunrise;
        double untilBoundary = (boundary - hours) / speed;
        if (minutes > untilBoundary)
            return ((boundary % 24) + (minutes - untilBoundary) * (IsDay(hours) ? NightSpeed : DaySpeed)) % 24;
        return (hours + minutes * speed) % 24;
    }
}
