'use strict';
/*
 * Encrypted content coding
 *
 * === Note about versions ===
 *
 * This code supports multiple versions of the draft.  This is selected using
 * the |version| parameter.
 *
 * aes128gcm: The most recent version, the salt, record size and key identifier
 *    are included in a header that is part of the encrypted content coding.
 *
 * aesgcm: The version that is widely deployed with WebPush (as of 2016-11).
 *    This version is selected by default, unless you specify a |padSize| of 1.
 */

var crypto = require('crypto');

var AES_GCM = 'aes-128-gcm';
var PAD_SIZE = { 'aes128gcm': 1, 'aesgcm': 2 };
var TAG_LENGTH = 16;
var KEY_LENGTH = 16;
var NONCE_LENGTH = 12;
var SHA_256_LENGTH = 32;
var MODE_ENCRYPT = 'encrypt';
var MODE_DECRYPT = 'decrypt';

var keylog;
if (process.env.ECE_KEYLOG === '1') {
  keylog = function(m, k) {
    console.warn(m + ' [' + k.length + ']: ' + k.toString('base64url'));
    return k;
  };
} else {
  keylog = function(m, k) { return k; };
}

/* Optionally base64 decode something. */
function decode(b) {
  if (typeof b === 'string') {
    return Buffer.from(b, 'base64url');
  }
  return b;
}

function HMAC_hash(key, input) {
  var hmac = crypto.createHmac('sha256', key);
  hmac.update(input);
  return hmac.digest();
}

/* HKDF as defined in RFC5869, using SHA-256 */
function HKDF_extract(salt, ikm) {
  keylog('salt', salt);
  keylog('ikm', ikm);
  return keylog('extract', HMAC_hash(salt, ikm));
}

function HKDF_expand(prk, info, l) {
  keylog('prk', prk);
  keylog('info', info);
  var output = Buffer.alloc(0);
  var T = Buffer.alloc(0);
  info = Buffer.from(info, 'ascii');
  var counter = 0;
  var cbuf = Buffer.alloc(1);
  while (output.length < l) {
    cbuf.writeUIntBE(++counter, 0, 1);
    T = HMAC_hash(prk, Buffer.concat([T, info, cbuf]));
    output = Buffer.concat([output, T]);
  }

  return keylog('expand', output.slice(0, l));
}

function HKDF(salt, ikm, info, len) {
  return HKDF_expand(HKDF_extract(salt, ikm), info, len);
}

function info(base, context) {
  var result = Buffer.concat([
    Buffer.from('Content-Encoding: ' + base + '\0', 'ascii'),
    context
  ]);
  keylog('info ' + base, result);
  return result;
}

function lengthPrefix(buffer) {
  var b = Buffer.concat([Buffer.alloc(2), buffer]);
  b.writeUIntBE(buffer.length, 0, 2);
  return b;
}

function extractDH(header, mode) {
  var key = header.privateKey;
  var senderPubKey, receiverPubKey;
  if (mode === MODE_ENCRYPT) {
    senderPubKey = key.getPublicKey();
    receiverPubKey = header.dh;
  } else if (mode === MODE_DECRYPT) {
    senderPubKey = header.dh;
    receiverPubKey = key.getPublicKey();
  } else {
    throw new Error('Unknown mode only ' + MODE_ENCRYPT +
                    ' and ' + MODE_DECRYPT + ' supported');
  }
  return {
    secret: key.computeSecret(header.dh),
    context: Buffer.concat([
      Buffer.from(header.keylabel, 'ascii'),
      Buffer.from([0]),
      lengthPrefix(receiverPubKey), // user agent
      lengthPrefix(senderPubKey)    // application server
    ])
  };
}

function extractSecretAndContext(header, mode) {
  var result = { secret: null, context: Buffer.alloc(0) };
  if (header.key) {
    result.secret = header.key;
    if (result.secret.length !== KEY_LENGTH) {
      throw new Error('An explicit key must be ' + KEY_LENGTH + ' bytes');
    }
  } else if (header.dh) { // receiver/decrypt
    result = extractDH(header, mode);
  } else if (typeof header.keyid !== undefined) {
    result.secret = header.keymap[header.keyid];
  }
  if (!result.secret) {
    throw new Error('Unable to determine key');
  }
  keylog('secret', result.secret);
  keylog('context', result.context);
  if (header.authSecret) {
    result.secret = HKDF(header.authSecret, result.secret,
                         info('auth', Buffer.alloc(0)), SHA_256_LENGTH);
    keylog('authsecret', result.secret);
  }
  return result;
}

