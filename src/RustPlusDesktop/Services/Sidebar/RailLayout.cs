using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services.Sidebar;

/// <summary>
/// Persisted, serializable description of the sidebar rail: an ordered list of
/// top-level nodes, where each node is either a single tab or a folder holding an
/// ordered list of tab ids. Only stable ids and folder metadata are stored here —
/// the visuals (icons, names) live in <see cref="RailCatalog"/> in code.
/// </summary>
public sealed class RailLayoutData
{
    /// <summary>Bumped when the shipped default layout changes so we can re-seed folders.</summary>
    public int Version { get; set; }

    public List<RailNodeData> Nodes { get; set; } = new();

    public bool IsEmpty => Nodes.Count == 0;
}

/// <summary>A single rail entry: either a tab (<see cref="TabId"/>) or a folder.</summary>
public sealed class RailNodeData
{
    /// <summary>"tab" or "folder".</summary>
    public string Kind { get; set; } = RailNodeKind.Tab;

    // Tab node
    public string? TabId { get; set; }

    // Folder node
    public string? FolderId { get; set; }
    public string? Name { get; set; }
    public string? ColorHex { get; set; }
    public bool Expanded { get; set; }
    public List<string> Children { get; set; } = new();

    public bool IsFolder => Kind == RailNodeKind.Folder;
    public bool IsTab => Kind == RailNodeKind.Tab;

    public static RailNodeData Tab(string tabId) => new() { Kind = RailNodeKind.Tab, TabId = tabId };

    public static RailNodeData Folder(string folderId, string name, string colorHex, IEnumerable<string> children, bool expanded = false)
        => new()
        {
            Kind = RailNodeKind.Folder,
            FolderId = folderId,
            Name = name,
            ColorHex = colorHex,
            Expanded = expanded,
            Children = children.ToList(),
        };
}

public static class RailNodeKind
{
    public const string Tab = "tab";
    public const string Folder = "folder";
}
