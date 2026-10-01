const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../ChzzkDownloader/Services/RPlaySessionService.cs'), 'utf8');
const script = source.match(/ReadScript = """([\s\S]*?)""";/)[1];
function read(account, getters = {}, vue3 = true) {
    const store = { state: { AccountModule: account }, getters };
    const root = vue3 ? { __vue_app__: { config: { globalProperties: { $store: store } } } } : { __vue__: { $store: store } };
    const result = vm.runInNewContext(script, {
        document: { querySelector: () => root, querySelectorAll: () => [] },
        navigator: { userAgent: 'Synthetic Browser' },
        window: { localStorage: { getItem() { throw Error('Must not read stale storage'); }, setItem() { throw Error('Must not write storage'); } } }
    });
    return JSON.parse(JSON.stringify(result));
}
const account = { token: 'current-account-token', loginType: 'google', userInfo: { oid: '0123456789abcdef01234567' } };
assert.equal(read(account).token, account.token);
assert.equal(read(account, {}, false).token, account.token);
assert.equal(read(account, { 'AccountModule/currentUserToken': 'refreshed-token' }).token, 'refreshed-token');
assert.equal(read(account, { 'AccountModule/currentUserLoginType': '' }).token, '');
assert.equal(read({ ...account, loginType: '' }).token, '');
assert.equal(vm.runInNewContext(script, { document: { querySelector: () => null, querySelectorAll: () => [] } }), null);
console.log('RPlay session script: 6 regression checks passed.');
