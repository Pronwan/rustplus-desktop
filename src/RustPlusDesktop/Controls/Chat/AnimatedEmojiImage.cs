using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public class AnimatedEmojiImage : Image
{
    public static readonly DependencyProperty EmojiNameProperty =
        DependencyProperty.Register(
            nameof(EmojiName),
            typeof(string),
            typeof(AnimatedEmojiImage),
            new PropertyMetadata(null, OnEmojiNameChanged));

    public static readonly DependencyProperty AutoPlayProperty =
        DependencyProperty.Register(
            nameof(AutoPlay),
            typeof(bool),
            typeof(AnimatedEmojiImage),
            new PropertyMetadata(true, OnPlayModeChanged));

    public static readonly DependencyProperty AnimateOnHoverProperty =
        DependencyProperty.Register(
            nameof(AnimateOnHover),
            typeof(bool),
            typeof(AnimatedEmojiImage),
            new PropertyMetadata(false, OnPlayModeChanged));

    public string? EmojiName
    {
        get => (string?)GetValue(EmojiNameProperty);
        set => SetValue(EmojiNameProperty, value);
    }

    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    public bool AnimateOnHover
    {
        get => (bool)GetValue(AnimateOnHoverProperty);
        set => SetValue(AnimateOnHoverProperty, value);
    }

    private BitmapSource[]? _frames;
    private int[]? _frameDelays;
    private int _currentFrameIndex = 0;
    private bool _isHovered = false;

    // Frame playback is driven off the render loop rather than a DispatcherTimer: the timer can be
    // starved under load (which shows up as an emoji frozen on one frame), whereas the render
    // callback fires in lockstep with the compositor and advances by real elapsed time.
    private bool _isPlaying = false;
    private TimeSpan _lastRenderTime = TimeSpan.Zero;
    private double _accumulatedMs = 0;

    public AnimatedEmojiImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
    }

    private static void OnEmojiNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedEmojiImage img)
        {
            img.LoadFrames();
        }
    }

    private static void OnPlayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedEmojiImage img)
        {
            img.UpdateAnimationState();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadFrames();
        UpdateAnimationState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isHovered = false;
        StopAnimation();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        if (AnimateOnHover && !AutoPlay)
        {
            if (_frames != null && _frames.Length > 1)
            {
                StartAnimation();
            }
            else
            {
                var target = EmojiName;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    EmojiService.GetCustomEmojiFramesAsync(target).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully && t.Result != null)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                if (EmojiName == target)
                                {
                                    _frames = t.Result;
                                    _frameDelays = EmojiService.GetFrameDelays(target);
                                    if (_isHovered && AnimateOnHover && !AutoPlay)
                                    {
                                        StartAnimation();
                                    }
                                }
                            }, DispatcherPriority.Render);
                        }
                    });
                }
            }
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        if (AnimateOnHover && !AutoPlay)
        {
            StopAnimation();
            ResetToFirstFrame();
        }
    }

    private void LoadFrames()
    {
        if (string.IsNullOrWhiteSpace(EmojiName))
        {
            _frames = null;
            _frameDelays = null;
            Source = null;
            return;
        }

        // 1. Immediately set the static thumbnail (0ms UI latency!)
        Source = EmojiService.GetCustomEmojiImage(EmojiName);

        // 2. Check if animation frames are already cached in RAM
        if (EmojiService.TryGetCachedFrames(EmojiName, out var cached))
        {
            _frames = cached;
            _frameDelays = EmojiService.GetFrameDelays(EmojiName);
            if (_currentFrameIndex >= _frames.Length) _currentFrameIndex = 0;
            Source = _frames[_currentFrameIndex];
            UpdateAnimationState();
        }
        else
        {
            // 3. Load asynchronously in background
            var target = EmojiName;
            EmojiService.GetCustomEmojiFramesAsync(target).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (EmojiName == target)
                        {
                            _frames = t.Result;
                            _frameDelays = EmojiService.GetFrameDelays(target);
                            if (_currentFrameIndex >= _frames.Length) _currentFrameIndex = 0;
                            if (AutoPlay || (_isHovered && AnimateOnHover))
                            {
                                StartAnimation();
                            }
                            else if (_frames.Length > 0)
                            {
                                Source = _frames[0];
                            }
                        }
                    }, DispatcherPriority.Render);
                }
            });
        }
    }

    private void UpdateAnimationState()
    {
        if (!IsLoaded || _frames == null || _frames.Length <= 1)
        {
            StopAnimation();
            return;
        }

        if (AutoPlay || (_isHovered && AnimateOnHover))
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
            ResetToFirstFrame();
        }
    }

    private void StartAnimation()
    {
        if (_frames == null || _frames.Length <= 1) return;
        if (_isPlaying) return;

        _isPlaying = true;
        _lastRenderTime = TimeSpan.Zero;
        _accumulatedMs = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_isPlaying) return;

        _isPlaying = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void ResetToFirstFrame()
    {
        _currentFrameIndex = 0;
        if (_frames != null && _frames.Length > 0)
        {
            Source = _frames[0];
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_frames == null || _frames.Length == 0)
        {
            StopAnimation();
            return;
        }

        // The render loop hands us a monotonic clock; advance by however much real time passed so
        // the emoji plays at its authored speed regardless of the display's refresh rate.
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        if (_lastRenderTime == TimeSpan.Zero)
        {
            _lastRenderTime = now;
            return;
        }

        var delta = (now - _lastRenderTime).TotalMilliseconds;
        _lastRenderTime = now;
        if (delta <= 0) return;

        _accumulatedMs += delta;

        int guard = 0;
        int delay = CurrentFrameDelay();
        while (_accumulatedMs >= delay && guard++ < _frames.Length)
        {
            _accumulatedMs -= delay;
            _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Length;
            delay = CurrentFrameDelay();
        }

        Source = _frames[_currentFrameIndex];
    }

    private int CurrentFrameDelay()
    {
        int delay = _frameDelays != null && _currentFrameIndex < _frameDelays.Length
            ? _frameDelays[_currentFrameIndex]
            : 16;
        return delay > 0 ? delay : 16;
    }
}
