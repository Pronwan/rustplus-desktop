#if DEBUG
using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Features.Tutorials;

public sealed class TutorialInspector
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly ITutorialRegistry _registry;
    private readonly ITutorialService _service;
    private readonly Action<string> _log;
    private InspectorAdorner? _adorner;
    private FrameworkElement? _selected;

    public TutorialInspector(Window window, FrameworkElement root, ITutorialRegistry registry, ITutorialService service, Action<string> log)
    {
        _window = window;
        _root = root;
        _registry = registry;
        _service = service;
        _log = log;
        _window.PreviewKeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Toggle();
            e.Handled = true;
        }
        else if (_adorner is not null && e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            string? id = _selected is null ? null : Tutorial.GetTargetId(_selected);
            if (!string.IsNullOrWhiteSpace(id)) Clipboard.SetText(id);
            e.Handled = true;
        }
        else if (_adorner is not null && e.Key == Key.Enter && _selected is not null)
        {
            string? id = Tutorial.GetTargetId(_selected);
            var match = _registry.Tutorials.SelectMany(t => t.Steps.Select(s => (Tutorial: t, Step: s)))
                .FirstOrDefault(x => x.Step.TargetId == id);
            if (match.Tutorial is not null) _ = _service.StartAsync(match.Tutorial.Id, match.Step.Id);
            e.Handled = true;
        }
    }

    private void Toggle()
    {
        if (_adorner is not null)
        {
            _window.PreviewMouseMove -= OnMouseMove;
            AdornerLayer.GetAdornerLayer(_root)?.Remove(_adorner);
            _adorner = null;
            _selected = null;
            return;
        }

        _adorner = new InspectorAdorner(_root);
        AdornerLayer.GetAdornerLayer(_root)?.Add(_adorner);
        _window.PreviewMouseMove += OnMouseMove;
        foreach (string error in _registry.Validate()) _log($"[tutorial-inspector] {error}");
        _log("[tutorial-inspector] enabled; hover a control, Ctrl+C copies its target ID, Enter starts its step");
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_adorner is null) return;
        DependencyObject? hit = _root.InputHitTest(e.GetPosition(_root)) as DependencyObject;
        while (hit is not null && hit is not FrameworkElement) hit = VisualTreeHelper.GetParent(hit);
        _selected = hit as FrameworkElement;
        _adorner.SetElement(_selected);
    }

    private sealed class InspectorAdorner(FrameworkElement adornedElement) : Adorner(adornedElement)
    {
        private FrameworkElement? _element;
        public void SetElement(FrameworkElement? element) { _element = element; InvalidateVisual(); }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_element is null || !_element.IsVisible) return;
            try
            {
                Point p = _element.TransformToAncestor(AdornedElement).Transform(new Point());
                var rect = new Rect(p, new Size(_element.ActualWidth, _element.ActualHeight));
                drawingContext.DrawRectangle(null, new Pen(Brushes.Magenta, 2), rect);
                string text = $"{_element.GetType().Name}  x:Name={_element.Name}  TargetId={Tutorial.GetTargetId(_element) ?? "—"}  {rect.Width:0}×{rect.Height:0}";
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var box = new Rect(rect.Left, Math.Max(0, rect.Top - 25), formatted.Width + 12, 23);
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 40, 0, 48)), new Pen(Brushes.Magenta, 1), box, 4, 4);
                drawingContext.DrawText(formatted, new Point(box.Left + 6, box.Top + 4));
            }
            catch (InvalidOperationException) { }
        }
    }
}
#endif
