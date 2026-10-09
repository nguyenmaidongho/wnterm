(async function () {
    let settings = {
        copyOnSelect: true,
        rightClickAction: 'Paste',
        confirmMultilinePaste: false
    };

    // Cầu nối HTTP (Android/iOS): POST lên /__msg theo lô, nhận bằng SSE /__events.
    const bridgeParams = new URLSearchParams(location.search);
    const httpBridge = bridgeParams.get("bridge") === "http";
    const bridgeCh = bridgeParams.get("ch") || "";
    let outbox = [];
    let sending = false;
    async function flushOutbox() {
        if (sending) return;
        sending = true;
        try {
            while (outbox.length) {
                const batch = outbox;
                outbox = [];
                try {
                    await fetch("/__msg?ch=" + bridgeCh, { method: "POST", body: JSON.stringify(batch) });
                } catch (e) { }
            }
        } finally { sending = false; }
    }

    function post(msg) {
        if (httpBridge) { outbox.push(msg); flushOutbox(); return; }
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(msg);
        } else if (window.invokeCSharpAction) {
            window.invokeCSharpAction(JSON.stringify(msg));
        }
    }

    try {
        if (document.fonts && document.fonts.load) {
            await Promise.all([
                document.fonts.load("14px 'JetBrains Mono'"),
                document.fonts.load("bold 14px 'JetBrains Mono'")
            ]);
        }
        if (document.fonts) {
            await document.fonts.ready;
        }
    } catch (e) {
        console.warn('Font load error:', e);
    }

    const TerminalClass = window.Terminal?.Terminal || window.Terminal;
    const term = new TerminalClass({
        fontFamily: "'JetBrains Mono', Consolas, 'Courier New', monospace",
        fontSize: 14,
        lineHeight: 1.0,
        letterSpacing: 0,
        scrollback: 10000,
        cursorBlink: true,
        allowProposedApi: true,
        customGlyphs: true,
        rescaleOverlappingGlyphs: true,
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
            const oldCols = term.cols;
            const oldRows = term.rows;
            fitAddon.fit();
            if (term.cols !== oldCols || term.rows !== oldRows) {
                post({ type: 'resize', cols: term.cols, rows: term.rows });
            }
        }, 50);
    }

    const resizeObserver = new ResizeObserver(() => {
        notifyResize();
    });
    resizeObserver.observe(container);
    window.addEventListener('resize', notifyResize);

    if (document.fonts) {
        document.fonts.ready.then(() => {
            term.refresh(0, term.rows - 1);
            const oldCols = term.cols;
            const oldRows = term.rows;
            fitAddon.fit();
            if (term.cols !== oldCols || term.rows !== oldRows) {
                post({ type: 'resize', cols: term.cols, rows: term.rows });
            }
        });
    }

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

        if (e.ctrlKey && e.shiftKey && (e.key === 'S' || e.key === 's')) {
            e.preventDefault();
            post({ type: 'hotkey', name: 'Snippets' });
            return false;
        }

        if (e.altKey && e.key >= '1' && e.key <= '9') {
            post({ type: 'hotkey', name: 'Tab' + e.key });
            return false;
        }

        // preventDefault: để host đổi cỡ chữ, không để WebView tự phóng to cả trang.
        if (e.ctrlKey && (e.key === '=' || e.key === '+')) {
            e.preventDefault();
            post({ type: 'hotkey', name: 'ZoomIn' });
            return false;
        }

        if (e.ctrlKey && (e.key === '-' || e.key === '_')) {
            e.preventDefault();
            post({ type: 'hotkey', name: 'ZoomOut' });
            return false;
        }

        if (e.ctrlKey && e.key === '0') {
            e.preventDefault();
            post({ type: 'hotkey', name: 'ZoomReset' });
            return false;
        }

        return true;
    });

    // Dán gốc (menu/phím của WebView, bàn phím điện thoại): văn bản nhiều dòng → gửi host hỏi xác nhận trước.
    // Chỉ bật khi host gửi confirmMultilinePaste (bản WPF không gửi → không đổi hành vi).
    container.addEventListener('paste', (e) => {
        if (!settings.confirmMultilinePaste) return;
        const text = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
        if (!text || !/[\r\n]/.test(text)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        post({ type: 'pasteText', text: text });
    }, true);

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

    const onHostMessage = (msg) => {
        {
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
                    let changed = false;
                    if (msg.fontSize && term.options.fontSize !== msg.fontSize) {
                        term.options.fontSize = msg.fontSize;
                        changed = true;
                    }
                    if (msg.fontFamily) {
                        const font = msg.fontFamily.includes(',') ? msg.fontFamily : `'${msg.fontFamily}', Consolas, 'Courier New', monospace`;
                        if (term.options.fontFamily !== font) {
                            term.options.fontFamily = font;
                            changed = true;
                        }
                    }
                    if (msg.theme) term.options.theme = msg.theme;
                    if (msg.scrollback) term.options.scrollback = msg.scrollback;
                    if (msg.copyOnSelect !== undefined) settings.copyOnSelect = msg.copyOnSelect;
                    if (msg.rightClickAction) settings.rightClickAction = msg.rightClickAction;
                    if (msg.confirmMultilinePaste !== undefined) settings.confirmMultilinePaste = msg.confirmMultilinePaste;
                    fitAddon.fit();
                    if (changed) {
                        post({ type: 'resize', cols: term.cols, rows: term.rows });
                    }
                    break;
                case 'selectAll':
                    term.selectAll();
                    break;
                case 'clear':
                    term.clear();
                    break;
                case 'focus':
                    term.focus();
                    const oldCols = term.cols;
                    const oldRows = term.rows;
                    fitAddon.fit();
                    if (term.cols !== oldCols || term.rows !== oldRows) {
                        post({ type: 'resize', cols: term.cols, rows: term.rows });
                    }
                    break;
            }
        }
    };

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (e) => onHostMessage(e.data));
    }
    // Avalonia WebView: host gọi window.__wntermReceive(jsonString)
    window.__wntermReceive = (json) => { try { onHostMessage(JSON.parse(json)); } catch { } };

    if (httpBridge) {
        const es = new EventSource("/__events?ch=" + bridgeCh);
        es.onmessage = (e) => { try { onHostMessage(JSON.parse(e.data)); } catch (err) { } };
    }

    post({ type: 'ready', cols: term.cols, rows: term.rows });
})();
