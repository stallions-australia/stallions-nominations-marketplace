// Saves text the app fetched with the signed-in user's token (a plain link can't send the token).
window.stallionsDownload = {
    text: function (fileName, contentType, text) {
        // A byte-order mark so Excel reads the UTF-8 text (em dashes, accents) correctly.
        // HttpContent.ReadAsStringAsync strips the BOM the server writes, so this adds exactly one.
        const blob = new Blob([String.fromCharCode(0xFEFF), text], { type: contentType + ';charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }
};
