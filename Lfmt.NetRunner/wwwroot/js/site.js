async function apiPost(url) {
    const resp = await fetch(url, { method: 'POST' });
    if (resp.ok) {
        location.reload();
    } else {
        const text = await resp.text();
        alert('Error: ' + text);
    }
}
