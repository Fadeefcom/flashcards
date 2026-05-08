window.recallCraft = {
  audio: null,
  audioUrl: null,
  get: key => localStorage.getItem(key),
  set: (key, value) => localStorage.setItem(key, value),
  remove: key => localStorage.removeItem(key),
  online: () => navigator.onLine,
  primeAudio: () => {
    if (!window.recallCraft.audio) {
      window.recallCraft.audio = new Audio();
      window.recallCraft.audio.preload = 'auto';
      window.recallCraft.audio.playsInline = true;
    }

    const audio = window.recallCraft.audio;
    if (!audio.src) {
      audio.src = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAESsAACJWAAACABAAZGF0YQAAAAA=';
    }

    const promise = audio.play();
    if (promise && typeof promise.then === 'function') {
      return promise
        .then(() => {
          audio.pause();
          audio.currentTime = 0;
        })
        .catch(() => {});
    }
  },
  observeInfiniteScroll: (element, dotnetRef) => {
    if (!element) return null;
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        dotnetRef.invokeMethodAsync('LoadMoreCards');
      }
    }, { rootMargin: '240px' });
    observer.observe(element);
    return {
      dispose: () => observer.disconnect()
    };
  },
  playDataUrl: dataUrl => {
    if (!window.recallCraft.audio) {
      window.recallCraft.audio = new Audio();
      window.recallCraft.audio.preload = 'auto';
      window.recallCraft.audio.playsInline = true;
    }

    if (window.recallCraft.audioUrl) {
      URL.revokeObjectURL(window.recallCraft.audioUrl);
      window.recallCraft.audioUrl = null;
    }

    const [header, base64] = dataUrl.split(',');
    const mimeMatch = /data:([^;]+)/.exec(header || '');
    const mime = mimeMatch ? mimeMatch[1] : 'audio/mpeg';
    const binary = atob(base64 || '');
    const bytes = new Uint8Array(binary.length);

    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }

    window.recallCraft.audioUrl = URL.createObjectURL(new Blob([bytes], { type: mime }));
    window.recallCraft.audio.pause();
    try {
      window.recallCraft.audio.currentTime = 0;
    } catch {
      // iOS can throw when replacing a just-created media source.
    }
    window.recallCraft.audio.src = window.recallCraft.audioUrl;

    return window.recallCraft.audio.play();
  }
};
