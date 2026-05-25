#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import base64
import hashlib
import hmac
import http.server
import json
import os
import socketserver
import tempfile
import time
import traceback
import urllib.parse


# ============================================================
# CONFIG
# ============================================================



SHARED_SECRET_HEX = os.environ.get(
    "OVERLAY_SHARED_SECRET_HEX",
    "23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde"
)

BASE_DIR = "/home/rust-plus/data"

HOST = "0.0.0.0"
PORT = 5000

MAX_OVERLAY_BYTES = 350 * 1024          # maximale dekodierte Overlay-Gr��e
MAX_POST_BYTES = 650 * 1024             # maximale Request-Body-Gr��e
MAX_AGE_SECONDS = 60 * 60 * 24 * 28     # 28 Tage
REQUEST_TIMEOUT_SECONDS = 30

# Wichtig:
# Dein alter GET-Endpunkt hat sig_client gelesen, aber nicht gepr�ft.
# Falls dein Client bereits eine Fetch-Signatur nach steamId|serverKey|ts baut,
# kannst du das auf True lassen.
# Falls alte Clients sonst kaputtgehen, vor�bergehend auf False setzen.
REQUIRE_FETCH_SIGNATURE = True


# ============================================================
# HELPERS
# ============================================================

def log(msg):
    print("[%s] %s" % (time.strftime("%Y-%m-%d %H:%M:%S"), msg), flush=True)


def hex_to_bytes(h):
    try:
        return bytes.fromhex(h)
    except Exception:
        raise RuntimeError("Invalid SHARED_SECRET_HEX. Must be a valid hex string.")


SHARED_SECRET = hex_to_bytes(SHARED_SECRET_HEX)

# ============================================================
# RESOLVED PATHS CACHE
# ============================================================
RESOLVED_PATHS = {}  # steamId -> (file_path, mtime)


def populate_resolved_paths():
    global RESOLVED_PATHS
    new_paths = {}
    try:
        if os.path.isdir(BASE_DIR):
            for item in os.listdir(BASE_DIR):
                item_path = os.path.join(BASE_DIR, item)
                if os.path.isdir(item_path):
                    for file_name in os.listdir(item_path):
                        if file_name.endswith(".json"):
                            steam_id = file_name[:-5]
                            if steam_id.isdigit():
                                full_path = os.path.join(item_path, file_name)
                                try:
                                    mtime = os.path.getmtime(full_path)
                                    existing = new_paths.get(steam_id)
                                    if not existing or mtime > existing[1]:
                                        new_paths[steam_id] = (full_path, mtime)
                                except Exception:
                                    pass
        RESOLVED_PATHS = new_paths
        log("Scan complete. Loaded %d cached steamIds." % len(RESOLVED_PATHS))
    except Exception as ex:
        log("Error populating resolved paths: %s" % ex)


def ensure_dir(path):
    os.makedirs(path, exist_ok=True)


def server_key_to_path(server_key):
    safe = "".join(ch for ch in server_key if ch.isalnum() or ch in ("-", "_", "."))
    return safe[:160]


def steamid_to_filename(steamid):
    safe = "".join(ch for ch in steamid if ch.isdigit())
    return safe[:32] + ".json"


def is_valid_steamid(steamid):
    return steamid.isdigit() and 10 <= len(steamid) <= 32


def is_valid_server_key(server_key):
    safe = server_key_to_path(server_key)
    return bool(safe) and safe == server_key_to_path(server_key)


def build_upload_sig(steamid, server_key, ts, overlay_b64):
    msg = steamid + "|" + server_key + "|" + ts + "|" + overlay_b64
    return hmac.new(SHARED_SECRET, msg.encode("utf-8"), hashlib.sha256).hexdigest()


def build_fetch_sig(steamid, server_key, ts):
    msg = steamid + "|" + server_key + "|" + ts
    return hmac.new(SHARED_SECRET, msg.encode("utf-8"), hashlib.sha256).hexdigest()


def check_timestamp(ts):
    try:
        ts_int = int(ts)
    except Exception:
        return False, "bad_ts"

    now = int(time.time())
    diff = abs(now - ts_int)

    if diff > MAX_AGE_SECONDS:
        log("timestamp_out_of_range: now=%s ts=%s diff=%s" % (now, ts_int, now - ts_int))
        return False, "timestamp_out_of_range"

    return True, None


