export class AudioService {
  private static popAudio: HTMLAudioElement | null = null;
  private static wrongKeyAudio: HTMLAudioElement | null = null;

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
}
