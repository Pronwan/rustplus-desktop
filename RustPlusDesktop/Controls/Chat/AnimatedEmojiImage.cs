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
    private DispatcherTimer? _timer;
    private int _currentFrameIndex = 0;
    private bool _isHovered = false;

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
            Source = null;
            return;
        }

        // 1. Immediately set the static thumbnail (0ms UI latency!)
        Source = EmojiService.GetCustomEmojiImage(EmojiName);

        // 2. Check if animation frames are already cached in RAM
        if (EmojiService.TryGetCachedFrames(EmojiName, out var cached))
        {
            _frames = cached;
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

        if (AutoPlay)
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

        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16.67) // 60 fps
            };
            _timer.Tick += Timer_Tick;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void StopAnimation()
    {
        if (_timer != null && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void ResetToFirstFrame()
    {
        _currentFrameIndex = 0;
        if (_frames != null && _frames.Length > 0)
        {
            Source = _frames[0];
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_frames == null || _frames.Length == 0) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Length;
        Source = _frames[_currentFrameIndex];
    }
}
