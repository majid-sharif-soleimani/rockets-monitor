"""Records every message the rockets program posts (one JSON per line) and answers 202."""
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

out = open(sys.argv[2], "w")
lock = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers["Content-Length"]))
        with lock:
            out.write(body.decode().replace("\n", "") + "\n")
            out.flush()
        self.send_response(202)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, *args):
        pass


ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