function webpushSecret(header, mode) {
  if (!header.authSecret) {
    throw new Error('No authentication secret for webpush');
  }
  keylog('authsecret', header.authSecret);

  var remotePubKey, senderPubKey, receiverPubKey;
  if (mode === MODE_ENCRYPT) {
    senderPubKey = header.privateKey.getPublicKey();
    remotePubKey = receiverPubKey = header.dh;
  } else if (mode === MODE_DECRYPT) {
    remotePubKey = senderPubKey = header.keyid;
    receiverPubKey = header.privateKey.getPublicKey();
  } else {
    throw new Error('Unknown mode only ' + MODE_ENCRYPT +
                    ' and ' + MODE_DECRYPT + ' supported');
  }
  keylog('remote pubkey', remotePubKey);
  keylog('sender pubkey', senderPubKey);
  keylog('receiver pubkey', receiverPubKey);
  return keylog('secret dh',
                HKDF(header.authSecret,
                     header.privateKey.computeSecret(remotePubKey),
                     Buffer.concat([
                       Buffer.from('WebPush: info\0'),
                       receiverPubKey,
                       senderPubKey
                     ]),
                     SHA_256_LENGTH));
}

function extractSecret(header, mode, keyLookupCallback) {
  if (keyLookupCallback) {
    if (!isFunction(keyLookupCallback)) {
      throw new Error('Callback is not a function')
    }
  }

  if (header.key) {
    if (header.key.length !== KEY_LENGTH) {
      throw new Error('An explicit key must be ' + KEY_LENGTH + ' bytes');
    }
    return keylog('secret key', header.key);
  }

  if (!header.privateKey) {
    // Lookup based on keyid
    if (!keyLookupCallback) {
      var key = header.keymap && header.keymap[header.keyid];
    } else {
      var key = keyLookupCallback(header.keyid)
    }
    if (!key) {
      throw new Error('No saved key (keyid: "' + header.keyid + '")');
    }
    return key;
  }

  return webpushSecret(header, mode);
}

function deriveKeyAndNonce(header, mode, lookupKeyCallback) {
  if (!header.salt) {
    throw new Error('must include a salt parameter for ' + header.version);
  }
  var keyInfo;
  var nonceInfo;
  var secret;
  if (header.version === 'aesgcm') {
    // old
    var s = extractSecretAndContext(header, mode, lookupKeyCallback);
    keyInfo = info('aesgcm', s.context);
    nonceInfo = info('nonce', s.context);
    secret = s.secret;
  } else if (header.version === 'aes128gcm') {
    // latest
    keyInfo = Buffer.from('Content-Encoding: aes128gcm\0');
    nonceInfo = Buffer.from('Content-Encoding: nonce\0');
    secret = extractSecret(header, mode, lookupKeyCallback);
  } else {
    throw new Error('Unable to set context for mode ' + header.version);
  }
  var prk = HKDF_extract(header.salt, secret);
  var result = {
    key: HKDF_expand(prk, keyInfo, KEY_LENGTH),
    nonce: HKDF_expand(prk, nonceInfo, NONCE_LENGTH)
  };
  keylog('key', result.key);
  keylog('nonce base', result.nonce);
  return result;
}

/* Parse command-line arguments. */
function parseParams(params) {
  var header = {};

  header.version = params.version || 'aes128gcm';
  header.rs = parseInt(params.rs, 10);
  if (isNaN(header.rs)) {
    header.rs = 4096;
  }
  var overhead = PAD_SIZE[header.version];
  if (header.version === 'aes128gcm') {
    overhead += TAG_LENGTH;
  }
  if (header.rs <= overhead) {
    throw new Error('The rs parameter has to be greater than ' + overhead);
  }

  if (params.salt) {
    header.salt = decode(params.salt);
    if (header.salt.length !== KEY_LENGTH) {
      throw new Error('The salt parameter must be ' + KEY_LENGTH + ' bytes');
    }
  }
  header.keyid = params.keyid;
  if (params.key) {
    header.key = decode(params.key);
  } else {
    header.privateKey = params.privateKey;
    if (!header.privateKey) {
      header.keymap = params.keymap;
    }
    if (header.version !== 'aes128gcm') {
      header.keylabel = params.keylabel || 'P-256';
    }
    if (params.dh) {
      header.dh = decode(params.dh);
    }
  }
  if (params.authSecret) {
    header.authSecret = decode(params.authSecret);
  }
  return header;
}

