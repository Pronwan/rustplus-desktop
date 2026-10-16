using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RustPlusDesk.Avalonia.Services;

internal sealed class ProfileFilePicker(Window owner) : IProfileFilePicker
{
    public async Task<string?> PickAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Rust+ Desktop profiles",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON profiles") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return null;
        using var file = files[0];
        return file.TryGetLocalPath() ?? throw new NotSupportedException("A local profiles file is required.");
    }
}