def safe_join_overlay_path(server_key, steamid):
    safe_server = server_key_to_path(server_key)
    safe_file = steamid_to_filename(steamid)

    if not safe_server or safe_file == ".json":
        raise ValueError("invalid_path_parts")

    server_dir = os.path.join(BASE_DIR, safe_server)
    file_path = os.path.join(server_dir, safe_file)

    # zus�tzliche Absicherung gegen Path-Tricks
    base_abs = os.path.abspath(BASE_DIR)
    file_abs = os.path.abspath(file_path)

    if not file_abs.startswith(base_abs + os.sep):
        raise ValueError("path_escape_detected")

    return server_dir, file_path


def atomic_write_bytes(file_path, data):
    directory = os.path.dirname(file_path)
    ensure_dir(directory)

    fd, tmp_path = tempfile.mkstemp(prefix=".overlay-", suffix=".tmp", dir=directory)
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())

        os.replace(tmp_path, file_path)
    except Exception:
        try:
            os.remove(tmp_path)
        except Exception:
            pass
        raise


# ============================================================
# HTTP HANDLER
# ============================================================

class OverlayHandler(http.server.BaseHTTPRequestHandler):
    server_version = "RustPlusOverlaySync/1.1"
    protocol_version = "HTTP/1.1"
    def address_string(self):
        return self.client_address[0]

    def setup(self):
        super().setup()
        self.request.settimeout(REQUEST_TIMEOUT_SECONDS)

    def send_json(self, code, obj):
        try:
            body = json.dumps(obj, ensure_ascii=False).encode("utf-8")

            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)
            self.wfile.flush()

        except BrokenPipeError:
            log("client disconnected while sending response")
        except Exception as ex:
            log("send_json failed: %s" % ex)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        try:
            self.handle_get()
        except Exception as ex:
            log("Unhandled exception in GET: %s" % ex)
            log(traceback.format_exc())
            self.send_json(500, {"error": "internal_error"})

    def do_POST(self):
        try:
            self.handle_post()
        except Exception as ex:
            log("Unhandled exception in POST: %s" % ex)
            log(traceback.format_exc())
            self.send_json(500, {"error": "internal_error"})

    def handle_post(self):
        if self.path != "/upload":
            self.send_json(404, {"error": "not_found"})
            return

        # Content-Length sicher lesen
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self.send_json(400, {"error": "bad_content_length"})
            return

        if length <= 0:
            self.send_json(400, {"error": "empty_body"})
            return

        if length > MAX_POST_BYTES:
            self.send_json(413, {"error": "request_too_large"})
            return

        try:
            raw = self.rfile.read(length)
        except Exception as ex:
            log("read timeout/error: %s" % ex)
            self.send_json(408, {"error": "read_timeout"})
            return

        try:
            data = json.loads(raw.decode("utf-8"))
        except Exception:
            self.send_json(400, {"error": "bad_json"})
            return

        steamId = str(data.get("steamId", ""))
        serverKey = str(data.get("serverKey", ""))
        ts = str(data.get("ts", ""))
        overlay_b64 = data.get("overlayJsonB64", "")
        sig_client = str(data.get("sig", ""))

        if not steamId or not serverKey or not ts or not overlay_b64 or not sig_client:
            self.send_json(400, {"error": "missing_field"})
            return

        if not is_valid_steamid(steamId):
            self.send_json(400, {"error": "bad_steamid"})
            return

        if not server_key_to_path(serverKey):
            self.send_json(400, {"error": "bad_server_key"})
            return

        ok, err = check_timestamp(ts)
        if not ok:
            self.send_json(403, {"error": err})
            return

        sig_expected = build_upload_sig(steamId, serverKey, ts, overlay_b64)
        if not hmac.compare_digest(sig_expected, sig_client):
            log("bad_sig on /upload steamId=%s serverKey=%s overlay_b64_len=%s" %
                (steamId, serverKey, len(overlay_b64)))
            self.send_json(403, {"error": "bad_sig"})
            return

        try:
            overlay_bytes = base64.b64decode(overlay_b64.encode("utf-8"), validate=True)
        except Exception:
            self.send_json(400, {"error": "b64_invalid"})
            return

        if len(overlay_bytes) > MAX_OVERLAY_BYTES:
            self.send_json(413, {"error": "too_large"})
            return

        try:
            server_dir, file_path = safe_join_overlay_path(serverKey, steamId)
            ensure_dir(server_dir)
            atomic_write_bytes(file_path, overlay_bytes)
            # Update cache:
            RESOLVED_PATHS[steamId] = (file_path, time.time())
        except Exception as ex:
            log("io_error on /upload: %s" % ex)
            self.send_json(500, {"error": "io_error"})
            return

        self.send_json(200, {"status": "ok"})

    def handle_get(self):
        parsed = urllib.parse.urlparse(self.path)

        if parsed.path == "/health":
            self.send_json(200, {"status": "ok", "time": int(time.time())})
            return

        if parsed.path != "/fetch":
            self.send_json(404, {"error": "not_found"})
            return

        q = urllib.parse.parse_qs(parsed.query)

        steamId = q.get("steamId", [""])[0]
        serverKey = q.get("serverKey", [""])[0]
        ts = q.get("ts", [""])[0]
        sig_client = q.get("sig", [""])[0]

        if not steamId or not serverKey or not ts:
            self.send_json(400, {"error": "missing_field"})
            return

        if REQUIRE_FETCH_SIGNATURE and not sig_client:
            self.send_json(400, {"error": "missing_sig"})
            return

        if not is_valid_steamid(steamId):
            self.send_json(400, {"error": "bad_steamid"})
            return

        if not server_key_to_path(serverKey):
            self.send_json(400, {"error": "bad_server_key"})
            return

        ok, err = check_timestamp(ts)
        if not ok:
            self.send_json(403, {"error": err})
            return

        if REQUIRE_FETCH_SIGNATURE:
            sig_expected_fetch = build_fetch_sig(steamId, serverKey, ts)
            if not hmac.compare_digest(sig_expected_fetch, sig_client):
                log("bad_sig on /fetch steamId=%s serverKey=%s" % (steamId, serverKey))
                self.send_json(403, {"error": "bad_sig"})
                return

        try:
            _server_dir, file_path = safe_join_overlay_path(serverKey, steamId)
        except Exception:
            self.send_json(400, {"error": "bad_path"})
            return

        if not os.path.isfile(file_path):
            cached = RESOLVED_PATHS.get(steamId)
            if cached:
                fallback_path, _ = cached
                if os.path.isfile(fallback_path):
                    file_path = fallback_path
                    log("Fallback (cached): found file for steamId %s at %s" % (steamId, file_path))
                else:
                    RESOLVED_PATHS.pop(steamId, None)
                    self.send_json(404, {"error": "not_found"})
                    return
            else:
                self.send_json(404, {"error": "not_found"})
                return

        try:
            with open(file_path, "rb") as f:
                overlay_bytes = f.read()
        except Exception as ex:
            log("io_error on /fetch: %s" % ex)
            self.send_json(500, {"error": "io_error"})
            return

        if len(overlay_bytes) > MAX_OVERLAY_BYTES:
            self.send_json(500, {"error": "stored_file_too_large"})
            return

        overlay_b64 = base64.b64encode(overlay_bytes).decode("utf-8")

        # Response-Signatur bleibt wie fr�her:
        # Client kann pr�fen, dass die zur�ckgelieferten Daten vom Server kommen.
        sig_response = build_upload_sig(steamId, serverKey, ts, overlay_b64)

        resp = {
            "steamId": steamId,
            "serverKey": serverKey,
            "ts": ts,
            "overlayJsonB64": overlay_b64,
            "sig": sig_response
        }

        self.send_json(200, resp)

    def log_message(self, fmt, *args):
        try:
            msg = fmt % args

            # Querystrings aus GET-Logs entfernen, damit sig/steamId/serverKey nicht im Log landen
            if "GET " in msg:
                parts = msg.split(" ")
                if len(parts) >= 2:
                    url = parts[1]
                    parsed = urllib.parse.urlparse(url)
                    clean_url = parsed.path
                    parts[1] = clean_url
                    msg = " ".join(parts)

            msg_safe = msg.encode("ascii", "replace").decode("ascii")
            log("%s - %s" % (self.address_string(), msg_safe))
        except Exception:
            pass


# ============================================================
# SERVER
# ============================================================

class ThreadedReusableTCPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    ensure_dir(BASE_DIR)
    populate_resolved_paths()

    log("Overlay server starting on %s:%d" % (HOST, PORT))
    log("Base dir: %s" % BASE_DIR)
    log("Fetch signature required: %s" % REQUIRE_FETCH_SIGNATURE)

    httpd = ThreadedReusableTCPServer((HOST, PORT), OverlayHandler)

    try:
        log("Overlay server listening on %s:%d" % (HOST, PORT))
        httpd.serve_forever()
    except KeyboardInterrupt:
        log("Overlay server stopping due to KeyboardInterrupt")
    finally:
        try:
            httpd.shutdown()
        except Exception:
            pass

        try:
            httpd.server_close()
        except Exception:
            pass

        log("Overlay server stopped")


if __name__ == "__main__":
    main()