function generateNonce(base, counter) {
  var nonce = Buffer.from(base);
  var m = nonce.readUIntBE(nonce.length - 6, 6);
  var x = ((m ^ counter) & 0xffffff) +
      ((((m / 0x1000000) ^ (counter / 0x1000000)) & 0xffffff) * 0x1000000);
  nonce.writeUIntBE(x, nonce.length - 6, 6);
  keylog('nonce' + counter, nonce);
  return nonce;
}

/* Used when decrypting aes128gcm to populate the header values. Modifies the
 * header values in place and returns the size of the header. */
function readHeader(buffer, header) {
  var idsz = buffer.readUIntBE(20, 1);
  header.salt = buffer.slice(0, KEY_LENGTH);
  header.rs = buffer.readUIntBE(KEY_LENGTH, 4);
  header.keyid = buffer.slice(21, 21 + idsz);
  return 21 + idsz;
}

function unpadLegacy(data, version) {
  var padSize = PAD_SIZE[version];
  var pad = data.readUIntBE(0, padSize);
  if (pad + padSize > data.length) {
    throw new Error('padding exceeds block size');
  }
  keylog('padding', data.slice(0, padSize + pad));
  var padCheck = Buffer.alloc(pad);
  padCheck.fill(0);
  if (padCheck.compare(data.slice(padSize, padSize + pad)) !== 0) {
    throw new Error('invalid padding');
  }
  return data.slice(padSize + pad);
}

function unpad(data, last) {
  var i = data.length - 1;
  while(i >= 0) {
    if (data[i]) {
      if (last) {
        if (data[i] !== 2) {
          throw new Error('last record needs to start padding with a 2');
        }
      } else {
        if (data[i] !== 1) {
          throw new Error('last record needs to start padding with a 2');
        }
      }
      return data.slice(0, i);
    }
    --i;
  }
  throw new Error('all zero plaintext');
}

function decryptRecord(key, counter, buffer, header, last) {
  keylog('decrypt', buffer);
  var nonce = generateNonce(key.nonce, counter);
  var gcm = crypto.createDecipheriv(AES_GCM, key.key, nonce);
  gcm.setAuthTag(buffer.slice(buffer.length - TAG_LENGTH));
  var data = gcm.update(buffer.slice(0, buffer.length - TAG_LENGTH));
  data = Buffer.concat([data, gcm.final()]);
  keylog('decrypted', data);
  if (header.version !== 'aes128gcm') {
    return unpadLegacy(data, header.version);
  }
  return unpad(data, last);
}

/**
 * Decrypt some bytes.  This uses the parameters to determine the key and block
 * size, which are described in the draft.  Binary values are base64url encoded.
 *
 * |params.version| contains the version of encoding to use: aes128gcm is the latest,
 * but aesgcm is also accepted (though the latter might
 * disappear in a future release).  If omitted, assume aes128gcm.
 *
 * If |params.key| is specified, that value is used as the key.
 *
 * If the version is aes128gcm, the keyid is extracted from the header and used
 * as the ECDH public key of the sender.  For version aesgcm ,
 * |params.dh| needs to be provided with the public key of the sender.
 *
 * The |params.privateKey| includes the private key of the receiver.
 */
function decrypt(buffer, params, keyLookupCallback) {
  var header = parseParams(params);
  if (header.version === 'aes128gcm') {
    var headerLength = readHeader(buffer, header);
    buffer = buffer.slice(headerLength);
  }
  var key = deriveKeyAndNonce(header, MODE_DECRYPT, keyLookupCallback);
  var start = 0;
  var result = Buffer.alloc(0);

  var chunkSize = header.rs;
  if (header.version !== 'aes128gcm') {
    chunkSize += TAG_LENGTH;
  }

  for (var i = 0; start < buffer.length; ++i) {
    var end = start + chunkSize;
    if (header.version !== 'aes128gcm' && end === buffer.length) {
      throw new Error('Truncated payload');
    }
    end = Math.min(end, buffer.length);
    if (end - start <= TAG_LENGTH) {
      throw new Error('Invalid block: too small at ' + i);
    }
    var block = decryptRecord(key, i, buffer.slice(start, end),
                              header, end >= buffer.length);
    result = Buffer.concat([result, block]);
    start = end;
  }
  return result;
}

