const t = (key) => chrome.i18n.getMessage(key);
const STATES = {
  connected: ['var(--ok)', 'connected'],
  waiting: ['var(--warn)', 'waiting'],
  denied: ['var(--bad)', 'denied'],
  offline: ['var(--muted)', 'offline'],
};

document.documentElement.lang = chrome.i18n.getUILanguage().startsWith('sk') ? 'sk' : 'en';
document.getElementById('retry').textContent = t('retry');
document.getElementById('footer').textContent = t('footer');

async function render() {
  const { status = 'offline' } = await chrome.storage.session.get('status');
  const [color, key] = STATES[status] ?? STATES.offline;
  document.getElementById('dot').style.background = color;
  document.getElementById('title').textContent = t(key + 'Title');
  document.getElementById('text').textContent = t(key + 'Text');
  document.getElementById('retry').hidden = status === 'connected' || status === 'waiting';
}

document.getElementById('retry').addEventListener('click', () => {
  chrome.runtime.sendMessage('retry');
  setTimeout(render, 600);
});
chrome.storage.session.onChanged.addListener(render);
render();
