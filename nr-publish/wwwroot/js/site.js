async function apiPost(url, redirectTo) {
    const resp = await fetch(url, { method: 'POST' });
    if (resp.ok) {
        location.href = redirectTo || location.href;
    } else {
        try {
            const data = await resp.json();
            alert('Error: ' + (data.error || resp.statusText));
        } catch {
            alert('Error: ' + resp.statusText);
        }
    }
}
