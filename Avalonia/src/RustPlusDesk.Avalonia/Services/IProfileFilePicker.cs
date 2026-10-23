namespace RustPlusDesk.Avalonia.Services;

/// <summary>Selects a local profiles file using the host platform's file dialog.</summary>
public interface IProfileFilePicker
{
    Task<string?> PickAsync();
}
