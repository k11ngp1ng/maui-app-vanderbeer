"""Forward local HTTP requests, dropping one selected response after API commit.

For an isolated B80 integration test only. The request and response bodies are
never logged; stdout prints the upstream HTTP status for verification.
"""

import argparse
import http.client
import socket
from http.server import BaseHTTPRequestHandler, HTTPServer


class DropResponseHandler(BaseHTTPRequestHandler):
    drop_next_patch = True
    drop_next_orders_post = False

    def do_GET(self):
        self.forward("GET")

    def do_POST(self):
        self.forward("POST")

    def do_PUT(self):
        self.forward("PUT")

    def do_PATCH(self):
        self.forward("PATCH")

    def forward(self, method):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        headers = {key: value for key, value in self.headers.items()
                   if key.lower() not in ("host", "connection", "content-length")}
        upstream = http.client.HTTPConnection("127.0.0.1", 5080, timeout=15)
        try:
            upstream.request(method, self.path, body=body, headers=headers)
            response = upstream.getresponse()
            content = response.read()
            drop_patch = method == "PATCH" and self.server.drop_next_patch
            drop_order = (method == "POST" and self.path == "/api/v1/orders"
                          and self.server.drop_next_orders_post)
            if drop_patch or drop_order:
                if drop_patch:
                    self.server.drop_next_patch = False
                if drop_order:
                    self.server.drop_next_orders_post = False
                label = "patch" if drop_patch else "orders_post"
                print(f"dropped_{label}_upstream_status={response.status}", flush=True)
                try:
                    self.connection.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                self.connection.close()
                return
            self.send_response(response.status)
            for key in ("Content-Type", "Location", "Idempotent-Replay"):
                if value := response.getheader(key):
                    self.send_header(key, value)
            self.send_header("Content-Length", str(len(content)))
            self.end_headers()
            self.wfile.write(content)
            print(f"forwarded_{method.lower()}_status={response.status}", flush=True)
        finally:
            upstream.close()

    def log_message(self, format, *args):
        pass


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5081)
    parser.add_argument("--continuous", action="store_true")
    parser.add_argument("--drop-post-orders", action="store_true")
    args = parser.parse_args()
    server = HTTPServer(("127.0.0.1", args.port), DropResponseHandler)
    server.timeout = 20
    server.drop_next_patch = True
    server.drop_next_orders_post = args.drop_post_orders
    try:
        if args.continuous:
            server.serve_forever()
        else:
            server.handle_request()
    finally:
        server.server_close()