function encryptRecord(key, counter, buffer, pad, header, last) {
  keylog('encrypt', buffer);
  pad = pad || 0;
  var nonce = generateNonce(key.nonce, counter);
  var gcm = crypto.createCipheriv(AES_GCM, key.key, nonce);

  var ciphertext = [];
  var padSize = PAD_SIZE[header.version];
  var padding = Buffer.alloc(pad + padSize);
  padding.fill(0);

  if (header.version !== 'aes128gcm') {
    padding.writeUIntBE(pad, 0, padSize);
    keylog('padding', padding);
    ciphertext.push(gcm.update(padding));
    ciphertext.push(gcm.update(buffer));

    if (!last && padding.length + buffer.length < header.rs) {
      throw new Error('Unable to pad to record size');
    }
  } else {
    ciphertext.push(gcm.update(buffer));
    padding.writeUIntBE(last ? 2 : 1, 0, 1);
    keylog('padding', padding);
    ciphertext.push(gcm.update(padding));
  }

  gcm.final();
  var tag = gcm.getAuthTag();
  if (tag.length !== TAG_LENGTH) {
    throw new Error('invalid tag generated');
  }
  ciphertext.push(tag);
  return keylog('encrypted', Buffer.concat(ciphertext));
}

function writeHeader(header) {
  var ints = Buffer.alloc(5);
  var keyid = Buffer.from(header.keyid || []);
  if (keyid.length > 255) {
    throw new Error('keyid is too large');
  }
  ints.writeUIntBE(header.rs, 0, 4);
  ints.writeUIntBE(keyid.length, 4, 1);
  return Buffer.concat([header.salt, ints, keyid]);
}

/**
 * Encrypt some bytes.  This uses the parameters to determine the key and block
 * size, which are described in the draft.
 *
 * |params.version| contains the version of encoding to use: aes128gcm is the latest,
 * but aesgcm is also accepted (though the latter two might
 * disappear in a future release).  If omitted, assume aes128gcm.
 *
 * If |params.key| is specified, that value is used as the key.
 *
 * For Diffie-Hellman (WebPush), |params.dh| includes the public key of the
 * receiver.  |params.privateKey| is used to establish a shared secret.  Key
 * pairs can be created using |crypto.createECDH()|.
 */
function encrypt(buffer, params, keyLookupCallback) {  
  if (!Buffer.isBuffer(buffer)) {
    throw new Error('buffer argument must be a Buffer');
  }
  var header = parseParams(params);
  if (!header.salt) {
    header.salt = crypto.randomBytes(KEY_LENGTH);
  }

  var result;
  if (header.version === 'aes128gcm') {
    // Save the DH public key in the header unless keyid is set.
    if (header.privateKey && !header.keyid) {
      header.keyid = header.privateKey.getPublicKey();
    }
    result = writeHeader(header);
  } else {
    // No header on other versions
    result = Buffer.alloc(0);
  }

  var key = deriveKeyAndNonce(header, MODE_ENCRYPT, keyLookupCallback);
  var start = 0;
  var padSize = PAD_SIZE[header.version];
  var overhead = padSize;
  if (header.version === 'aes128gcm') {
    overhead += TAG_LENGTH;
  }
  var pad = isNaN(parseInt(params.pad, 10)) ? 0 : parseInt(params.pad, 10);

  var counter = 0;
  var last = false;
  while (!last) {
    // Pad so that at least one data byte is in a block.
    var recordPad = Math.min(header.rs - overhead - 1, pad);
    if (header.version !== 'aes128gcm') {
      recordPad = Math.min((1 << (padSize * 8)) - 1, recordPad);
    }
    if (pad > 0 && recordPad === 0) {
      ++recordPad; // Deal with perverse case of rs=overhead+1 with padding.
    }
    pad -= recordPad;

    var end = start + header.rs - overhead - recordPad;
    if (header.version !== 'aes128gcm') {
      // The > here ensures that we write out a padding-only block at the end
      // of a buffer.
      last = end > buffer.length;
    } else {
      last = end >= buffer.length;
    }
    last = last && pad <= 0;
    var block = encryptRecord(key, counter, buffer.slice(start, end),
                              recordPad, header, last);
    result = Buffer.concat([result, block]);

    start = end;
    ++counter;
  }
  return result;
}


function isFunction(object) {
  return typeof(object) === 'function';
 }

module.exports = {
  decrypt: decrypt,
  encrypt: encrypt
};
