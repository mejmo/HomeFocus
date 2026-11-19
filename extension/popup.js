const STATES = {
  connected: ['var(--ok)', 'Connected', 'Websites in your active tab are counted in HomeFocus.'],
  waiting: ['var(--warn)', 'Waiting for approval', 'Confirm the connection in the HomeFocus window on your PC.'],
  denied: ['var(--bad)', 'Connection declined', 'HomeFocus declined this browser. You can ask again in a few minutes.'],
  offline: ['var(--muted)', 'HomeFocus is not running', 'Start HomeFocus on this PC; the extension connects by itself.'],
};

async function render() {
  const { status = 'offline' } = await chrome.storage.session.get('status');
  const [color, title, text] = STATES[status] ?? STATES.offline;
  document.getElementById('dot').style.background = color;
  document.getElementById('title').textContent = title;
  document.getElementById('text').textContent = text;
  document.getElementById('retry').hidden = status === 'connected' || status === 'waiting';
}

document.getElementById('retry').addEventListener('click', () => {
  chrome.runtime.sendMessage('retry');
  setTimeout(render, 600);
});
chrome.storage.session.onChanged.addListener(render);
render();
