// HomeFocus browser extension: reports the domain of the active tab to the HomeFocus app on this PC.
// Only the host name is sent (never the full URL, page title or content), and only to 127.0.0.1.

const ENDPOINT = 'ws://127.0.0.1:47615/homefocus';
const PING_MS = 20000; // also keeps the MV3 service worker alive while connected

let ws = null;
let ready = false;
let pingTimer = null;

const browserName = (() => {
  const brands = (navigator.userAgentData?.brands || []).map((b) => b.brand);
  if (brands.some((b) => /Edge/i.test(b))) return 'edge';
  if (brands.some((b) => /Opera/i.test(b))) return 'opera';
  if (brands.some((b) => /Brave/i.test(b)) || navigator.brave) return 'brave';
  if (brands.some((b) => /Vivaldi/i.test(b))) return 'vivaldi';
  return 'chrome';
})();

function setStatus(status) {
  chrome.storage.session.set({ status });
  const badge = { connected: '', waiting: '…', denied: '!', offline: '' }[status] ?? '';
  chrome.action.setBadgeText({ text: badge });
  chrome.action.setBadgeBackgroundColor({ color: status === 'denied' ? '#DC2626' : '#2F5BEA' });
}

function domainOf(url) {
  try {
    const u = new URL(url);
    if (u.protocol !== 'http:' && u.protocol !== 'https:') return null;
    return u.hostname.replace(/^www\./, '').toLowerCase();
  } catch {
    return null;
  }
}

async function currentTab() {
  try {
    const win = await chrome.windows.getLastFocused();
    const [tab] = await chrome.tabs.query({ active: true, windowId: win.id });
    // Private windows are never reported.
    const domain = tab && !tab.incognito ? domainOf(tab.url) : null;
    return { domain, focused: !!win.focused };
  } catch {
    return { domain: null, focused: false };
  }
}

async function report() {
  if (!ready || ws?.readyState !== WebSocket.OPEN) return;
  const { domain, focused } = await currentTab();
  ws.send(JSON.stringify({ type: 'tab', domain, focused }));
}

async function connect() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
  ready = false;
  try {
    ws = new WebSocket(ENDPOINT);
  } catch {
    setStatus('offline');
    return;
  }

  ws.onopen = async () => {
    const { token } = await chrome.storage.local.get('token');
    setStatus(token ? 'connected' : 'waiting');
    ws.send(JSON.stringify({ type: 'hello', browser: browserName, token: token || null, version: chrome.runtime.getManifest().version }));
  };

  ws.onmessage = async (event) => {
    let msg;
    try { msg = JSON.parse(event.data); } catch { return; }
    if (msg.type === 'paired' && typeof msg.token === 'string') {
      await chrome.storage.local.set({ token: msg.token });
      ready = true;
    } else if (msg.type === 'ready') {
      ready = true;
    } else if (msg.type === 'denied') {
      await chrome.storage.local.remove('token');
      setStatus('denied');
      ws.close();
      return;
    }
    if (ready) {
      setStatus('connected');
      clearInterval(pingTimer);
      pingTimer = setInterval(report, PING_MS);
      report();
    }
  };

  ws.onclose = () => {
    ready = false;
    clearInterval(pingTimer);
    chrome.storage.session.get('status').then(({ status }) => { if (status !== 'denied') setStatus('offline'); });
  };
  ws.onerror = () => {};
}

// Re-check once a minute: reconnects after the app starts, or wakes the worker up.
chrome.alarms.create('homefocus', { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener(() => (ready ? report() : connect()));

chrome.tabs.onActivated.addListener(report);
chrome.tabs.onUpdated.addListener((_id, info, tab) => { if (tab.active && (info.url || info.status === 'complete')) report(); });
chrome.windows.onFocusChanged.addListener(report);
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
chrome.runtime.onMessage.addListener((msg) => { if (msg === 'retry') { setStatus('offline'); connect(); } });

connect();
