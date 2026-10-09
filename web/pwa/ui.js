/* WN Term PWA — tiện ích giao diện dùng chung: icon, lưu trữ, thông báo, hộp thoại. */
(function (global) {
  'use strict';
  var L = global.WNi18n.t;
  var $ = function (id) { return document.getElementById(id); };

  // ===== Icon (cùng bộ đường vẽ với app, khung 16x16) =====
  var ICONS = {
    play: 'M 3 2 L 13 8 L 3 14 Z',
    plus: 'M 8 2 L 8 14 M 2 8 L 14 8',
    cloud: 'M 4.5 13 A 3 3 0 0 1 4.5 7 A 4 4 0 0 1 12 6.5 A 3 3 0 0 1 12 13 Z',
    search: 'M 7 12 A 5 5 0 1 0 7 2 A 5 5 0 0 0 7 12 Z M 10.6 10.6 L 14.5 14.5',
    settings: 'M 1.5 4 L 14.5 4 M 1.5 8 L 14.5 8 M 1.5 12 L 14.5 12 M 9 2.5 L 9 5.5 M 5 6.5 L 5 9.5 M 11 10.5 L 11 13.5',
    close: 'M 3.5 3.5 L 12.5 12.5 M 12.5 3.5 L 3.5 12.5',
    chevron: 'M 3 5.5 L 8 10.5 L 13 5.5',
    monitor: 'M 2 2.5 L 14 2.5 L 14 10.5 L 2 10.5 Z M 5.5 13.5 L 10.5 13.5 M 8 10.5 L 8 13.5',
    folder: 'M 1.5 4 L 6 4 L 7.5 5.5 L 14.5 5.5 L 14.5 13 L 1.5 13 Z',
    file: 'M 3.5 1.5 L 9.5 1.5 L 12.5 4.5 L 12.5 14.5 L 3.5 14.5 Z M 9.5 1.5 L 9.5 4.5 L 12.5 4.5',
    refresh: 'M 13 8 A 5 5 0 1 1 11.5 4.5 M 13 2.5 L 13 5.5 L 10 5.5',
    up: 'M 8 14 L 8 3 M 3.5 7 L 8 2.5 L 12.5 7',
    down: 'M 8 2 L 8 13 M 3.5 8.5 L 8 13 L 12.5 8.5',
    download: 'M 8 2 L 8 11 M 4 7.5 L 8 11.5 L 12 7.5 M 2.5 14 L 13.5 14',
    upload: 'M 8 11.5 L 8 2.5 M 4 6.5 L 8 2.5 L 12 6.5 M 2.5 14 L 13.5 14',
    export: 'M 8 10 L 8 2 M 4.5 5.5 L 8 2 L 11.5 5.5 M 2.5 14 L 13.5 14',
    import: 'M 8 2 L 8 10 M 4.5 6.5 L 8 10 L 11.5 6.5 M 2.5 14 L 13.5 14',
    trash: 'M 2.5 4 L 13.5 4 M 6 4 L 6 2 L 10 2 L 10 4 M 4 4 L 4.8 14 L 11.2 14 L 12 4',
    home: 'M 2 8 L 8 2.5 L 14 8 M 3.5 7 L 3.5 13.5 L 12.5 13.5 L 12.5 7',
    terminal: 'M 2 3 L 14 3 L 14 13 L 2 13 Z M 4.5 6 L 7 8 L 4.5 10 M 8.5 10.5 L 11.5 10.5',
    newfolder: 'M 1.5 4 L 6 4 L 7.5 5.5 L 14.5 5.5 L 14.5 13 L 1.5 13 Z M 8 7.5 L 8 11 M 6.3 9.25 L 9.7 9.25',
    edit: 'M 2.5 13.5 L 3 10.5 L 11 2.5 L 13.5 5 L 5.5 13 Z M 9.5 4 L 12 6.5',
    key: 'M 10 9.5 A 3.5 3.5 0 1 1 10 2.5 A 3.5 3.5 0 0 1 10 9.5 Z M 7.5 7.5 L 2 13 M 4 11 L 5.5 12.5',
    copy: 'M 5.5 5.5 L 13.5 5.5 L 13.5 13.5 L 5.5 13.5 Z M 2.5 10.5 L 2.5 2.5 L 10.5 2.5',
    logout: 'M 6 2.5 L 2.5 2.5 L 2.5 13.5 L 6 13.5 M 10.5 5 L 13.5 8 L 10.5 11 M 13.5 8 L 6 8',
    more: 'M 3 8 L 3 8.01 M 8 8 L 8 8.01 M 13 8 L 13 8.01',
    font: 'M 2 13.5 L 6 2.5 L 10 13.5 M 3.5 9.5 L 8.5 9.5 M 11 13.5 L 14.5 13.5',
    check: 'M 3 8.5 L 6.5 12 L 13 4.5',
    eye: 'M 1 8 C 3 4.5 5.5 3.5 8 3.5 C 10.5 3.5 13 4.5 15 8 C 13 11.5 10.5 12.5 8 12.5 C 5.5 12.5 3 11.5 1 8 Z M 8 10 A 2 2 0 1 0 8 6 A 2 2 0 0 0 8 10 Z',
    pin: 'M 8 1.2 L 9.9 5.4 L 14.4 5.9 L 11 9 L 12 13.5 L 8 11.1 L 4 13.5 L 5 9 L 1.6 5.9 L 6.1 5.4 Z',
    tag: 'M 2 2 L 8.5 2 L 14 7.5 L 8.5 13 L 3 7.5 Z M 5 5 L 5 5.01',
    history: 'M 8 14.5 A 6.5 6.5 0 1 0 1.5 8 M 1.5 3.5 L 1.5 8 L 6 8 M 8 4.5 L 8 8 L 10.5 9.5',
    cpu: 'M 4 4 L 12 4 L 12 12 L 4 12 Z M 6.5 6.5 L 9.5 6.5 L 9.5 9.5 L 6.5 9.5 Z M 6 1.5 L 6 4 M 10 1.5 L 10 4 M 6 12 L 6 14.5 M 10 12 L 10 14.5 M 1.5 6 L 4 6 M 1.5 10 L 4 10 M 12 6 L 14.5 6 M 12 10 L 14.5 10',
    ram: 'M 1.5 5 L 14.5 5 L 14.5 11 L 1.5 11 Z M 4 11 L 4 13 M 7 11 L 7 13 M 10 11 L 10 13 M 13 11 L 13 13 M 4 7 L 4 9 M 7 7 L 7 9 M 10 7 L 10 9',
    clock: 'M 8 14.5 A 6.5 6.5 0 1 0 8 1.5 A 6.5 6.5 0 0 0 8 14.5 Z M 8 4.5 L 8 8 L 10.5 9.5',
    user: 'M 8 8 A 3 3 0 1 0 8 2 A 3 3 0 0 0 8 8 Z M 2.5 14.5 A 5.5 5 0 0 1 13.5 14.5',
    disk: 'M 2 4 A 6 2 0 1 0 14 4 A 6 2 0 0 0 2 4 Z M 2 4 L 2 12 A 6 2 0 0 0 14 12 L 14 4 M 2 8 A 6 2 0 0 0 14 8',
    link: 'M 6.5 9.5 L 9.5 6.5 M 7 4.5 L 8 3.5 A 2.5 2.5 0 0 1 11.5 7 L 10.5 8 M 9 11.5 L 8 12.5 A 2.5 2.5 0 0 1 4.5 9 L 5.5 8',
    snippet: 'M 5.5 4 L 2 8 L 5.5 12 M 10.5 4 L 14 8 L 10.5 12 M 9 3 L 7 13',
    shield: 'M 8 1.5 L 13.5 3.5 L 13.5 8 C 13.5 11 11 13.5 8 14.5 C 5 13.5 2.5 11 2.5 8 L 2.5 3.5 Z M 5.5 8 L 7.2 9.7 L 10.5 6.3'
  };
  function svg(name) { return '<svg viewBox="0 0 16 16"><path d="' + (ICONS[name] || '') + '"/></svg>'; }
  function ico(name, cls) { return '<span data-ico="' + name + '"' + (cls ? ' class="' + cls + '"' : '') + '>' + svg(name) + '</span>'; }
  function hydrate(root) { (root || document).querySelectorAll('[data-ico]').forEach(function (e) { if (!e.firstChild) e.innerHTML = svg(e.dataset.ico); }); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  // ===== Lưu trữ cục bộ =====
  var store = {
    get: function (k, d) { try { var v = localStorage.getItem('wnweb.' + k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
    set: function (k, v) { try { localStorage.setItem('wnweb.' + k, JSON.stringify(v)); } catch (e) {} },
    del: function (k) { try { localStorage.removeItem('wnweb.' + k); } catch (e) {} }
  };

  // ===== Thông báo ngắn =====
  var toastTimer;
  function toast(msg, ms) {
    var t = $('toast'); t.textContent = msg; t.hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(function () { t.hidden = true; }, ms || 2500);
  }

  // ===== Hộp thoại =====
  var modalQueue = Promise.resolve();

  /**
   * modal({ title, html, menu:[{text,value,icon,cls}], fields:[...], buttons:[{text,value,cls,onClick,validate}],
   *         stack, wide, cancelValue, validate(vals)→string|null|Promise, onOpen(ctx) })
   * fields: { id, label, type:'text|password|number|textarea|checkbox|radio|select', value, placeholder, hint, rows, mono, options:[{value,label}] }
   * → Promise<{ value, fields }>. Hộp thoại xếp hàng: cái sau chỉ hiện khi cái trước đã đóng.
   */
  function modal(opts) {
    var run = function () {
      return new Promise(function (resolve) {
        $('modalCard').className = 'modal-card' + (opts.wide ? ' wide' : '') + (opts.cls ? ' ' + opts.cls : '');
        $('modalTitle').textContent = opts.title || '';
        var body = $('modalBody'); body.innerHTML = opts.html || '';
        var err = $('modalErr'), info = $('modalInfo'); err.hidden = true; info.hidden = true;
        var inputs = {}, closed = false;

        function values() {
          var v = {};
          Object.keys(inputs).forEach(function (k) {
            var i = inputs[k];
            if (i.radios) { var c = i.radios.find(function (r) { return r.checked; }); v[k] = c ? c.value : null; }
            else if (i.type === 'checkbox') v[k] = i.checked;
            else v[k] = i.value;
          });
          return v;
        }
        function done(value) { if (closed) return; closed = true; var vals = values(); $('modal').hidden = true; resolve({ value: value, fields: vals }); }
        var ctx = {
          body: body, inputs: inputs, values: values, close: done,
          setError: function (m) { err.textContent = m || ''; err.hidden = !m; if (m) info.hidden = true; },
          setInfo: function (m) { info.textContent = m || ''; info.hidden = !m; if (m) err.hidden = true; }
        };

        if (opts.menu) {
          var list = document.createElement('div'); list.className = 'menu-list';
          opts.menu.forEach(function (m) {
            var b = document.createElement('button'); b.className = 'menu-item' + (m.cls ? ' ' + m.cls : '');
            b.innerHTML = (m.icon ? ico(m.icon) : '') + '<span>' + esc(m.text) + '</span>' + (m.right ? '<em>' + esc(m.right) + '</em>' : '');
            b.onclick = function () { done(m.value); }; list.appendChild(b);
          });
          body.appendChild(list);
        }

        (opts.fields || []).forEach(function (f) {
          var type = f.type || 'text', wrap;
          if (type === 'checkbox') {
            wrap = document.createElement('label'); wrap.className = 'check';
            var cb = document.createElement('input'); cb.type = 'checkbox'; cb.checked = !!f.value; cb.dataset.fid = f.id;
            var sp = document.createElement('span'); sp.textContent = f.label || '';
            wrap.appendChild(cb); wrap.appendChild(sp); inputs[f.id] = cb;
          } else if (type === 'radio') {
            wrap = document.createElement('div'); wrap.className = 'field';
            if (f.label) { var lb = document.createElement('span'); lb.textContent = f.label; wrap.appendChild(lb); }
            var radios = [];
            (f.options || []).forEach(function (o) {
              var l = document.createElement('label'); l.className = 'check';
              var r = document.createElement('input'); r.type = 'radio'; r.name = 'm-' + f.id; r.value = o.value; r.checked = o.value === f.value; r.dataset.fid = f.id;
              var s2 = document.createElement('span'); s2.textContent = o.label;
              l.appendChild(r); l.appendChild(s2); wrap.appendChild(l); radios.push(r);
            });
            inputs[f.id] = { radios: radios };
          } else {
            wrap = document.createElement('label'); wrap.className = 'field';
            if (f.label) { var sp2 = document.createElement('span'); sp2.textContent = f.label; wrap.appendChild(sp2); }
            var i;
            if (type === 'textarea') { i = document.createElement('textarea'); i.rows = f.rows || 4; }
            else if (type === 'select') {
              i = document.createElement('select');
              (f.options || []).forEach(function (o) { var op = document.createElement('option'); op.value = o.value; op.textContent = o.label; i.appendChild(op); });
            } else { i = document.createElement('input'); i.type = type; }
            i.dataset.fid = f.id;
            if (type !== 'select') i.value = f.value == null ? '' : f.value; else i.value = f.value;
            if (type !== 'password') i.autocomplete = 'off';
            i.autocapitalize = 'off'; i.spellcheck = false; i.autocorrect = 'off';
            if (f.placeholder) i.placeholder = f.placeholder;
            if (f.mono) i.classList.add('mono');
            if (f.readonly) i.readOnly = true;
            if (f.inputmode) i.inputMode = f.inputmode;
            if (type === 'password' && f.reveal !== false) {
              var row = document.createElement('div'); row.className = 'pw-row'; row.appendChild(i);
              var eye = document.createElement('button'); eye.type = 'button'; eye.className = 'icon-btn'; eye.innerHTML = ico('eye');
              eye.onclick = function () { i.type = i.type === 'password' ? 'text' : 'password'; };
              row.appendChild(eye); wrap.appendChild(row);
            } else wrap.appendChild(i);
            inputs[f.id] = i;
          }
          if (f.hint) { var h = document.createElement('small'); h.className = 'hint'; h.textContent = f.hint; wrap.appendChild(h); }
          body.appendChild(wrap);
        });

        var btns = $('modalButtons'); btns.innerHTML = '';
        btns.className = 'modal-buttons' + (opts.stack ? ' stack' : '');
        var list2 = opts.buttons || (opts.menu ? [] : [{ text: 'OK', value: true, cls: 'primary' }]);
        list2.forEach(function (b) {
          var e = document.createElement('button'); e.className = 'btn' + (b.cls ? ' ' + b.cls : ''); e.textContent = b.text;
          e.onclick = async function () {
            if (e.disabled) return;
            if (b.onClick) { e.disabled = true; try { await b.onClick(ctx); } finally { e.disabled = false; } return; }
            var check = b.validate !== false && b.value && opts.validate ? opts.validate : null;
            if (check) {
              e.disabled = true;
              var msg; try { msg = await check(values(), ctx); } catch (ex) { msg = ex.message || String(ex); }
              e.disabled = false;
              if (msg) { ctx.setError(msg); return; }
            }
            done(b.value);
          };
          btns.appendChild(e);
        });
        btns.hidden = !btns.children.length;
        $('modalX').onclick = function () { done(opts.cancelValue === undefined ? null : opts.cancelValue); };
        hydrate($('modal'));
        $('modal').hidden = false;
        $('modalCard').scrollTop = 0;
        var first = (opts.fields || []).find(function (f) { return ['text', 'password', 'number', 'textarea'].indexOf(f.type || 'text') >= 0 && !f.noFocus; });
        if (first && !opts.noAutofocus) setTimeout(function () { inputs[first.id].focus(); }, 60);
        Object.keys(inputs).forEach(function (k) {
          var el = inputs[k]; if (!el.addEventListener || el.tagName === 'TEXTAREA') return;
          el.addEventListener('keydown', function (ev) { if (ev.key === 'Enter') { var p = list2.find(function (b) { return b.value && !b.onClick; }); if (p) { ev.preventDefault(); btns.querySelectorAll('button')[list2.indexOf(p)].click(); } } });
        });
        if (opts.onOpen) opts.onOpen(ctx);
      });
    };
    var p = modalQueue.then(run);
    modalQueue = p.catch(function () {});
    return p;
  }

  /** Hộp xác nhận nhanh → true/false. */
  async function confirmBox(title, html, okText, danger) {
    var r = await modal({ title: title, html: html, buttons: [{ text: L('Hủy'), value: false }, { text: okText || 'OK', value: true, cls: danger ? 'danger-solid' : 'primary' }], cancelValue: false });
    return !!r.value;
  }

  global.UI = { $: $, ico: ico, svg: svg, hydrate: hydrate, esc: esc, store: store, toast: toast, modal: modal, confirm: confirmBox };
})(window);
