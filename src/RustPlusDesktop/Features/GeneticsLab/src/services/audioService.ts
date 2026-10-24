export class AudioService {
  private static audioCtx: AudioContext | null = null;
  private static popBuffer: AudioBuffer | null = null;
  private static wrongKeyAudio: HTMLAudioElement | null = null;
  private static isPreloading = false;
  private static lastPopTime = 0;
  private static lastDuplicateTime = 0;
  private static keepAliveOsc: OscillatorNode | null = null;

  public static getCtx(): AudioContext | null {
    const Ctx = typeof window !== 'undefined'
      ? (window.AudioContext || (window as any).webkitAudioContext)
      : undefined;
    if (!Ctx) return null;
    if (!this.audioCtx) {
      this.audioCtx = new Ctx();
    }
    return this.audioCtx;
  }

  /**
   * Continuous silent keep-alive node prevents Chromium from suspending AudioContext
   * when running in the background behind Rust.
   */
  public static startKeepAlive(): void {
    try {
      const ctx = this.getCtx();
      if (!ctx) return;
      if (ctx.state === 'suspended') {
        ctx.resume().catch(() => {});
      }
      if (!this.keepAliveOsc) {
        const gain = ctx.createGain();
        gain.gain.value = 0; // Pure zero amplitude silence
        gain.connect(ctx.destination);

        const osc = ctx.createOscillator();
        osc.frequency.value = 440;
        osc.connect(gain);
        osc.start();

        this.keepAliveOsc = osc;
      }
    } catch {
      // ignore
    }
  }

  /**
   * Pre-fetches and decodes pop.mp3 into raw PCM AudioBuffer.
   * Trims leading silence/encoder padding so playback begins on the exact initial transient sample.
   */
  public static async preload(): Promise<void> {
    if (this.popBuffer || this.isPreloading) return;
    this.isPreloading = true;

    try {
      const ctx = this.getCtx();
      if (!ctx) return;
      this.startKeepAlive();
      if (ctx.state === 'suspended') {
        ctx.resume().catch(() => {});
      }
      const res = await fetch('./audio/pop.mp3');
      if (res.ok) {
        const arrayBuf = await res.arrayBuffer();
        const decoded = await ctx.decodeAudioData(arrayBuf);
        
        // Trim leading silent samples introduced by MP3 encoder delay (~25ms - 50ms)
        let startSample = 0;
        const channel0 = decoded.getChannelData(0);
        for (let i = 0; i < Math.min(channel0.length, 4800); i++) {
          if (Math.abs(channel0[i]) > 0.015) {
            startSample = Math.max(0, i - 16);
            break;
          }
        }

        if (startSample > 0 && startSample < decoded.length) {
          const trimmed = ctx.createBuffer(
            decoded.numberOfChannels,
            decoded.length - startSample,
            decoded.sampleRate
          );
          for (let ch = 0; ch < decoded.numberOfChannels; ch++) {
            trimmed.copyToChannel(decoded.getChannelData(ch).subarray(startSample), ch);
          }
          this.popBuffer = trimmed;
        } else {
          this.popBuffer = decoded;
        }
      }
    } catch {
      // Fallback synthesizer handles playback if fetch fails
    }
  }

  private static getWrongKeyAudio(): HTMLAudioElement {
    if (!this.wrongKeyAudio && typeof Audio !== 'undefined') {
      this.wrongKeyAudio = new Audio('./audio/headshot.mp3');
    }
    return this.wrongKeyAudio!;
  }

  /**
   * Ultra-low latency pop sound (< 1ms hardware latency).
   * Plays polyphonically on top of other cues without mutual blocking.
   */
  public static playPop(enabled = true): void {
    if (!enabled) return;
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    if (now - this.lastPopTime < 15) return;
    this.lastPopTime = now;

    try {
      this.startKeepAlive();
      const ctx = this.getCtx();
      if (!ctx) return;
      if (ctx.state === 'suspended') {
        ctx.resume().then(() => this.triggerPop(ctx)).catch(() => {});
        return;
      }
      this.triggerPop(ctx);
    } catch {
      // ignore
    }
  }

  private static triggerPop(ctx: AudioContext): void {
    if (this.popBuffer) {
      const source = ctx.createBufferSource();
      source.buffer = this.popBuffer;
      source.connect(ctx.destination);
      source.start(0);
      return;
    }

    // 0ms synthesized "bubble pop" fallback
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    const audioTime = ctx.currentTime;

    osc.type = 'sine';
    osc.frequency.setValueAtTime(400, audioTime);
    osc.frequency.exponentialRampToValueAtTime(950, audioTime + 0.035);
    osc.frequency.exponentialRampToValueAtTime(550, audioTime + 0.07);

    gain.gain.setValueAtTime(0.32, audioTime);
    gain.gain.exponentialRampToValueAtTime(0.001, audioTime + 0.08);

    osc.connect(gain);
    gain.connect(ctx.destination);

    osc.start(audioTime);
    osc.stop(audioTime + 0.09);
  }

  public static playWrongKey(enabled = true): void {
    if (!enabled) return;
    try {
      const audio = this.getWrongKeyAudio();
      if (audio) {
        audio.currentTime = 0;
        audio.play().catch(() => {});
      }
    } catch {
      // ignore
    }
  }

  /**
   * Distinct, synthesized "duplicate" cue — a short two-note descending blip.
   * Plays polyphonically on top of other cues without mutual blocking.
   */
  public static playDuplicate(enabled = true): void {
    if (!enabled) return;
    const nowMs = typeof performance !== 'undefined' ? performance.now() : Date.now();
    if (nowMs - this.lastDuplicateTime < 25) return;
    this.lastDuplicateTime = nowMs;

    try {
      this.startKeepAlive();
      const ctx = this.getCtx();
      if (!ctx) return;
      if (ctx.state === 'suspended') {
        ctx.resume().then(() => this.triggerDuplicate(ctx)).catch(() => {});
        return;
      }
      this.triggerDuplicate(ctx);
    } catch {
      // ignore (autoplay policy / unsupported)
    }
  }

  private static triggerDuplicate(ctx: AudioContext): void {
    const now = ctx.currentTime;
    const notes: Array<[number, number]> = [[740, 0], [560, 0.11]]; // freq (Hz), start offset (s)
    for (const [freq, offset] of notes) {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'triangle';
      osc.frequency.value = freq;
      const start = now + offset;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.22, start + 0.012);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.1);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start(start);
      osc.stop(start + 0.11);
    }
  }
}

if (typeof window !== 'undefined') {
  const unlockAudio = () => {
    AudioService.startKeepAlive();
    AudioService.preload();
  };
  ['click', 'keydown', 'touchstart', 'focus'].forEach(evt => {
    window.addEventListener(evt, unlockAudio, { passive: true });
  });
}
