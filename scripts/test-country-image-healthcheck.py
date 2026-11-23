"""Exercise the image probe against a task-owned, temporary loopback server."""

from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
import subprocess
from threading import Thread
import unittest


API = Path(__file__).resolve().parents[1] / "Legacy.Maliev.CountryService.Api"


class CountryImageHealthcheckTests(unittest.TestCase):
    def test_dockerfile_uses_the_tested_probe(self):
        dockerfile = (API / "Dockerfile").read_text()
        self.assertIn("COPY Legacy.Maliev.CountryService.Api/healthcheck.sh /app/healthcheck.sh", dockerfile)
        self.assertIn('CMD ["/bin/sh", "/app/healthcheck.sh", "8080"]', dockerfile)

    def test_only_exact_200_is_healthy(self):
        for status, expected_exit in ((200, 0), (204, 1), (307, 1), (503, 1)):
            with self.subTest(status=status):
                requests = []

                class Handler(BaseHTTPRequestHandler):
                    def do_GET(self):
                        requests.append(self.path)
                        self.send_response(status if self.path == "/countries/liveness" else 200)
                        if status == 307 and self.path == "/countries/liveness":
                            self.send_header("Location", "/redirect-target")
                        self.end_headers()

                    def log_message(self, *_args):
                        pass

                with HTTPServer(("127.0.0.1", 0), Handler) as server:
                    serving = Thread(target=server.serve_forever, daemon=True)
                    serving.start()
                    try:
                        result = subprocess.run(
                            ["/bin/sh", str(API / "healthcheck.sh"), str(server.server_port)],
                            capture_output=True,
                            timeout=5,
                            check=False,
                        )
                    finally:
                        server.shutdown()
                        serving.join(timeout=3)
                        self.assertFalse(serving.is_alive())
                self.assertEqual(expected_exit, result.returncode, result.stderr.decode())
                self.assertEqual(["/countries/liveness"], requests)


if __name__ == "__main__":
    unittest.main()
