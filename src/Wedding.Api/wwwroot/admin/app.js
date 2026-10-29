const api = {
  async request(path, options = {}) {
    const opts = { ...options, credentials: 'same-origin' };
    if (opts.body && typeof opts.body !== 'string' && !(opts.body instanceof FormData)) {
      opts.body = JSON.stringify(opts.body);
      opts.headers = { ...(opts.headers || {}), 'Content-Type': 'application/json' };
    }
    const resp = await fetch(path, opts);
    if (resp.status === 401) {
      if (!location.pathname.endsWith('/login.html')) location.href = 'login.html';
      throw new Error('Не авторизован');
    }
    if (!resp.ok) {
      let msg = 'Ошибка запроса';
      try { const d = await resp.json(); if (d && d.error) msg = d.error; } catch {}
      throw new Error(msg);
    }
    const ct = resp.headers.get('content-type') || '';
    return ct.includes('application/json') ? resp.json() : resp.text();
  },
  get(p) { return this.request(p); },
  post(p, body) { return this.request(p, { method: 'POST', body }); },
  put(p, body) { return this.request(p, { method: 'PUT', body }); },
  del(p) { return this.request(p, { method: 'DELETE' }); },
};

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function fmtDate(d) {
  if (!d) return '—';
  const dt = new Date(d);
  return dt.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function renderNav(active) {
  const items = [
    ['index.html', 'Дашборд'],
    ['guests.html', 'Гости'],
    ['events.html', 'События'],
    ['questions.html', 'Вопросы'],
    ['answers.html', 'Ответы'],
    ['template.html', 'Шаблон'],
  ];
  const nav = document.getElementById('nav');
  nav.innerHTML = `<a class="brand" href="index.html">Свадьба</a>` +
    items.map(([href, label]) =>
      `<a href="${href}" class="${active === label ? 'active' : ''}">${label}</a>`).join('') +
    `<span class="spacer"></span><button id="logout-btn">Выйти</button>`;
  document.getElementById('logout-btn').addEventListener('click', async () => {
    await api.post('/api/admin/logout', {});
    location.href = 'login.html';
  });
}

async function requireAuth() {
  try {
    const me = await api.get('/api/admin/me');
    if (!me.authenticated) { location.href = 'login.html'; return false; }
  } catch { return false; }
  return true;
}

function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }

function showErr(el, msg) {
  if (el) el.textContent = msg || '';
}

function downloadBlob(url, filename) {
  fetch(url, { credentials: 'same-origin' })
    .then(r => r.blob())
    .then(blob => {
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = filename;
      a.click();
      URL.revokeObjectURL(a.href);
    });
}

// Копирование в буфер с запасным способом: Clipboard API работает только
// по HTTPS или на localhost, поэтому на http-сервере используем execCommand.
async function copyText(text) {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch { /* переходим к запасному способу */ }
  try {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '-1000px';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    const ok = document.execCommand('copy');
    document.body.removeChild(ta);
    return ok;
  } catch {
    return false;
  }
}
