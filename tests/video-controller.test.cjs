const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const source = fs.readFileSync(require("node:path").join(__dirname, "../Browser/VideoController.js"), "utf8");

function media(overrides = {}) {
  return {
    paused: true, ended: false, currentTime: 0, duration: 120, isConnected: true,
    seekable: { length: 1, start: () => 0, end: () => 120 },
    getBoundingClientRect: () => ({ width: 640, height: 360 }),
    closest: () => null,
    async play() { this.paused = false; },
    pause() { this.paused = true; },
    ...overrides
  };
}

function setup(videos, nodes = []) {
  const messages = [];
  const document = { querySelectorAll: selector => selector === "*" ? nodes : videos };
  const window = {
    getComputedStyle: () => ({ display: "block", visibility: "visible" }),
    chrome: { webview: { postMessage: result => messages.push(result) } }
  };
  const context = vm.createContext({ window, document });
  vm.runInContext(source, context);
  return { execute: command => window.__biliRemote.execute({ requestId: "test", ...command }), messages };
}

test("toggle preserves current position, stop pauses and returns to start", async () => {
  const video = media({ currentTime: 40 });
  const { execute, messages } = setup([video]);
  assert.equal((await execute({ action: "toggle" })).paused, false);
  assert.equal((await execute({ action: "toggle" })).paused, true);
  assert.equal(video.currentTime, 40);
  assert.equal((await execute({ action: "stop" })).currentTime, 0);
  assert.equal(video.paused, true);
  assert.equal(messages.at(-1).type, "bili-remote-result");
  assert.equal(messages.at(-1).requestId, "test");
});

test("seek bounds and adjustable steps", async () => {
  const video = media({ currentTime: 115 });
  const { execute } = setup([video]);
  assert.equal((await execute({ action: "forward", seconds: 30 })).currentTime, 120);
  assert.equal((await execute({ action: "backward", seconds: 5 })).currentTime, 115);
  video.currentTime = 2;
  assert.equal((await execute({ action: "backward", seconds: 30 })).currentTime, 0);
  assert.equal((await execute({ action: "forward", seconds: -999 })).currentTime, 10);
});

test("main B站 player wins over playing preview thumbnails", async () => {
  const preview = media({ paused: false });
  const main = media({ closest: () => ({ id: "bilibili-player" }) });
  await setup([preview, main]).execute({ action: "toggle" });
  assert.equal(main.paused, false);
  assert.equal(preview.paused, false);
});

test("supports bwp-video and media nested inside open shadow roots", async () => {
  const video = media();
  const shadowRoot = { querySelectorAll: selector => selector === "*" ? [] : [video] };
  const { execute } = setup([], [{ shadowRoot }]);
  assert.equal((await execute({ action: "forward" })).currentTime, 10);
});

test("live ranges and discontinuities do not seek outside buffered window", async () => {
  const video = media({
    currentTime: 150, duration: Infinity,
    seekable: { length: 2, start: i => [100, 180][i], end: i => [150, 240][i] }
  });
  const { execute } = setup([video]);
  assert.equal((await execute({ action: "forward", seconds: 10 })).currentTime, 150);
  assert.equal((await execute({ action: "forward", seconds: 30 })).currentTime, 180);
  assert.equal((await execute({ action: "stop" })).currentTime, 100);
});

test("not yet seekable and missing videos produce helpful results", async () => {
  assert.equal((await setup([]).execute({ action: "toggle" })).code, "no-video");
  const video = media({ seekable: { length: 0 } });
  const { execute } = setup([video]);
  assert.equal((await execute({ action: "forward" })).code, "not-seekable");
  assert.equal((await execute({ action: "stop" })).ok, true);
  assert.equal(video.paused, true);
});

test("autoplay errors and unknown actions do not escape into the host", async () => {
  const { execute } = setup([media({ async play() { throw { name: "NotAllowedError" }; } })]);
  const rejected = await execute({ action: "toggle" });
  assert.equal(rejected.ok, false);
  assert.match(rejected.message, /点击播放一次/);
  assert.equal((await execute({ action: "unknown" })).code, "invalid-action");
});
