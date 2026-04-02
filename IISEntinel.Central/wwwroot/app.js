window.logsAutoScroll = function (elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    el.scrollTop = el.scrollHeight;
};