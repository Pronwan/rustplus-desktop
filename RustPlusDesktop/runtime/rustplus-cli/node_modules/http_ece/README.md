# encrypted-content-encoding

A simple implementation of the [HTTP encrypted
content-encoding](https://tools.ietf.org/html/rfc8188)

# Use

```js
var ece = require('http_ece');
var crypto = require('crypto')

var parameters = {
  key: crypto.randomBytes(16).toString('base64url'),
  salt: crypto.randomBytes(16).toString('base64url')
};
var encrypted = ece.encrypt(data, parameters);

var decrypted = ece.decrypt(encrypted, parameters);

require('assert').equal(decrypted.compare(data), 0);
```

This also supports the static-ephemeral ECDH mode.  The source explains how.

# TODO

Use the node streams API instead of the legacy APIs.
