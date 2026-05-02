window.recallCraft = {
  get: key => localStorage.getItem(key),
  set: (key, value) => localStorage.setItem(key, value),
  remove: key => localStorage.removeItem(key),
  online: () => navigator.onLine,
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
    const audio = new Audio(dataUrl);
    return audio.play();
  }
};
