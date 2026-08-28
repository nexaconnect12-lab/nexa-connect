import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

events = []
events_lock = Lock()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self._write(200, {"status": "healthy"})
            return
        if self.path == "/events":
            with events_lock:
                snapshot = list(events)
            self._write(200, snapshot)
            return
        self._write(404, {"error": "not_found"})

    def do_POST(self):
        if self.path != "/alerts":
            self._write(404, {"error": "not_found"})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length))
            normalized = {
                "status": payload.get("status"),
                "receiver": payload.get("receiver"),
                "alerts": [
                    {
                        "status": item.get("status"),
                        "alertname": item.get("labels", {}).get("alertname"),
                        "service": item.get("labels", {}).get("service"),
                        "severity": item.get("labels", {}).get("severity"),
                        "rehearsal_id": item.get("labels", {}).get("rehearsal_id"),
                    }
                    for item in payload.get("alerts", [])
                ],
            }
            with events_lock:
                events.append(normalized)
            print(json.dumps(normalized, separators=(",", ":")), flush=True)
            self._write(200, {"accepted": True})
        except (ValueError, TypeError, json.JSONDecodeError):
            self._write(400, {"error": "invalid_payload"})

    def log_message(self, format, *args):
        return

    def _write(self, status, payload):
        body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
