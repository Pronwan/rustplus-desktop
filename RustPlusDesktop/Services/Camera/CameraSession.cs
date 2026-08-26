using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi;
using RustPlusApi.Camera;
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustPlusDesk.Services.Camera
{
    /// <summary>
    /// A single live camera. Owns a dedicated <see cref="RustPlus"/> connection (the server tracks
    /// one camera subscription per connection, so multiple simultaneous cameras each need their own),
    /// drives a <see cref="CameraController"/> for subscribe / keep-alive / input, and turns the ray
    /// stream into PNG frames with a <see cref="CameraRenderer"/>.
    /// </summary>
    public sealed class CameraSession : IAsyncDisposable
    {
        private readonly RustPlus _api;              // dedicated connection, owned by this session
        private readonly CameraController _controller;
        private readonly CameraRenderer _renderer;
        private readonly object _renderLock = new();
        private DateTime _lastRenderUtc = DateTime.MinValue;
        private volatile bool _disposed;

        /// <summary>Camera identifier this session is subscribed to.</summary>
        public string CameraId => _controller.CameraId;

        /// <summary>Subscription metadata (width/height/control flags); null if the subscribe failed.</summary>
        public CameraInfo? Info => _controller.Info;

        public CameraControlFlags ControlFlags => _controller.Info?.ControlFlags ?? CameraControlFlags.None;
        public int Width => _controller.Info?.Width ?? 0;
        public int Height => _controller.Info?.Height ?? 0;

        public bool IsDrone => _controller.IsDrone;
        public bool IsPtzCamera => _controller.IsPtzCamera;
        public bool IsAutoTurret => _controller.IsAutoTurret;
        public bool IsStaticCamera => _controller.IsStaticCamera;

        /// <summary>Maximum rendered frames per second raised via <see cref="FrameRendered"/>.
        /// Ray samples still accumulate every frame; only the render/raise is throttled.</summary>
        public int TargetFps { get; set; } = 4;

        /// <summary>Raised (on a background thread) with freshly rendered PNG bytes.</summary>
        public event Action<byte[]>? FrameRendered;

        /// <summary>Raised (on a background thread) with the entities in the latest frame and the vertical FOV in degrees.</summary>
        public event Action<IReadOnlyList<CameraEntity>, double>? EntitiesUpdated;

        /// <summary>Raised when the keep-alive renewal fails (camera destroyed, disconnected, …). Frames go quiet after this.</summary>
        public event Action<string>? KeepAliveFailed;

        private CameraSession(RustPlus api, CameraController controller)
        {
            _api = api;
            _controller = controller;

            var info = controller.Info;
            var w = info?.Width ?? 0;
            var h = info?.Height ?? 0;
            _renderer = new CameraRenderer(w > 0 ? w : 1, h > 0 ? h : 1);

            _controller.OnFrameReceived += OnFrameReceived;
            _controller.OnKeepAliveFailed += OnKeepAliveFailed;
        }

        /// <summary>
        /// Opens a dedicated connection, subscribes to <paramref name="cameraId"/> and returns a live session.
        /// Throws if the connection or subscription fails (the connection is cleaned up in that case).
        /// </summary>
        public static async Task<CameraSession> StartAsync(
            string host, int port, ulong playerId, int playerToken, bool useProxy,
            string cameraId, CancellationToken ct = default)
        {
            var api = new RustPlus(new RustPlusConnection(host, port, playerId, playerToken, useProxy));
            try
            {
                await api.ConnectAsync(ct).ConfigureAwait(false);
                var response = await CameraController.SubscribeAsync(api, cameraId, null, ct).ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                    throw new InvalidOperationException(response.Error?.Message ?? $"Failed to subscribe to camera '{cameraId}'.");
                return new CameraSession(api, response.Data);
            }
            catch
            {
                try { await api.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
                throw;
            }
        }

        private void OnFrameReceived(object? sender, CameraRaysEventArg frame)
        {
            if (_disposed) return;

            IReadOnlyList<CameraEntity> entities = frame.Entities is null
                ? Array.Empty<CameraEntity>()
                : (frame.Entities as IReadOnlyList<CameraEntity>) ?? new List<CameraEntity>(frame.Entities);
            var vfov = frame.VerticalFov;

            byte[]? png = null;
            lock (_renderLock)
            {
                if (_disposed) return;
                _renderer.AddRays(frame);   // always accumulate so the image keeps sharpening

                var minIntervalMs = 1000.0 / Math.Max(1, TargetFps);
                var now = DateTime.UtcNow;
                if ((now - _lastRenderUtc).TotalMilliseconds < minIntervalMs) return;
                _lastRenderUtc = now;

                try { png = _renderer.Render(); } catch { png = null; }
            }

            if (png != null) FrameRendered?.Invoke(png);
            EntitiesUpdated?.Invoke(entities, vfov);
        }

        private void OnKeepAliveFailed(object? sender, ErrorMessage err)
            => KeepAliveFailed?.Invoke(err?.Message ?? "keep-alive failed");

        public Task SendInputAsync(CameraButtons buttons, float mouseDeltaX, float mouseDeltaY)
            => _disposed ? Task.CompletedTask : _controller.SendInputAsync(buttons, mouseDeltaX, mouseDeltaY, CancellationToken.None);

        /// <summary>Mouse-look pan/tilt.</summary>
        public Task LookAsync(float deltaX, float deltaY)
            => _disposed ? Task.CompletedTask : _controller.LookAsync(deltaX, deltaY, CancellationToken.None);

        /// <summary>Fire (auto-turret). Self-refuses on non-turret cameras.</summary>
        public Task ShootAsync()
            => _disposed ? Task.CompletedTask : _controller.ShootAsync(CancellationToken.None);

        /// <summary>Reload (auto-turret). Self-refuses on non-turret cameras.</summary>
        public Task ReloadAsync()
            => _disposed ? Task.CompletedTask : _controller.ReloadAsync(CancellationToken.None);

        /// <summary>Zoom (PTZ camera). Self-refuses on non-PTZ cameras.</summary>
        public Task ZoomAsync()
            => _disposed ? Task.CompletedTask : _controller.ZoomAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _controller.OnFrameReceived -= OnFrameReceived;
            _controller.OnKeepAliveFailed -= OnKeepAliveFailed;

            try { await _controller.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
            try { await _api.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
        }
    }
}
