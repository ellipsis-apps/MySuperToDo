window.backup = {
    generateEncryptedSeed: async function(password) {
        if (!password) throw 'Password required';

        const enc = new TextEncoder();
        const salt = crypto.getRandomValues(new Uint8Array(16));
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const seed = crypto.getRandomValues(new Uint8Array(32));

        const base64 = (buf) => {
            let binary = '';
            const bytes = new Uint8Array(buf);
            const len = bytes.byteLength;
            for (let i = 0; i < len; i++) binary += String.fromCharCode(bytes[i]);
            return btoa(binary);
        };

        const keyMaterial = await crypto.subtle.importKey(
            'raw', enc.encode(password), { name: 'PBKDF2' }, false, ['deriveKey']
        );

        const key = await crypto.subtle.deriveKey(
            { name: 'PBKDF2', salt: salt, iterations: 100000, hash: 'SHA-256' },
            keyMaterial,
            { name: 'AES-GCM', length: 256 },
            false,
            ['encrypt']
        );

        const cipher = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, key, seed);

        const out = {
            v: 1,
            cipher: base64(cipher),
            iv: base64(iv),
            salt: base64(salt),
            seedLength: seed.byteLength
        };

        return JSON.stringify(out);
    }
};
