using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RustPlusDesk.Views;

// Phase 1 of the map-tools overhaul: non-destructive editing (undo/redo),
// a temporary space-to-pan hand, and a cursor that reflects the active tool.
public partial class MainWindow
{
    // A single reversible edit to my overlay. Undo and Redo restore the exact
    // before/after state; the stacks below sequence them.
    private sealed class OverlayEdit
    {
        public Action Undo { get; init; } = static () => { };
        public Action Redo { get; init; } = static () => { };
    }

    private readonly Stack<OverlayEdit> _undoStack = new();
    private readonly Stack<OverlayEdit> _redoStack = new();

    // While Space is held, left-drag pans the map instead of using the active tool.
    private bool _spacePanHeld;

    // Captured when a drag starts, so the move can be recorded as one undo step.
    private Point? _dragStartPos;

    private void PushOverlayEdit(Action undo, Action redo)
    {
        _undoStack.Push(new OverlayEdit { Undo = undo, Redo = redo });
        _redoStack.Clear();
    }

    private void ClearOverlayHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void OverlayUndo()
    {
        if (_undoStack.Count == 0) return;
        OverlayEdit edit = _undoStack.Pop();
        edit.Undo();
        _redoStack.Push(edit);
        SaveOwnOverlayToJson();
    }

    private void OverlayRedo()
    {
        if (_redoStack.Count == 0) return;
        OverlayEdit edit = _redoStack.Pop();
        edit.Redo();
        _undoStack.Push(edit);
        SaveOwnOverlayToJson();
    }

    // --- element add/remove primitives used by the reversible edits ---

    private void RemoveOwnElement(FrameworkElement element)
    {
        Overlay.Children.Remove(element);
        if (_playerOverlayElements.TryGetValue(_mySteamId, out List<FrameworkElement>? mine))
            mine.Remove(element);
    }

    private void ReAddOwnElement(FrameworkElement element)
    {
        if (!Overlay.Children.Contains(element))
            Overlay.Children.Add(element);
        if (_playerOverlayElements.TryGetValue(_mySteamId, out List<FrameworkElement>? mine))
        {
            if (!mine.Contains(element)) mine.Add(element);
        }
        else
        {
            _playerOverlayElements[_mySteamId] = new List<FrameworkElement> { element };
        }
    }

    /// <summary>Record a freshly placed element (stroke, icon, text) as one undoable step.</summary>
    private void RecordOverlayAdd(FrameworkElement element)
    {
        if (_isShowingDeepSeaMap) return; // deep-sea overlay is transient, not part of the saved plan
        PushOverlayEdit(
            undo: () => RemoveOwnElement(element),
            redo: () => ReAddOwnElement(element));
    }

    /// <summary>Record a batch erase so the whole touch removes/restores together.</summary>
    private void RecordOverlayErase(IReadOnlyList<FrameworkElement> removed)
    {
        if (_isShowingDeepSeaMap || removed.Count == 0) return;
        var snapshot = new List<FrameworkElement>(removed);
        PushOverlayEdit(
            undo: () => { foreach (FrameworkElement fe in snapshot) ReAddOwnElement(fe); },
            redo: () => { foreach (FrameworkElement fe in snapshot) RemoveOwnElement(fe); });
    }

    /// <summary>Record a drag as a single position change.</summary>
    private void RecordOverlayMove(FrameworkElement element, Point from, Point to)
    {
        if (_isShowingDeepSeaMap) return;
        if (Math.Abs(from.X - to.X) < 0.5 && Math.Abs(from.Y - to.Y) < 0.5) return;
        PushOverlayEdit(
            undo: () => { Canvas.SetLeft(element, from.X); Canvas.SetTop(element, from.Y); },
            redo: () => { Canvas.SetLeft(element, to.X); Canvas.SetTop(element, to.Y); });
    }

    // --- cursor feedback ---

    /// <summary>Cursor reflects the active mode: hand while space-panning, cross while a draw tool is armed, arrow otherwise.</summary>
    private void ApplyToolCursor()
    {
        if (WebViewHost == null) return;
        if (_spacePanHeld)
        {
            WebViewHost.Cursor = Cursors.Hand;
            return;
        }
        bool drawing = _overlayToolsVisible && _currentTool != OverlayToolMode.None;
        WebViewHost.Cursor = drawing ? Cursors.Cross : Cursors.Arrow;
    }
}
