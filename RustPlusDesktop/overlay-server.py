#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import http.server
import socketserver
import json
import base64
import hmac
import hashlib
import os
import time
import urllib.parse

# === CONFIG ===
_shared_secret_hex = "23c5a7dbf02b63543da043ca7d6de1fbf706a080c899e334a8cd599206e13fde"

def _hex_to_bytes(h):
    return bytes.fromhex(h)

SHARED_SECRET = _hex_to_bytes(_shared_secret_hex)

BASE_DIR = "/home/rust-plus/data"
MAX_OVERLAY_BYTES = 350 * 1024
MAX_AGE_SECONDS = 60 * 60 * 24 * 28

# Hilfsfunktionen
def ensure_dir(path):
    if not os.path.isdir(path):
        os.makedirs(path, exist_ok=True)

def server_key_to_path(server_key):
    # Sicherheit: nur sehr eingeschränkte erlaubte Zeichen
    safe = "".join(ch for ch in server_key if ch.isalnum() or ch in ("-","_","."))
    return safe

def steamid_to_filename(steamid):
    safe = "".join(ch for ch in steamid if ch.isdigit())
    return safe + ".json"

def build_sig(steamid, server_key, ts, overlay_b64):
    msg = steamid + "|" + server_key + "|" + ts + "|" + overlay_b64
    mac = hmac.new(SHARED_SECRET, msg.encode("utf-8"), hashlib.sha256).hexdigest()
    return mac

class OverlayHandler(http.server.BaseHTTPRequestHandler):
    # kleine Helper um JSON-Response zu schicken
    def send_json(self, code, obj):
        body = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        # kein CORS nötig für dich lokal, aber schadet nicht:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    # wir erlauben nur zwei endpoints:
    # POST /upload
    # GET  /fetch?steamId=...&serverKey=...&ts=...&sig=...
    def do_POST(self):
        if self.path != "/upload":
            self.send_json(404, {"error": "not_found"})
            return

        # Body lesen
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length)

        try:
            data = json.loads(raw.decode("utf-8"))
        except Exception:
            self.send_json(400, {"error": "bad_json"})
            return

        # Felder holen
        steamId   = str(data.get("steamId", ""))
        serverKey = str(data.get("serverKey", ""))
        ts        = str(data.get("ts", ""))
        overlay_b64 = data.get("overlayJsonB64", "")
        sig_client  = str(data.get("sig", ""))

        # Plausicheck
        if not steamId or not serverKey or not ts or not overlay_b64 or not sig_client:
            self.send_json(400, {"error":"missing_field"})
            return

        # Timestamp check
        try:
            ts_int = int(ts)
        except:
            self.send_json(400, {"error":"bad_ts"})
            return

        now = int(time.time())
        if abs(now - ts_int) > MAX_AGE_SECONDS:
            print("DEBUG /upload: timestamp_out_of_range. now=%s ts=%s diff=%s"
                  % (now, ts_int, now - ts_int))
            self.send_json(403, {"error":"timestamp_out_of_range"})
            return

        # Signature check
        sig_expected = build_sig(steamId, serverKey, ts, overlay_b64)
        if not hmac.compare_digest(sig_expected, sig_client):
            print("DEBUG /upload: bad_sig")
            print("  steamId   =", steamId)
            print("  serverKey =", serverKey)
            print("  ts        =", ts)
            print("  overlay_b64_len =", len(overlay_b64))
            print("  sig_expected=", sig_expected)
            print("  sig_client  =", sig_client)
            self.send_json(403, {"error":"bad_sig"})
            return



        # Größe checken
        try:
            overlay_bytes = base64.b64decode(overlay_b64.encode("utf-8"), validate=True)
        except Exception:
            self.send_json(400, {"error":"b64_invalid"})
            return

        if len(overlay_bytes) > MAX_OVERLAY_BYTES:
            self.send_json(413, {"error":"too_large"})
            return

        # Zielpfad bauen
        safe_server = server_key_to_path(serverKey)
        ensure_dir(os.path.join(BASE_DIR, safe_server))
        file_path = os.path.join(BASE_DIR, safe_server, steamid_to_filename(steamId))

        # speichern
        try:
            with open(file_path, "wb") as f:
                f.write(overlay_bytes)
        except Exception as ex:
            self.send_json(500, {"error":"io_error","detail":str(ex)})
            return

        self.send_json(200, {"status":"ok"})

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != "/fetch":
            self.send_json(404, {"error": "not_found"})
            return

        q = urllib.parse.parse_qs(parsed.query)

        steamId   = q.get("steamId", [""])[0]
        serverKey = q.get("serverKey", [""])[0]
        ts        = q.get("ts", [""])[0]
        sig_client= q.get("sig", [""])[0]

        # Plausicheck
        if not steamId or not serverKey or not ts or not sig_client:
            self.send_json(400, {"error":"missing_field"})
            return

        # Timestamp check
        try:
            ts_int = int(ts)
        except:
            self.send_json(400, {"error":"bad_ts"})
            return

        now = int(time.time())
        if abs(now - ts_int) > MAX_AGE_SECONDS:
            self.send_json(403, {"error":"timestamp_out_of_range"})
            return

        # Datei lesen
        safe_server = server_key_to_path(serverKey)
        file_path = os.path.join(BASE_DIR, safe_server, steamid_to_filename(steamId))

        if not os.path.isfile(file_path):
            # nix gefunden
            self.send_json(404, {"error":"not_found"})
            return

        try:
            with open(file_path, "rb") as f:
                overlay_bytes = f.read()
        except Exception as ex:
            self.send_json(500, {"error":"io_error","detail":str(ex)})
            return

        # wieder b64 encoden
        overlay_b64 = base64.b64encode(overlay_bytes).decode("utf-8")

        # Signatur neu berechnen damit Client prüfen kann
        sig_expected = build_sig(steamId, serverKey, ts, overlay_b64)

        # Response
        resp = {
            "steamId": steamId,
            "serverKey": serverKey,
            "ts": ts,
            "overlayJsonB64": overlay_b64,
            "sig": sig_expected
        }
        self.send_json(200, resp)

    # unterdrück lautes Logging in der Konsole wenn du willst
    def log_message(self, format, *args):
        # kommentier aus für Silence
        print("[%s] %s" % (self.log_date_time_string(), format%args))

class ReusableTCPServer(socketserver.TCPServer):
    allow_reuse_address = True

if __name__ == "__main__":
    HOST = "0.0.0.0"
    PORT = 5000

    ensure_dir(BASE_DIR)

    print("Overlay server starting on %s:%d" % (HOST, PORT))

    httpd = ReusableTCPServer((HOST, PORT), OverlayHandler)
    print("Overlay server listening on %s:%d" % (HOST, PORT))
    httpd.serve_forever()
