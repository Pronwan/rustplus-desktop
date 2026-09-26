using System;

namespace RustPlusDesk.Services;

internal sealed class ServerClock
{
    public double DaySpeed { get; private set; } = 12.0 / 50.0;
    public double NightSpeed { get; private set; } = 12.0 / 10.0;
    public double Sunrise { get; private set; } = 8;
    public double Sunset { get; private set; } = 20;
    private DateTime? _sampleTime;
    private double _sampleHours;

    public bool IsDay(double hours) => hours >= Sunrise && hours < Sunset;

    public void Observe(double hours, DateTime now, double? sunrise = null, double? sunset = null)
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

    public double Advance(double hours, double minutes)
    {
        // Split at the boundary so extrapolation uses the new phase's speed.
        double speed = IsDay(hours) ? DaySpeed : NightSpeed;
        double boundary = IsDay(hours) ? Sunset : hours >= Sunset ? 24 + Sunrise : Sunrise;
        double untilBoundary = (boundary - hours) / speed;
        if (minutes > untilBoundary)
            return ((boundary % 24) + (minutes - untilBoundary) * (IsDay(hours) ? NightSpeed : DaySpeed)) % 24;
        return (hours + minutes * speed) % 24;
    }
}
