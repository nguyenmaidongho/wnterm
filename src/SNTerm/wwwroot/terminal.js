(async function () {
    let settings = {
        copyOnSelect: true,
        rightClickAction: 'Paste'
    };

    function post(msg) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(msg);
        }
    }

    try {
        if (document.fonts && document.fonts.load) {
            await document.fonts.load("14px 'JetBrains Mono'");
        }
    } catch (e) {
        console.warn('Font load error:', e);
    }

    const TerminalClass = window.Terminal?.Terminal || window.Terminal;
    const term = new TerminalClass({
        fontFamily: "'JetBrains Mono', 'Cascadia Mono', Consolas, monospace",
        fontSize: 14,
        scrollback: 10000,
        cursorBlink: true,
        allowProposedApi: true,
        rightClickSelectsWord: false,
        theme: {
            background: '#1e1e1e',
            foreground: '#d4d4d4',
            cursor: '#aeafad',
            selectionBackground: '#264f78'
        }
    });

    const FitClass = window.FitAddon?.FitAddon || window.FitAddon;
    const fitAddon = new FitClass();
    term.loadAddon(fitAddon);

    const UnicodeClass = window.Unicode11Addon?.Unicode11Addon || window.Unicode11Addon;
    if (UnicodeClass) {
        term.loadAddon(new UnicodeClass());
        term.unicode.activeVersion = '11';
    }

    const container = document.getElementById('terminal-container');
    term.open(container);
    fitAddon.fit();
    window.term = term;
    window.fitAddon = fitAddon;

    let resizeTimer = null;
    function notifyResize() {
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            fitAddon.fit();
    window.term = term;
    window.fitAddon = fitAddon;
            post({ type: 'resize', cols: term.cols, rows: term.rows });
        }, 100);
    }

    const resizeObserver = new ResizeObserver(() => {
        notifyResize();
    });
    resizeObserver.observe(container);

    term.onData((data) => {
        post({ type: 'input', data: data });
    });

    term.attachCustomKeyEventHandler((e) => {
        if (e.type !== 'keydown') return true;

        if (e.ctrlKey && e.shiftKey && (e.key === 'C' || e.key === 'c')) {
            const text = term.getSelection();
            if (text) post({ type: 'copy', text: text });
            return false;
        }

        if (e.ctrlKey && e.shiftKey && (e.key === 'V' || e.key === 'v')) {
            post({ type: 'requestPaste' });
            return false;
        }

        if (e.ctrlKey && e.shiftKey && (e.key === 'W' || e.key === 'w')) {
            post({ type: 'hotkey', name: 'CloseTab' });
            return false;
        }

        if (e.ctrlKey && e.key === 'Tab') {
            if (e.shiftKey) {
                post({ type: 'hotkey', name: 'PrevTab' });
            } else {
                post({ type: 'hotkey', name: 'NextTab' });
            }
            return false;
        }

        if (e.altKey && e.key >= '1' && e.key <= '9') {
            post({ type: 'hotkey', name: 'Tab' + e.key });
            return false;
        }

        if (e.ctrlKey && (e.key === '=' || e.key === '+')) {
            post({ type: 'hotkey', name: 'ZoomIn' });
            return false;
        }

        if (e.ctrlKey && (e.key === '-' || e.key === '_')) {
            post({ type: 'hotkey', name: 'ZoomOut' });
            return false;
        }

        if (e.ctrlKey && e.key === '0') {
            post({ type: 'hotkey', name: 'ZoomReset' });
            return false;
        }

        return true;
    });

    if (term.element) {
        term.element.addEventListener('mouseup', (e) => {
            if (e.button !== 0 || !settings.copyOnSelect) return;
            const text = term.getSelection();
            if (text) post({ type: 'copy', text: text });
        });

        term.element.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            const wantPaste = (settings.rightClickAction === 'Paste') !== e.shiftKey;
            if (wantPaste) {
                post({ type: 'requestPaste' });
            } else {
                post({ type: 'contextMenu', hasSelection: term.hasSelection() });
            }
        });
    }

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (e) => {
            const msg = e.data;
            if (!msg || !msg.type) return;

            switch (msg.type) {
                case 'output':
                    if (msg.b64) {
                        const binary = atob(msg.b64);
                        const len = binary.length;
                        const bytes = new Uint8Array(len);
                        for (let i = 0; i < len; i++) {
                            bytes[i] = binary.charCodeAt(i);
                        }
                        term.write(bytes);
                    }
                    break;
                case 'paste':
                    if (msg.text) {
                        term.paste(msg.text);
                    }
                    break;
                case 'status':
                    if (msg.text) {
                        term.writeln(msg.text);
                    }
                    break;
                case 'settings':
                    if (msg.fontSize) term.options.fontSize = msg.fontSize;
                    if (msg.fontFamily) term.options.fontFamily = msg.fontFamily;
                    if (msg.theme) term.options.theme = msg.theme;
                    if (msg.scrollback) term.options.scrollback = msg.scrollback;
                    if (msg.copyOnSelect !== undefined) settings.copyOnSelect = msg.copyOnSelect;
                    if (msg.rightClickAction) settings.rightClickAction = msg.rightClickAction;
                    fitAddon.fit();
    window.term = term;
    window.fitAddon = fitAddon;
                    break;
                case 'selectAll':
                    term.selectAll();
                    break;
                case 'clear':
                    term.clear();
                    break;
                case 'focus':
                    term.focus();
                    fitAddon.fit();
                    break;
            }
        });
    }

    post({ type: 'ready', cols: term.cols, rows: term.rows });
})();


