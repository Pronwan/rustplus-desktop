namespace RustPlusDesk.Services
{
    /// <summary>
    /// Flags for upcoming Rust+ API restrictions.
    ///
    /// Facepunch committed a change to stop sending vending-machine and dynamic
    /// event markers (Cargo Ship, Patrol Helicopter, Travelling Vendor, Vending
    /// machines) to Rust+. It takes effect on a force wipe, not immediately.
    ///
    /// Flip <see cref="EventsAndShopsRemoved"/> to true once that lands: the app
    /// then hides the now-dead shops + events UI (map shop toggle + auto-load,
    /// the events dock, and the events alerts column) and stops shop polling.
    /// While false, everything behaves exactly as before.
    /// </summary>
    public static class RustApiFeatures
    {
        public const bool EventsAndShopsRemoved = false;
    }
}
