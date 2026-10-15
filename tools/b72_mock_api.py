"""Local synthetic fixture for manual B72 UI verification. No personal data."""

import json
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse


STATE = Path(__file__).with_name("b72_mock_state.json")
PRODUCTS = [
    {"id": "00000000-0000-4000-8000-000000000901", "name": "Kit fictício", "old": 29.9},
    {"id": "00000000-0000-4000-8000-000000000902", "name": "Lata fictícia", "old": 10.0},
]
ORDERS = {}


class Handler(BaseHTTPRequestHandler):
    def respond(self, status, value):
        body = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = urlparse(self.path).path
        if path.startswith("/api/v1/customers/") and path.endswith("/orders"):
            customer_id = path.split("/")[4]
            order_id = str(uuid.uuid5(uuid.NAMESPACE_DNS, "b72-demo-" + customer_id))
            items = [
                {
                    "productId": item["id"], "productName": item["name"],
                    "unitLabel": "unidade", "quantity": 1,
                    "unitPrice": item["old"], "subtotal": item["old"],
                }
                for item in PRODUCTS
            ]
            order = {
                "id": order_id, "protocol": "VB-TEST-B72",
                "createdAtUtc": "2026-10-13T12:00:00Z", "requestedDate": "2026-10-13",
                "fulfillmentType": "Pickup", "status": "Received", "total": 39.9,
                "version": 1, "clientRequestId": str(uuid.uuid5(uuid.NAMESPACE_DNS, order_id)),
                "customerId": customer_id, "customer": {"name": "Cliente fictício", "phone": "11999999999"},
                "paymentMethod": {"id": "00000000-0000-4000-8000-000000000903", "code": "Cash", "name": "Dinheiro"},
                "deliveryAddress": None, "notes": None, "cashTendered": None,
                "changeDue": 0, "deliveryFee": 0,
                "updatedAtUtc": "2026-10-13T12:00:00Z", "items": items,
                "statusHistory": [{"sequence": 1, "fromStatus": None, "toStatus": "Received",
                                   "occurredAtUtc": "2026-10-13T12:00:00Z", "reason": None}],
            }
            ORDERS[order_id] = order
            summary = {key: order[key] for key in
                       ("id", "protocol", "createdAtUtc", "requestedDate", "fulfillmentType", "status", "total", "version")}
            self.respond(200, {"items": [summary], "page": 1, "pageSize": 20, "total": 1})
        elif path.startswith("/api/v1/orders/"):
            order = ORDERS.get(path.rsplit("/", 1)[-1])
            self.respond(200 if order else 404, order or {"code": "NotFound"})
        elif path.startswith("/api/v1/products/"):
            product_id = path.rsplit("/", 1)[-1]
            state = json.loads(STATE.read_text(encoding="utf-8"))
            product = next((item for item in PRODUCTS if item["id"] == product_id), None)
            if product is None or (product is PRODUCTS[1] and not state["secondAvailable"]):
                self.respond(404, {"code": "NotFound"})
                return
            price = state["firstPrice"] if product is PRODUCTS[0] else product["old"]
            self.respond(200, {
                "id": product_id, "categoryId": "00000000-0000-4000-8000-000000000904",
                "sku": "VB-TEST", "name": product["name"], "description": None,
                "unitLabel": "unidade", "price": price, "imageUrl": None,
                "isAvailable": True, "isActive": True, "version": 2,
                "updatedAtUtc": "2026-10-13T12:00:00Z",
            })
        else:
            self.respond(404, {"code": "NotFound"})

    def do_POST(self):
        if urlparse(self.path).path != "/api/v1/customers":
            self.respond(404, {"code": "NotFound"})
            return
        request = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
        self.respond(201, {
            "id": "00000000-0000-4000-8000-000000000906",
            "customerType": request["customerType"], "fullName": request["fullName"],
            "companyName": request.get("companyName"), "cpf": request.get("cpf"),
            "cnpj": request.get("cnpj"), "phone": request["phone"],
            "email": request.get("email"), "address": request.get("address"),
            "version": 1, "createdAtUtc": "2026-10-13T12:00:00Z",
            "updatedAtUtc": "2026-10-13T12:00:00Z",
        })

    def log_message(self, format, *args):
        pass


ThreadingHTTPServer(("127.0.0.1", 5080), Handler).serve_forever()
