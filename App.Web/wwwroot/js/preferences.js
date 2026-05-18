function getTheme() {
    return localStorage.getItem('vx-theme') ||
        (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
}

function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('vx-theme', theme);
}

function getLanguage() {
    return localStorage.getItem('vx-lang') ||
        (navigator.language || 'en').split('-')[0];
}

function setLanguage(lang) {
    localStorage.setItem('vx-lang', lang);
}

applyTheme(getTheme());
