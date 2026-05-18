function getTheme() {
    return localStorage.getItem('vx-theme') ||
        (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
}
function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('vx-theme', theme);
}
function toggleTheme() {
    var current = document.documentElement.getAttribute('data-theme') || 'light';
    applyTheme(current === 'dark' ? 'light' : 'dark');
}
function getLanguage() {
    return localStorage.getItem('vx-lang') || (navigator.language || 'vi').split('-')[0];
}
function setLanguage(lang) {
    localStorage.setItem('vx-lang', lang);
    location.reload();
}
applyTheme(getTheme());
