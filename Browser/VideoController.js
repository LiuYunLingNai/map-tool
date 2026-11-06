/* BiliRemote: installed in each document, including frames, by WebView2. */
(() => {
  "use strict";
  if (window.__biliRemote) return;

  function candidates(root = document, visited = new Set()) {
    if (visited.has(root)) return [];
    visited.add(root);
    const media = [...root.querySelectorAll("video, bwp-video")];
    for (const node of root.querySelectorAll("*")) {
      if (node.shadowRoot) media.push(...candidates(node.shadowRoot, visited));
    }
    return media.filter(v => typeof v.play === "function" && typeof v.pause === "function");
  }

  function selectVideo() {
    return candidates().filter(v => v.isConnected !== false).sort((a, b) => score(b) - score(a))[0];
  }

  function score(video) {
    const rect = video.getBoundingClientRect();
    const style = window.getComputedStyle(video);
    const visible = rect.width > 0 && rect.height > 0 && style.display !== "none" && style.visibility !== "hidden";
    const mainPlayer = Boolean(video.closest("#bilibili-player, #bilibiliPlayer, .bpx-player-container, .bilibili-player, #live-player"));
    return (mainPlayer ? 1e12 : 0) + (visible ? 1e10 : 0) + (!video.paused ? 1e9 : 0) + rect.width * rect.height;
  }

  function state(video) {
    return {
      paused: Boolean(video.paused),
      currentTime: Number.isFinite(video.currentTime) ? video.currentTime : 0,
      duration: Number.isFinite(video.duration) ? video.duration : null
    };
  }

  function seek(video, target) {
    if (!video.seekable?.length) return false;
    // Stay in an actual seekable range (also handles live streams and gaps).
    const ranges = video.seekable;
    let nearest = ranges.start(0);
    let distance = Infinity;
    for (let i = 0; i < ranges.length; i++) {
      const start = ranges.start(i);
      const end = ranges.end(i);
      const value = Math.max(start, Math.min(end, target));
      if (Math.abs(value - target) < distance) {
        nearest = value;
        distance = Math.abs(value - target);
      }
    }
    video.currentTime = nearest;
    return true;
  }

  async function perform(command) {
    const video = selectVideo();
    if (!video) return { ok: false, code: "no-video", message: "当前页面没有视频，请先打开一个 B 站视频。" };
    try {
      const seconds = [5, 10, 30].includes(command.seconds) ? command.seconds : 10;
      let message;
      switch (command.action) {
        case "toggle":
          if (video.paused || video.ended) {
            await video.play();
            message = "已播放";
          } else {
            video.pause();
            message = "已暂停";
          }
          break;
        case "stop":
          video.pause();
          message = seek(video, 0) ? "已停止，回到开头" : "已暂停；视频尚未就绪或当前直播不支持回到开头";
          break;
        case "forward":
        case "backward":
          if (!seek(video, (Number(video.currentTime) || 0) + (command.action === "forward" ? seconds : -seconds))) {
            return { ok: false, code: "not-seekable", message: "视频尚未就绪，或当前直播不支持跳转。", ...state(video) };
          }
          message = command.action === "forward" ? `已前进 ${seconds} 秒` : `已后退 ${seconds} 秒`;
          break;
        case "state":
          message = "视频已就绪";
          break;
        default:
          return { ok: false, code: "invalid-action", message: "未知的播放指令。" };
      }
      return { ok: true, message, ...state(video) };
    } catch (error) {
      return {
        ok: false, code: "media-error",
        message: error?.name === "NotAllowedError"
          ? "请先在网页中点击播放一次，再使用快捷键。"
          : `视频操作失败：${error?.message || String(error)}`
      };
    }
  }

  const api = {
    async execute(command) {
      const result = await perform(command);
      window.chrome?.webview?.postMessage({ type: "bili-remote-result", requestId: command.requestId, ...result });
      return result;
    }
  };
  Object.defineProperty(window, "__biliRemote", { value: api, configurable: false, writable: false });
})();
