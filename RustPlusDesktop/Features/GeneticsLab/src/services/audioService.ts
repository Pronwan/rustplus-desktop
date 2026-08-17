export class AudioService {
  private static popAudio: HTMLAudioElement | null = null;
  private static wrongKeyAudio: HTMLAudioElement | null = null;
  private static audioCtx: AudioContext | null = null;

  private static getCtx(): AudioContext | null {
    const Ctx = typeof window !== 'undefined'
      ? (window.AudioContext || (window as any).webkitAudioContext)
      : undefined;
    if (!Ctx) return null;
    if (!this.audioCtx) this.audioCtx = new Ctx();
    return this.audioCtx;
  }

  private static getPopAudio(): HTMLAudioElement {
    if (!this.popAudio && typeof Audio !== 'undefined') {
      this.popAudio = new Audio('./audio/pop.mp3');
    }
    return this.popAudio!;
  }

  private static getWrongKeyAudio(): HTMLAudioElement {
    if (!this.wrongKeyAudio && typeof Audio !== 'undefined') {
      this.wrongKeyAudio = new Audio('./audio/headshot.mp3');
    }
    return this.wrongKeyAudio!;
  }

  public static playPop(enabled = true): void {
    if (!enabled) return;
    try {
      const audio = this.getPopAudio();
      if (audio) {
        audio.currentTime = 0;
        audio.play().catch(() => {});
      }
    } catch {
      // ignore audio play errors (e.g. user interaction policy)
    }
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
   * Generated with the Web Audio API so it needs no asset file and stays clearly
   * different from the pop (new clone) and headshot (wrong key) sounds.
   */
  public static playDuplicate(enabled = true): void {
    if (!enabled) return;
    try {
      const ctx = this.getCtx();
      if (!ctx) return;
      if (ctx.state === 'suspended') ctx.resume().catch(() => {});

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
    } catch {
      // ignore (autoplay policy / unsupported)
    }
  }
}
