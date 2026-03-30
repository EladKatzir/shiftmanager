/**
 * Centralized logger for ShiftManager.
 * - Logger.log()   → only outputs when debug mode is enabled (?debug=1 or Logger.enable())
 * - Logger.warn()  → always outputs (operational warnings should never be silenced)
 * - Logger.error() → always outputs (errors should never be silenced)
 *
 * Usage: Logger.log('ModuleName', 'message', data);
 * Enable: Logger.enable()  or  add ?debug=1 to URL
 * Disable: Logger.disable()
 */
window.Logger = (function() {
    var enabled = (location.search.indexOf('debug=1') !== -1)
        || (localStorage.getItem('shifty_debug') === '1');

    function makeArgs(module, args) {
        return ['[' + module + ']'].concat(Array.prototype.slice.call(args, 1));
    }

    return {
        log: function(module) {
            if (enabled) console.log.apply(console, makeArgs(module, arguments));
        },
        warn: function(module) {
            console.warn.apply(console, makeArgs(module, arguments));
        },
        error: function(module) {
            console.error.apply(console, makeArgs(module, arguments));
        },
        enable: function() {
            localStorage.setItem('shifty_debug', '1');
            enabled = true;
            console.log('[Logger] Debug logging enabled');
        },
        disable: function() {
            localStorage.removeItem('shifty_debug');
            enabled = false;
            console.log('[Logger] Debug logging disabled');
        }
    };
})();
