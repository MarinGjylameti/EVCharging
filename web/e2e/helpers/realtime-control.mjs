// Injected into the E2E browser context only. No production hooks are introduced.
export function installRealtimeControl() {
  const NativeEventSource = window.EventSource;
  const streams = new Set();
  let paused = false;
  class E2EEventSource extends NativeEventSource {
    constructor(url, options) {
      super(url, options);
      if (new URL(String(url), location.href).pathname === '/api/driver/events') {
        streams.add(this);
        if (paused) setTimeout(() => { this.dispatchEvent(new Event('error')); this.close(); }, 0);
      }
    }
  }
  window.EventSource = E2EEventSource;
  window.__e2eSetRealtimePaused = value => {
    paused = value;
    if (paused) for (const stream of streams) {
      stream.dispatchEvent(new Event('error'));
      stream.close();
    }
    else window.dispatchEvent(new Event('online')); // Existing product recovery/snapshot logic.
  };
}
