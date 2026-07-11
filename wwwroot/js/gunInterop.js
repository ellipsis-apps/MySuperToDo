// Simple Gun/SEA interop used by the Blazor registration page.
// This file expects Gun and SEA to be available on window (include gun + sea scripts in index.html).
(function () {
    window.gunInterop = {
        gun: null,
        init: function (relay) {
            if (!window.Gun) {
                throw 'Gun library not found. Make sure you included gun and sea scripts in index.html.';
            }
            this.gun = Gun({ peers: [relay] });
        },

        // Create a new user, authenticate, and write a small profile seed.
        createUser: function (username, password) {
            var self = this;
            return new Promise(function (resolve, reject) {
                if (!self.gun) {
                    return reject('Gun not initialized');
                }

                try {
                    // Create the user account (Gun SEA API)
                    self.gun.user().create(username, password, function (ack) {
                        if (ack && ack.err) {
                            return reject(ack.err);
                        }

                        // Authenticate the newly-created user so we can write their profile
                        self.gun.user().auth(username, password, function (authAck) {
                            if (authAck && authAck.err) {
                                return reject(authAck.err);
                            }

                            // Write a basic profile under the user's space
                            try {
                                self.gun.user().get('profile').put({ username: username, created: Date.now() }, function (putAck) {
                                    if (putAck && putAck.err) {
                                        return reject(putAck.err);
                                    }
                                    resolve({ ok: true });
                                });
                            }
                            catch (e) {
                                reject(e.message || e);
                            }
                        });
                    });
                }
                catch (e) {
                    reject(e.message || e);
                }
            });
        }
    };
})();
