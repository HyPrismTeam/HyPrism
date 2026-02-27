'use strict';

/**
 * ElectronNET startup hook — IPv6 loopback socket bridge
 *
 * This file is loaded by main.js via the ElectronNET.Core custom_main.js hook
 * mechanism before app.on('ready') fires, so the patch is in the Node.js module
 * cache when startSocketApiBridge() calls require('http').createServer()
 */
module.exports = {
    onStartup(_host) {
        const http = require('http');
        const _origCreate = http.createServer;

        http.createServer = function (...args) {
            const server = _origCreate.apply(http, args);
            const _origListen = server.listen.bind(server);

            server.listen = function (port, host, ...rest) {
                if (host === 'localhost' || host === '127.0.0.1') {
                    host = '::1';
                }
                return _origListen(port, host, ...rest);
            };

            return server;
        };
    },
};
