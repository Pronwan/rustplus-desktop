# app.py
from flask import Flask, request, send_file, jsonify, abort
import os, hmac, hashlib, time, json
from pathlib import Path
from functools import wraps

# --- CONFIG ---
DATA_DIR = Path("/var/lib/rustplus-overlays")   # Speicherort (muss existieren, chmod 750)
SHARED_SECRET = b"23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde" # Byte-Secret (setze hier ein langes random)
MAX_UPLOAD_BYTES = 350 * 1024   # 350 KB
MAX_UPLOADS_PER_MIN = 5         # 5 Uploads / Minute pro SteamID
CLEANUP_DAYS = 35               # Dateien älter als X Tage werden gelöscht
# ----------------

app = Flask(__name__)
DATA_DIR.mkdir(parents=True, exist_ok=True)

# simple persistent rate-store file (atomic-ish)
RATE_FILE = DATA_DIR / "rate_store.json"
if RATE_FILE.exists():
    try:
        _rate_store = json.loads(RATE_FILE.read_text())
    except:
        _rate_store = {}
else:
    _rate_store = {}

def save_rate_store():
    try:
        RATE_FILE.write_text(json.dumps(_rate_store))
    except Exception:
        pass

def require_sig(f):
    """Decorator: expect headers:
       X-SteamId, X-ServerKey, X-Nonce, X-Signature (hex)
       signature = HMAC_SHA256(shared_secret, steamId|serverKey|nonce|body)
    """
    @wraps(f)
    def wrapper(*args, **kwargs):
        steam = request.headers.get("X-SteamId")
        server_key = request.headers.get("X-ServerKey")
        nonce = request.headers.get("X-Nonce")
        sig = request.headers.get("X-Signature")
        if not (steam and server_key and nonce and sig):
            abort(400, "missing auth headers")

        # Check nonce timeliness (allow 5 min)
        try:
            nval = int(nonce)
        except:
            abort(400, "bad nonce")
        now = int(time.time())
        if abs(now - nval) > 300:
            abort(400, "nonce expired")

        # Compute HMAC
        body = request.get_data() or b""
        msg = steam.encode("utf-8") + b"|" + server_key.encode("utf-8") + b"|" + nonce.encode("utf-8") + b"|"
        msg += body
        expected = hmac.new(SHARED_SECRET, msg, digestmod=hashlib.sha256).hexdigest()
        if not hmac.compare_digest(expected, sig):
            abort(403, "bad signature")

        # attach validated values for view
        request._validated_steam = steam
        request._validated_server = server_key
        return f(*args, **kwargs)
    return wrapper

def rate_allow(steamid):
    """Simple per-steamid rate limiter using timestamp queue in _rate_store"""
    now = int(time.time())
    arr = _rate_store.get(steamid, [])
    # remove older than 60s
    arr = [t for t in arr if now - t < 60]
    if len(arr) >= MAX_UPLOADS_PER_MIN:
        return False
    arr.append(now)
    _rate_store[steamid] = arr
    save_rate_store()
    return True

@app.route("/overlay/<server_key>/<steamid>", methods=["PUT"])
@require_sig
def put_overlay(server_key, steamid):
    # header validation already done by decorator
    body = request.get_data() or b""
    if len(body) > MAX_UPLOAD_BYTES:
        abort(413, "payload too large")

    # rate limiting
    if not rate_allow(steamid):
        abort(429, "rate limit exceeded")

    # create folder per server
    target_dir = DATA_DIR / server_key
    target_dir.mkdir(parents=True, exist_ok=True)
    # atomic-ish write
    tmp = target_dir / f"{steamid}.json.tmp"
    final = target_dir / f"{steamid}.json"
    with tmp.open("wb") as f:
        f.write(body)
    tmp.replace(final)
    return jsonify({"ok": True, "size": len(body)}), 201

@app.route("/overlay/<server_key>/<steamid>", methods=["GET"])
def get_overlay(server_key, steamid):
    target = DATA_DIR / server_key / f"{steamid}.json"
    if not target.exists():
        return ("", 204)  # no content
    # return application/json
    return send_file(str(target), mimetype="application/json", as_attachment=False)

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"ok": True, "server": True})

# Optional: admin endpoint to list server folders (restrict via firewall / nginx auth)
# not exposed by default

if __name__ == "__main__":
    # For quick dev (not recommended in prod)
    app.run(host="0.0.0.0", port=8080)