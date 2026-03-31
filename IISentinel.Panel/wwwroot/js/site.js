window.logsAutoScroll = () => {
    const el = document.getElementById("logsConsole");
    if (!el) return;

    const isNearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 50;

    // 🔥 comportamiento inteligente
    if (isNearBottom) {
        el.scrollTop = el.scrollHeight; // sigue abajo
    }
};