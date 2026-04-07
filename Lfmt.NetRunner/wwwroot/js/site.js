async function apiPost(url, redirectTo) {
    const resp = await fetch(url, { method: 'POST' });
    if (resp.ok) {
        location.href = redirectTo || location.href;
    } else {
        const text = await resp.text();
        alert('Error: ' + text);
    }
}
